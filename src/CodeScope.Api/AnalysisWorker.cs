using CodeScope.Application;
using CodeScope.Domain;

namespace CodeScope.Api;

public sealed class AnalysisWorker : BackgroundService
{
    private readonly IAnalysisJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AnalysisWorker> _logger;

    public AnalysisWorker(
        IAnalysisJobQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<AnalysisWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await MarkInterruptedAnalysesAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            QueuedAnalysis job;
            try
            {
                job = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            if (job.CancellationToken.IsCancellationRequested)
            {
                await SetStatusAsync(job.Id, AnalysisStatus.Cancelled, null, CancellationToken.None);
                _queue.MarkFinished(job.Id, AnalysisStatus.Cancelled);
                continue;
            }

            _queue.MarkRunning(job.Id);
            await SetStatusAsync(job.Id, AnalysisStatus.Running, null, stoppingToken);
            _logger.LogInformation("Starting analysis {AnalysisId}.", job.Id);

            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                job.CancellationToken,
                stoppingToken);

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var scanner = scope.ServiceProvider.GetRequiredService<IProjectScanner>();
                var repository = scope.ServiceProvider.GetRequiredService<IAnalysisRepository>();
                var progress = new ImmediateProgress<AnalysisProgress>(value => _queue.Report(job.Id, value));
                var analysis = await scanner.ScanAsync(job.Id, job.RootPath, progress, linkedCancellation.Token);
                await repository.CompleteAsync(analysis, linkedCancellation.Token);
                _queue.MarkFinished(job.Id, AnalysisStatus.Completed);
                _logger.LogInformation("Completed analysis {AnalysisId}.", job.Id);
            }
            catch (OperationCanceledException) when (job.CancellationToken.IsCancellationRequested)
            {
                await SetStatusAsync(job.Id, AnalysisStatus.Cancelled, null, CancellationToken.None);
                _queue.MarkFinished(job.Id, AnalysisStatus.Cancelled);
                _logger.LogInformation("Cancelled analysis {AnalysisId}.", job.Id);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                var error = $"{exception.GetType().Name}: {exception.Message}";
                await SetStatusAsync(job.Id, AnalysisStatus.Failed, error, CancellationToken.None);
                _queue.MarkFinished(job.Id, AnalysisStatus.Failed, error);
                _logger.LogError(exception, "Analysis {AnalysisId} failed.", job.Id);
            }
        }
    }

    private async Task MarkInterruptedAnalysesAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAnalysisRepository>();
        var analyses = await repository.ListAsync(ct);
        foreach (var analysis in analyses.Where(x => x.Status is AnalysisStatus.Pending or AnalysisStatus.Running))
            await repository.UpdateStatusAsync(
                analysis.Id,
                AnalysisStatus.Failed,
                "L'analyse a été interrompue par l'arrêt de l'application.",
                ct);
    }

    private async Task SetStatusAsync(Guid id, AnalysisStatus status, string? error, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAnalysisRepository>();
        await repository.UpdateStatusAsync(id, status, error, ct);
    }

    private sealed class ImmediateProgress<T> : IProgress<T>
    {
        private readonly Action<T> _report;
        public ImmediateProgress(Action<T> report) => _report = report;
        public void Report(T value) => _report(value);
    }
}
