using Jellyfin.Plugin.JellyUnwatch.Web;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyUnwatch;

public sealed class PluginEntryPoint : IHostedService
{
    private static readonly TimeSpan[] RetryDelays =
    {
        TimeSpan.Zero,
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(20)
    };

    private readonly ILogger<PluginEntryPoint> _logger;

    public PluginEntryPoint(ILogger<PluginEntryPoint> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!(Plugin.Instance?.Configuration.EnableWebButton ?? true))
        {
            _logger.LogInformation("The web button is disabled, skipping the index.html transformation");
            return Task.CompletedTask;
        }

        _ = Task.Run(() => RegisterWithRetries(cancellationToken), cancellationToken);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        FileTransformationRegistrar.Unregister();
        return Task.CompletedTask;
    }

    private async Task RegisterWithRetries(CancellationToken cancellationToken)
    {
        foreach (var delay in RetryDelays)
        {
            if (delay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }

            try
            {
                if (FileTransformationRegistrar.Register(_logger))
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to register the index.html transformation");
                return;
            }
        }

        _logger.LogWarning(
            "File Transformation was not found. The API and the server side filtering work, but the web button is unavailable. Install File Transformation 3.0.0.0 or newer.");
    }
}
