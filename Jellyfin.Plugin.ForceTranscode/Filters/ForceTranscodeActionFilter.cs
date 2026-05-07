using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Api.Helpers;
using Jellyfin.Api.Models.MediaInfoDtos;
using Jellyfin.Plugin.ForceTranscode.Configuration;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.MediaInfo;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ForceTranscode.Filters;

/// <summary>
/// Global MVC action filter that intercepts both <c>GetPostedPlaybackInfo</c> (POST)
/// and <c>GetPlaybackInfo</c> (GET) requests. For requests whose device+user match a
/// configured profile the filter either patches the input device profile (POST) or
/// calls SetDeviceSpecificData on the response (GET) so the server generates a real
/// TranscodingUrl pointing to the configured target codec.
/// </summary>
public class ForceTranscodeActionFilter : IAsyncActionFilter
{
    private const string TargetController = "Jellyfin.Api.Controllers.MediaInfoController";
    private const string PostAction       = "GetPostedPlaybackInfo";
    private const string GetAction        = "GetPlaybackInfo";

    private readonly IAuthorizationContext _authContext;
    private readonly IDeviceManager _deviceManager;
    private readonly MediaInfoHelper _mediaInfoHelper;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<ForceTranscodeActionFilter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ForceTranscodeActionFilter"/> class.
    /// </summary>
    public ForceTranscodeActionFilter(
        IAuthorizationContext authContext,
        IDeviceManager deviceManager,
        MediaInfoHelper mediaInfoHelper,
        ILibraryManager libraryManager,
        ILogger<ForceTranscodeActionFilter> logger)
    {
        _authContext     = authContext;
        _deviceManager   = deviceManager;
        _mediaInfoHelper = mediaInfoHelper;
        _libraryManager  = libraryManager;
        _logger          = logger;
    }

    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var actionName = GetActionName(context);

        if (actionName == PostAction)
        {
            await TryApplyPostAsync(context).ConfigureAwait(false);
            await next().ConfigureAwait(false);
        }
        else if (actionName == GetAction)
        {
            await TryApplyGetAsync(context, next).ConfigureAwait(false);
        }
        else
        {
            await next().ConfigureAwait(false);
        }
    }

    private static string? GetActionName(ActionExecutingContext context)
    {
        if (context.ActionDescriptor is ControllerActionDescriptor d
            && d.ControllerTypeInfo.FullName == TargetController)
        {
            return d.ActionName;
        }

        return null;
    }

    private async Task<(bool matched, ForceTranscodeProfile? profile, string deviceId, Guid userId)>
        TryMatchProfileAsync(ActionExecutingContext context)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || config.Profiles.Count == 0)
        {
            return (false, null, string.Empty, Guid.Empty);
        }

        var authInfo = await _authContext.GetAuthorizationInfo(context.HttpContext.Request)
            .ConfigureAwait(false);

        var userId   = authInfo.UserId;
        var deviceId = authInfo.DeviceId ?? string.Empty;

        _logger.LogInformation(
            "[ForceTranscode] PlaybackInfo — user {UserId}, device {DeviceId}",
            userId,
            deviceId);

        var match = config.Profiles.FirstOrDefault(p =>
            p.DeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase) &&
            p.UserIds.Contains(userId));

        if (match is null)
        {
            _logger.LogInformation(
                "[ForceTranscode] No matching profile for user {UserId} on device {DeviceId} — skipping",
                userId,
                deviceId);
            return (false, null, deviceId, userId);
        }

        return (true, match, deviceId, userId);
    }

    private async Task TryApplyPostAsync(ActionExecutingContext context)
    {
        try
        {
            var (matched, match, deviceId, userId) = await TryMatchProfileAsync(context).ConfigureAwait(false);
            if (!matched || match is null)
            {
                return;
            }

            IReadOnlyList<string> sourceCodecs = match.SourceVideoCodecs is { Count: > 0 }
                ? match.SourceVideoCodecs
                : new List<string> { "hevc" };

            var targetCodec = string.IsNullOrWhiteSpace(match.TargetVideoCodec)
                ? "h264"
                : match.TargetVideoCodec;

            _logger.LogInformation(
                "[ForceTranscode] Matched profile '{Name}' — [{Sources}] → {Target} for user {UserId} on device {DeviceId}",
                match.Name,
                string.Join(", ", sourceCodecs),
                targetCodec,
                userId,
                deviceId);

            context.ActionArguments.TryGetValue("playbackInfoDto", out var arg);
            var dto = arg as PlaybackInfoDto ?? new PlaybackInfoDto();
            if (arg is null)
            {
                context.ActionArguments["playbackInfoDto"] = dto;
            }

            if (dto.DeviceProfile is null)
            {
                var caps = _deviceManager.GetCapabilities(deviceId);
                dto.DeviceProfile = caps?.DeviceProfile ?? new DeviceProfile();
            }

            CodecHelper.ApplyForceTranscode(dto.DeviceProfile, targetCodec, sourceCodecs);

            _logger.LogInformation(
                "[ForceTranscode] POST profile patched — {Dp} DirectPlay profiles, {Tp} TranscodingProfiles; source codecs removed, target → {Target}",
                dto.DeviceProfile.DirectPlayProfiles?.Length ?? 0,
                dto.DeviceProfile.TranscodingProfiles?.Length ?? 0,
                targetCodec);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ForceTranscode] Failed to apply POST override; playback proceeds normally.");
        }
    }

    private async Task TryApplyGetAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // Resolve profile match before running the action so we log once.
        var (matched, match, deviceId, userId) = await TryMatchProfileAsync(context).ConfigureAwait(false);

        // Always run the original GET action — it is lightweight (no transcoding decisions).
        var executed = await next().ConfigureAwait(false);

        if (!matched || match is null)
        {
            return;
        }

        try
        {
            if (executed.Result is not ObjectResult { Value: PlaybackInfoResponse playbackInfo })
            {
                return;
            }

            IReadOnlyList<string> sourceCodecs = match.SourceVideoCodecs is { Count: > 0 }
                ? match.SourceVideoCodecs
                : new List<string> { "hevc" };

            var targetCodec = string.IsNullOrWhiteSpace(match.TargetVideoCodec)
                ? "h264"
                : match.TargetVideoCodec;

            // Build a patched device profile: clone the registered caps (or start
            // fresh if InfuseSync / similar hasn't registered one) and apply our
            // codec overrides so StreamBuilder produces an H264 HLS TranscodingUrl.
            var caps = _deviceManager.GetCapabilities(deviceId);
            var patchedProfile = caps?.DeviceProfile is not null
                ? JsonSerializer.Deserialize<DeviceProfile>(
                    JsonSerializer.SerializeToUtf8Bytes(caps.DeviceProfile))!
                : new DeviceProfile();

            CodecHelper.ApplyForceTranscode(patchedProfile, targetCodec, sourceCodecs);

            // Retrieve the item so SetDeviceSpecificData can determine audio vs video
            // and check user transcoding permissions.
            var itemId = context.ActionArguments.TryGetValue("itemId", out var raw) && raw is Guid g
                ? g
                : Guid.Empty;

            var item = itemId != Guid.Empty
                ? _libraryManager.GetItemById(itemId)
                : null;

            if (item is null)
            {
                _logger.LogWarning(
                    "[ForceTranscode] GET: could not resolve item {ItemId}; skipping SetDeviceSpecificData.",
                    itemId);
                return;
            }

            var playSessionId = playbackInfo.PlaySessionId
                ?? Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture);
            var ipAddress     = context.HttpContext.GetNormalizedRemoteIP();
            var patchedCount  = 0;

            foreach (var source in playbackInfo.MediaSources ?? [])
            {
                var codec = source.VideoStream?.Codec;
                if (codec is null || !CodecHelper.IsCodecForbidden(codec, sourceCodecs))
                {
                    continue;
                }

                _mediaInfoHelper.SetDeviceSpecificData(
                    item,
                    source,
                    patchedProfile,
                    context.HttpContext.User,
                    maxBitrate: null,
                    startTimeTicks: 0,
                    mediaSourceId: source.Id ?? string.Empty,
                    audioStreamIndex: null,
                    subtitleStreamIndex: null,
                    maxAudioChannels: null,
                    playSessionId: playSessionId,
                    userId: userId,
                    enableDirectPlay: false,
                    enableDirectStream: false,
                    enableTranscoding: true,
                    allowVideoStreamCopy: true,
                    allowAudioStreamCopy: true,
                    alwaysBurnInSubtitleWhenTranscoding: false,
                    ipAddress: ipAddress);

                patchedCount++;
            }

            _logger.LogInformation(
                "[ForceTranscode] GET: SetDeviceSpecificData applied to {Count} source(s) for profile '{Name}' — [{Sources}] → {Target}",
                patchedCount,
                match.Name,
                string.Join(", ", sourceCodecs),
                targetCodec);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ForceTranscode] Failed to apply GET override; playback proceeds normally.");
        }
    }
}
