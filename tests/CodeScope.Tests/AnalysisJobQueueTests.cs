using CodeScope.Domain;
using CodeScope.Infrastructure;

namespace CodeScope.Tests;

public sealed class AnalysisJobQueueTests
{
    [Fact]
    public async Task Queue_tracks_progress_and_cancellation()
    {
        var queue = new AnalysisJobQueue();
        var id = Guid.NewGuid();
        await queue.EnqueueAsync(id, "C:\\source", default);

        var job = await queue.DequeueAsync(default);
        queue.MarkRunning(id);
        queue.Report(id, new("files", "Lecture", 1, 2, 3, 4, 1));

        var running = queue.Get(id);
        Assert.NotNull(running);
        Assert.Equal(AnalysisStatus.Running, running!.Status);
        Assert.Equal(3, running.FilesProcessed);
        Assert.True(queue.TryCancel(id));
        Assert.True(job.CancellationToken.IsCancellationRequested);
        Assert.Equal(AnalysisStatus.Cancelled, queue.Get(id)!.Status);
    }

    [Fact]
    public async Task Removing_cancelled_pending_job_keeps_dequeue_safe()
    {
        var queue = new AnalysisJobQueue();
        var id = Guid.NewGuid();
        await queue.EnqueueAsync(id, "C:\\source", default);
        Assert.True(queue.TryCancel(id));

        queue.Remove(id);
        var job = await queue.DequeueAsync(default);

        Assert.Equal(id, job.Id);
        Assert.True(job.CancellationToken.IsCancellationRequested);
        Assert.Null(queue.Get(id));
    }
}
