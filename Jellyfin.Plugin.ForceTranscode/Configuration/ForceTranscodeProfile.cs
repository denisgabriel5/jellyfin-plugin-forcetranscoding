using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.ForceTranscode.Configuration;

/// <summary>
/// Maps a device to a set of Jellyfin users for a forced-transcode rule.
/// When any listed user plays video on that device, the selected source codecs
/// are stripped from the device profile so the server transcodes them to the
/// chosen target codec instead of direct-playing or remuxing.
/// </summary>
public class ForceTranscodeProfile
{
    /// <summary>Gets or sets the unique profile identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Gets or sets the display name for this profile.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the device this profile applies to.</summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>Gets or sets the Jellyfin users this profile applies to.</summary>
    public List<Guid> UserIds { get; set; } = new List<Guid>();

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
