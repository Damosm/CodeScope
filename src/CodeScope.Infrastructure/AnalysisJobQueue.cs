using System.Collections.Concurrent;
using System.Threading.Channels;
using CodeScope.Application;
using CodeScope.Domain;

namespace CodeScope.Infrastructure;

public sealed class AnalysisJobQueue : IAnalysisJobQueue
{
    private readonly Channel<JobState> _channel = Channel.CreateBounded<JobState>(
        new BoundedChannelOptions(20)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

    private readonly ConcurrentDictionary<Guid, JobState> _jobs = new();

    public async ValueTask EnqueueAsync(Guid analysisId, string rootPath, CancellationToken ct)
    {
        var state = new JobState(analysisId, rootPath);
        if (!_jobs.TryAdd(analysisId, state))
            throw new InvalidOperationException("Cette analyse est déjà dans la file.");

        try
        {
            await _channel.Writer.WriteAsync(state, ct);
        }
        catch
        {
            _jobs.TryRemove(analysisId, out _);
            state.Cancellation.Dispose();
            throw;
        }
    }

    public async ValueTask<QueuedAnalysis> DequeueAsync(CancellationToken ct)
    {
        var state = await _channel.Reader.ReadAsync(ct);
        return new QueuedAnalysis(state.Id, state.RootPath, state.Cancellation.Token);
    }

    public AnalysisJobSnapshot? Get(Guid analysisId)
    {
        if (!_jobs.TryGetValue(analysisId, out var state)) return null;
        lock (state.SyncRoot) return state.Snapshot;
    }

    public void MarkRunning(Guid analysisId)
    {
        Update(analysisId, state => state.Snapshot = state.Cancellation.IsCancellationRequested
            ? state.Snapshot with { Status = AnalysisStatus.Cancelled, Stage = "cancelled", Message = "Analyse annulée." }
            : state.Snapshot with { Status = AnalysisStatus.Running, Stage = "starting", Message = "Démarrage de l'analyse." });
    }

    public void Report(Guid analysisId, AnalysisProgress progress)
    {
        Update(analysisId, state =>
        {
            if (state.Snapshot.Status != AnalysisStatus.Running) return;
            state.Snapshot = state.Snapshot with
            {
                Stage = progress.Stage,
                Message = progress.Message,
                ProjectsProcessed = progress.ProjectsProcessed,
                TotalProjects = progress.TotalProjects,
                FilesProcessed = progress.FilesProcessed,
                SymbolsFound = progress.SymbolsFound,
                Warnings = progress.Warnings
            };
        });
    }

    public void MarkFinished(Guid analysisId, AnalysisStatus status, string? error = null)
    {
        var (stage, message) = status switch
        {
            AnalysisStatus.Completed => ("completed", "Analyse terminée."),
            AnalysisStatus.Cancelled => ("cancelled", "Analyse annulée."),
            _ => ("failed", "L'analyse a échoué.")
        };
        Update(analysisId, state => state.Snapshot = state.Snapshot with
        {
            Status = status,
            Stage = stage,
            Message = message,
            Error = error
        });
    }

    public bool TryCancel(Guid analysisId)
    {
        if (!_jobs.TryGetValue(analysisId, out var state)) return false;
        lock (state.SyncRoot)
        {
            if (state.Snapshot.Status is AnalysisStatus.Completed or AnalysisStatus.Failed or AnalysisStatus.Cancelled)
                return false;
            state.Cancellation.Cancel();
            state.Snapshot = state.Snapshot with
            {
                Status = AnalysisStatus.Cancelled,
                Stage = "cancelled",
                Message = "Annulation demandée."
            };
            return true;
        }
    }

    public void Remove(Guid analysisId)
    {
        if (!_jobs.TryRemove(analysisId, out var state)) return;
        state.Cancellation.Cancel();
    }

    private void Update(Guid id, Action<JobState> update)
    {
        if (!_jobs.TryGetValue(id, out var state)) return;
        lock (state.SyncRoot) update(state);
    }

    private sealed class JobState
    {
        public JobState(Guid id, string rootPath)
        {
            Id = id;
            RootPath = rootPath;
            Snapshot = new AnalysisJobSnapshot(
                id,
                AnalysisStatus.Pending,
                "queued",
                "Analyse en attente.",
                0,
                0,
                0,
                0,
                0,
                null);
        }

        public Guid Id { get; }
        public string RootPath { get; }
        public CancellationTokenSource Cancellation { get; } = new();
        public object SyncRoot { get; } = new();
        public AnalysisJobSnapshot Snapshot { get; set; }
    }
}
