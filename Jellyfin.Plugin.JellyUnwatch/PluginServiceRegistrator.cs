using Jellyfin.Plugin.JellyUnwatch.EventHandlers;
using Jellyfin.Plugin.JellyUnwatch.Filters;
using Jellyfin.Plugin.JellyUnwatch.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.JellyUnwatch;

public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<HiddenItemStore>();
        serviceCollection.AddScoped<IEventConsumer<PlaybackStartEventArgs>, PlaybackStartConsumer>();
        serviceCollection.AddHostedService<PluginEntryPoint>();
        serviceCollection.Configure<MvcOptions>(options => options.Filters.Add<ResumeVisibilityFilter>());
    }
}
