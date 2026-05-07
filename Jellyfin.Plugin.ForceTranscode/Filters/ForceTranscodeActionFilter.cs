using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Api.Models.MediaInfoDtos;
using Jellyfin.Plugin.ForceTranscode.Configuration;
using MediaBrowser.Controller.Devices;
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
/// strips direct-play/direct-stream from the response (GET) so the server transcodes
/// the selected source codecs to the configured target codec.
/// </summary>
public class ForceTranscodeActionFilter : IAsyncActionFilter
{
    private const string TargetController  = "Jellyfin.Api.Controllers.MediaInfoController";
    private const string PostAction        = "GetPostedPlaybackInfo";
    private const string GetAction         = "GetPlaybackInfo";

    private readonly IAuthorizationContext _authContext;
    private readonly IDeviceManager _deviceManager;
    private readonly ILogger<ForceTranscodeActionFilter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ForceTranscodeActionFilter"/> class.
    /// </summary>
    public ForceTranscodeActionFilter(
        IAuthorizationContext authContext,
        IDeviceManager deviceManager,
        ILogger<ForceTranscodeActionFilter> logger)
    {
        _authContext = authContext;
        _deviceManager = deviceManager;
        _logger = logger;
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
            var executed = await next().ConfigureAwait(false);
            await TryApplyGetResultAsync(context, executed).ConfigureAwait(false);
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

    private async Task<(bool matched, ForceTranscodeProfile? profile, string deviceId, Guid userId)> TryMatchProfileAsync(
        ActionExecutingContext context)
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

    private async Task TryApplyGetResultAsync(ActionExecutingContext context, ActionExecutedContext executed)
    {
        try
        {
            var (matched, match, _, _) = await TryMatchProfileAsync(context).ConfigureAwait(false);
            if (!matched || match is null)
            {
                return;
            }

            IReadOnlyList<string> sourceCodecs = match.SourceVideoCodecs is { Count: > 0 }
                ? match.SourceVideoCodecs
                : new List<string> { "hevc" };

            if (executed.Result is not ObjectResult { Value: PlaybackInfoResponse playbackInfo })
            {
                return;
            }

            var patched = 0;
            foreach (var source in playbackInfo.MediaSources ?? [])
            {
                var codec = source.VideoStream?.Codec;
                if (codec != null && CodecHelper.IsCodecForbidden(codec, sourceCodecs))
                {
                    source.SupportsDirectPlay   = false;
                    source.SupportsDirectStream = false;
                    patched++;
                }
            }

            _logger.LogInformation(
                "[ForceTranscode] GET response patched — {Count} source(s) had DirectPlay/DirectStream disabled for profile '{Name}'",
                patched,
                match.Name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ForceTranscode] Failed to apply GET override; playback proceeds normally.");
        }
    }
}
