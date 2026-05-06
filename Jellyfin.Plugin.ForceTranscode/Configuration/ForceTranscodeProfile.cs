using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.ForceTranscode.Configuration;

/// <summary>
/// Maps a Jellyfin user and a set of devices to a forced-transcode rule.
/// When the user plays video on any listed device, the selected source codecs
/// are stripped from the device profile so the server transcodes them to the
/// chosen target codec instead of direct-playing or remuxing.
/// </summary>
public class ForceTranscodeProfile
{
    /// <summary>Gets or sets the unique profile identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Gets or sets the display name for this profile.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the Jellyfin user this profile applies to.</summary>
    public Guid UserId { get; set; }

    /// <summary>Gets or sets the device IDs this profile applies to.</summary>
    public List<string> DeviceIds { get; set; } = new List<string>();

    /// <summary>
    /// Gets or sets the video codec to transcode to (e.g. "h264", "hevc", "av1", "vp9").
    /// Defaults to "h264".
    /// </summary>
    public string TargetVideoCodec { get; set; } = "h264";

    /// <summary>
    /// Gets or sets the source video codecs that should be intercepted and force-transcoded.
    /// Each entry is a canonical codec key; aliases (e.g. h265 for hevc) are resolved
    /// automatically in the filter. Defaults to ["hevc"].
    /// </summary>
    public List<string> SourceVideoCodecs { get; set; } = new List<string> { "hevc" };
}
