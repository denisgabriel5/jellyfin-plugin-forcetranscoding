using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Api.Models.MediaInfoDtos;
using Jellyfin.Plugin.ForceTranscode.Configuration;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Dlna;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ForceTranscode.Filters;

/// <summary>
/// Global MVC action filter that intercepts <c>GetPostedPlaybackInfo</c> requests.
/// For requests whose user+device match a configured profile, the device profile is
/// patched so that the selected source codecs are force-transcoded to the target codec
/// instead of being direct-played or remuxed.
/// </summary>
public class ForceTranscodeActionFilter : IAsyncActionFilter
{
    private const string TargetController = "Jellyfin.Api.Controllers.MediaInfoController";
    private const string TargetAction = "GetPostedPlaybackInfo";

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
        if (IsPlaybackInfoAction(context))
        {
            await TryApplyAsync(context).ConfigureAwait(false);
        }

        await next().ConfigureAwait(false);
    }

    private static bool IsPlaybackInfoAction(ActionExecutingContext context)
        => context.ActionDescriptor is ControllerActionDescriptor d
           && d.ControllerTypeInfo.FullName == TargetController
           && d.ActionName == TargetAction;

    private async Task TryApplyAsync(ActionExecutingContext context)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || config.Profiles.Count == 0)
        {
            return;
        }

        try
        {
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
                "[ForceTranscode] Profile patched — {Dp} DirectPlay profiles, {Tp} TranscodingProfiles; source codecs removed, target → {Target}",
                dto.DeviceProfile.DirectPlayProfiles?.Length ?? 0,
                dto.DeviceProfile.TranscodingProfiles?.Length ?? 0,
                targetCodec);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ForceTranscode] Failed to apply override; playback proceeds normally.");
        }
    }
}
