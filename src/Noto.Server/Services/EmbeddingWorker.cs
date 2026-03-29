using Noto.Server.Data;

namespace Noto.Server.Services;

public class EmbeddingWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<EmbeddingWorker> _log;

    public EmbeddingWorker(IServiceProvider services, ILogger<EmbeddingWorker> log)
    {
        _services = services;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for startup
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        _log.LogInformation("EmbeddingWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var embSvc = scope.ServiceProvider.GetRequiredService<EmbeddingService>();

                var provider = await embSvc.GetActiveProvider();
                if (provider == null)
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                    continue;
                }

                var unembedded = await embSvc.GetUnembeddedEntityIds(provider.Id, limit: 20);
                if (unembedded.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                    continue;
                }

                _log.LogInformation("Embedding {Count} entities with {Provider}",
                    unembedded.Count, provider.Name);

                var success = 0;
                foreach (var entityId in unembedded)
                {
                    if (stoppingToken.IsCancellationRequested) break;

                    if (await embSvc.EmbedEntity(entityId, provider))
                        success++;

                    // Small delay to avoid overwhelming the embedding server
                    await Task.Delay(50, stoppingToken);
                }

                var total = await embSvc.GetEmbeddedCount(provider.Id);
                _log.LogInformation("Embedded {Success}/{Count} entities (total: {Total})",
                    success, unembedded.Count, total);

                // Short delay before checking for more
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "EmbeddingWorker error");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }

        _log.LogInformation("EmbeddingWorker stopped");
    }
}
