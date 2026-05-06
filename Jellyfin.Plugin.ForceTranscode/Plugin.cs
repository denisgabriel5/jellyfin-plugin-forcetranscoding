using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.ForceTranscode.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.ForceTranscode;

/// <summary>
/// Force H.264 Transcode plugin — intercepts playback info requests and removes
/// HEVC/H.265 from the device profile for configured user+device pairs, causing
/// Jellyfin to transcode HEVC content to H.264 rather than direct-play or remux it.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "Force Transcode";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("3c5f2d1a-8b4e-4f7c-a92d-1e6b0f3d9c2a");

    /// <inheritdoc />
    public override string Description =>
        "Force-transcodes selected video codecs to a target codec for specific user and device combinations, bypassing direct-play and remux paths.";

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}.Configuration.configPage.html",
                    GetType().Namespace)
            }
        ];
    }
}
