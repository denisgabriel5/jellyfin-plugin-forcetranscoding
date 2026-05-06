using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.ForceTranscode.Configuration;

/// <summary>
/// Plugin configuration stored as XML by Jellyfin.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the list of force-transcode profiles.
    /// </summary>
    public List<ForceTranscodeProfile> Profiles { get; set; } = new List<ForceTranscodeProfile>();
}
