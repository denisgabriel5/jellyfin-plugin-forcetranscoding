using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.ForceTranscode;
using MediaBrowser.Model.Dlna;
using Xunit;

namespace Jellyfin.Plugin.ForceTranscode.Tests;

public class CodecHelperTests
{
    // ── Alias resolution ────────────────────────────────────────────────────

    [Theory]
    [InlineData("hevc",  new[] { "hevc", "h265", "dvhe", "dvh1" })]
    [InlineData("h265",  new[] { "hevc", "h265", "dvhe", "dvh1" })]
    [InlineData("av1",   new[] { "av1" })]
    [InlineData("vp9",   new[] { "vp9" })]
    [InlineData("h264",  new[] { "h264", "avc" })]
    public void ResolveAliases_ReturnsExpectedSet(string codec, string[] expected)
    {
        var result = CodecHelper.ResolveAliases(codec).ToArray();
        Assert.Equal(expected.OrderBy(x => x), result.OrderBy(x => x));
    }

    [Fact]
    public void ResolveAliases_UnknownCodec_ReturnsLowercasedKey()
    {
        var result = CodecHelper.ResolveAliases("MYCODEC").ToArray();
        Assert.Equal(new[] { "mycodec" }, result);
    }

    // ── DirectPlay profile patching ─────────────────────────────────────────

    [Fact]
    public void ApplyForceTranscode_RemovesHevcFromExplicitDirectPlayList()
    {
        var profile = new DeviceProfile
        {
            DirectPlayProfiles = new[]
            {
                new DirectPlayProfile { Type = DlnaProfileType.Video, VideoCodec = "h264,hevc,vp9" }
            },
            TranscodingProfiles = new[]
            {
                new TranscodingProfile { Type = DlnaProfileType.Video, VideoCodec = "h264,hevc" }
            }
        };

        CodecHelper.ApplyForceTranscode(profile, "h264", new[] { "hevc" });

        Assert.Equal("h264,vp9", profile.DirectPlayProfiles[0].VideoCodec);
    }

    [Fact]
    public void ApplyForceTranscode_NullVideoCodec_UsesNegativeList()
    {
        var profile = new DeviceProfile
        {
            DirectPlayProfiles = new[]
            {
                new DirectPlayProfile { Type = DlnaProfileType.Video, VideoCodec = null }
            },
            TranscodingProfiles = new[]
            {
                new TranscodingProfile { Type = DlnaProfileType.Video, VideoCodec = "h264" }
            }
        };

        CodecHelper.ApplyForceTranscode(profile, "h264", new[] { "hevc" });

        // Should produce a negative exclusion list covering all hevc aliases
        var codecs = profile.DirectPlayProfiles[0].VideoCodec!;
        Assert.Contains("-hevc", codecs);
        Assert.Contains("-h265", codecs);
        Assert.Contains("-dvhe", codecs);
        Assert.Contains("-dvh1", codecs);
    }

    [Fact]
    public void ApplyForceTranscode_DoesNotTouchAudioDirectPlayProfiles()
    {
        var profile = new DeviceProfile
        {
            DirectPlayProfiles = new[]
            {
                new DirectPlayProfile { Type = DlnaProfileType.Audio, VideoCodec = "hevc" }
            },
            TranscodingProfiles = Array.Empty<TranscodingProfile>()
        };

        CodecHelper.ApplyForceTranscode(profile, "h264", new[] { "hevc" });

        // Audio profile must be untouched
        Assert.Equal("hevc", profile.DirectPlayProfiles[0].VideoCodec);
    }

    [Fact]
    public void ApplyForceTranscode_RemovesDolbyVisionAliases()
    {
        var profile = new DeviceProfile
        {
            DirectPlayProfiles = new[]
            {
                new DirectPlayProfile { Type = DlnaProfileType.Video, VideoCodec = "h264,dvhe,dvh1,hevc" }
            },
            TranscodingProfiles = new[]
            {
                new TranscodingProfile { Type = DlnaProfileType.Video, VideoCodec = "hevc" }
            }
        };

        CodecHelper.ApplyForceTranscode(profile, "h264", new[] { "hevc" });

        // All HEVC aliases must be removed
        var remaining = profile.DirectPlayProfiles[0].VideoCodec!
            .Split(',')
            .ToHashSet();
        Assert.DoesNotContain("dvhe",  remaining);
        Assert.DoesNotContain("dvh1",  remaining);
        Assert.DoesNotContain("hevc",  remaining);
        Assert.Contains("h264", remaining);
    }

    // ── TranscodingProfile patching ─────────────────────────────────────────

    [Fact]
    public void ApplyForceTranscode_SetsTargetCodecOnAllVideoTranscodingProfiles()
    {
        var profile = new DeviceProfile
        {
            DirectPlayProfiles = Array.Empty<DirectPlayProfile>(),
            TranscodingProfiles = new[]
            {
                new TranscodingProfile { Type = DlnaProfileType.Video, VideoCodec = "hevc,h264" },
                new TranscodingProfile { Type = DlnaProfileType.Video, VideoCodec = "vp9"       },
                new TranscodingProfile { Type = DlnaProfileType.Audio, VideoCodec = "hevc"      }
            }
        };

        CodecHelper.ApplyForceTranscode(profile, "h264", new[] { "hevc" });

        Assert.Equal("h264", profile.TranscodingProfiles[0].VideoCodec);
        Assert.Equal("h264", profile.TranscodingProfiles[1].VideoCodec);
        // Audio profile untouched
        Assert.Equal("hevc", profile.TranscodingProfiles[2].VideoCodec);
    }

    [Fact]
    public void ApplyForceTranscode_EmptyTranscodingProfiles_AddsHlsFallback()
    {
        var profile = new DeviceProfile
        {
            DirectPlayProfiles  = Array.Empty<DirectPlayProfile>(),
            TranscodingProfiles = Array.Empty<TranscodingProfile>()
        };

        CodecHelper.ApplyForceTranscode(profile, "av1", new[] { "hevc" });

        Assert.Single(profile.TranscodingProfiles);
        Assert.Equal("av1", profile.TranscodingProfiles[0].VideoCodec);
        Assert.Equal(DlnaProfileType.Video, profile.TranscodingProfiles[0].Type);
        Assert.Equal(MediaStreamProtocol.hls, profile.TranscodingProfiles[0].Protocol);
    }

    [Fact]
    public void ApplyForceTranscode_MultipleSourceCodecs_AllRemoved()
    {
        var profile = new DeviceProfile
        {
            DirectPlayProfiles = new[]
            {
                new DirectPlayProfile { Type = DlnaProfileType.Video, VideoCodec = "h264,hevc,av1,vp9" }
            },
            TranscodingProfiles = new[]
            {
                new TranscodingProfile { Type = DlnaProfileType.Video, VideoCodec = "hevc,av1,vp9" }
            }
        };

        CodecHelper.ApplyForceTranscode(profile, "h264", new[] { "hevc", "av1", "vp9" });

        Assert.Equal("h264", profile.DirectPlayProfiles[0].VideoCodec);
        Assert.Equal("h264", profile.TranscodingProfiles[0].VideoCodec);
    }

    [Fact]
    public void ApplyForceTranscode_CustomTargetCodec_AppliedCorrectly()
    {
        var profile = new DeviceProfile
        {
            DirectPlayProfiles = new[]
            {
                new DirectPlayProfile { Type = DlnaProfileType.Video, VideoCodec = "hevc,h264" }
            },
            TranscodingProfiles = new[]
            {
                new TranscodingProfile { Type = DlnaProfileType.Video, VideoCodec = "hevc" }
            }
        };

        CodecHelper.ApplyForceTranscode(profile, "av1", new[] { "hevc" });

        Assert.DoesNotContain("hevc", profile.DirectPlayProfiles[0].VideoCodec);
        Assert.Equal("av1", profile.TranscodingProfiles[0].VideoCodec);
    }
}
