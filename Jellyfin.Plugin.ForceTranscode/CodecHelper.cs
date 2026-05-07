using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.Dlna;

namespace Jellyfin.Plugin.ForceTranscode;

/// <summary>
/// Pure static helpers for manipulating <see cref="DeviceProfile"/> codec lists.
/// Extracted from the action filter so they can be unit-tested independently.
/// </summary>
public static class CodecHelper
{
    /// <summary>
    /// Maps each canonical codec name to the full set of names/aliases Jellyfin
    /// may use for that codec in device profiles.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> Aliases =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["hevc"]       = new[] { "hevc", "h265", "dvhe", "dvh1" },
            ["h265"]       = new[] { "hevc", "h265", "dvhe", "dvh1" },
            ["h264"]       = new[] { "h264", "avc"  },
            ["avc"]        = new[] { "h264", "avc"  },
            ["av1"]        = new[] { "av1"           },
            ["vp9"]        = new[] { "vp9"           },
            ["vp8"]        = new[] { "vp8"           },
            ["mpeg2video"] = new[] { "mpeg2video"    },
        };

    /// <summary>
    /// Returns all codec name aliases for the given canonical key, or just
    /// the lowercased key itself if it is unknown.
    /// </summary>
    public static IEnumerable<string> ResolveAliases(string codec)
        => Aliases.TryGetValue(codec, out var a) ? a : new[] { codec.ToLowerInvariant() };

    /// <summary>
    /// Returns true if <paramref name="codec"/> (or any of its aliases) matches
    /// any of the entries in <paramref name="sourceCodecs"/>.
    /// </summary>
    public static bool IsCodecForbidden(string codec, IEnumerable<string> sourceCodecs)
    {
        var normalized = codec.ToLowerInvariant();
        foreach (var src in sourceCodecs)
        {
            foreach (var alias in ResolveAliases(src))
            {
                if (string.Equals(alias, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Patches <paramref name="profile"/> so that:
    /// <list type="bullet">
    ///   <item>All <paramref name="sourceCodecs"/> (plus their aliases) are removed from
    ///   every video <see cref="DirectPlayProfile"/>.</item>
    ///   <item>Every video <see cref="TranscodingProfile"/> targets
    ///   <paramref name="targetCodec"/> exclusively.</item>
    /// </list>
    /// A fallback HLS transcoding profile is added when none exist.
    /// </summary>
    public static void ApplyForceTranscode(
        DeviceProfile profile,
        string targetCodec,
        IEnumerable<string> sourceCodecs)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetCodec);

        var toRemove = new HashSet<string>(
            (sourceCodecs ?? Enumerable.Empty<string>()).SelectMany(ResolveAliases),
            StringComparer.OrdinalIgnoreCase);

        // ── Strip source codecs from DirectPlay ──────────────────────────────
        foreach (var dp in profile.DirectPlayProfiles ?? [])
        {
            if (dp.Type != DlnaProfileType.Video)
            {
                continue;
            }

            if (string.IsNullOrEmpty(dp.VideoCodec))
            {
                // Null/empty means "all codecs" — switch to a negative exclusion list.
                dp.VideoCodec = string.Join(',', toRemove.Select(c => "-" + c));
            }
            else
            {
                var kept = dp.VideoCodec
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Where(c => !toRemove.Contains(c.TrimStart('-')))
                    .ToArray();
                dp.VideoCodec = string.Join(',', kept);
            }
        }

        // ── Force target codec in TranscodingProfiles ────────────────────────
        if (profile.TranscodingProfiles is { Length: > 0 })
        {
            foreach (var tp in profile.TranscodingProfiles)
            {
                if (tp.Type == DlnaProfileType.Video)
                {
                    tp.VideoCodec = targetCodec;
                }
            }
        }
        else
        {
            // No video transcoding profile — add a minimal HLS one so StreamBuilder
            // has a transcode path to fall back on.
            profile.TranscodingProfiles =
            [
                new TranscodingProfile
                {
                    Container  = "ts",
                    Type       = DlnaProfileType.Video,
                    VideoCodec = targetCodec,
                    AudioCodec = "aac,mp3,ac3",
                    Protocol   = MediaStreamProtocol.hls,
                    Context    = EncodingContext.Streaming
                }
            ];
        }
    }
}
