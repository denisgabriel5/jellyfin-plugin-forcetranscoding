using Jellyfin.Plugin.ForceTranscode.Filters;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.ForceTranscode;

/// <summary>
/// Registers the plugin's services with the Jellyfin dependency injection container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddTransient<ForceTranscodeActionFilter>();

        serviceCollection.Configure<MvcOptions>(options =>
        {
            options.Filters.AddService<ForceTranscodeActionFilter>();
        });
    }
}
