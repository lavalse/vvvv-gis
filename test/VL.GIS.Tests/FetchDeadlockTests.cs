using System.Collections.Concurrent;
using VL.GIS.Tiles;

namespace VL.GIS.Tests;

/// <summary>
/// Regression cover for a deadlock that stopped vvvv dead.
///
/// FetchTileBytes used to await GetTileAsync directly on the calling thread and
/// block on the result. vvvv's runtime thread carries a SynchronizationContext, so the
/// continuation was posted back to the thread already sitting in GetResult(), and neither
/// side ever moved again. The runtime stopped evaluating, F5 could not restart it, the log
/// file stayed empty, and closing the window left vvvv.exe running.
///
/// It took an evening to find, largely because calling the node from PowerShell worked
/// perfectly: PowerShell installs no SynchronizationContext, so the bug cannot happen
/// there. Exercising sync-over-async in a host without a context proves nothing about a
/// host that has one, which is exactly what these tests supply.
///
/// No network. The tile source is a fake whose task completes on the thread pool, which is
/// all that is needed to reproduce the hazard.
/// </summary>
public class FetchDeadlockTests
{
    /// <summary>
    /// Runs every posted callback on one dedicated thread, the way a UI or runtime loop
    /// does. If the code under test blocks that thread while waiting for a continuation
    /// posted to it, nothing will ever run again.
    /// </summary>
    sealed class SingleThreadedContext : SynchronizationContext, IDisposable
    {
        readonly BlockingCollection<(SendOrPostCallback callback, object? state)> _queue = new();
        readonly Thread _thread;

        public SingleThreadedContext()
        {
            _thread = new Thread(() =>
            {
                SetSynchronizationContext(this);
                foreach (var (callback, state) in _queue.GetConsumingEnumerable())
                    callback(state);
            })
            { IsBackground = true };
            _thread.Start();
        }

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        /// <summary>Run an action on the context's thread and wait for it, or time out.</summary>
        public bool TryRun(Action action, TimeSpan timeout)
        {
            using var done = new ManualResetEventSlim(false);
            Post(_ => { try { action(); } finally { done.Set(); } }, null);
            return done.Wait(timeout);
        }

        public void Dispose() => _queue.CompleteAdding();
    }

    /// <summary>
    /// Returns bytes from a task that completes on the thread pool, so awaiting it inside a
    /// SynchronizationContext produces a continuation that has to be posted back.
    /// </summary>
    sealed class FakeHttpTileSource : ITileSource
    {
        readonly byte[] _payload;

        public FakeHttpTileSource(byte[] payload) => _payload = payload;

        public string Name => "Fake";
        public int MinZoom => 0;
        public int MaxZoom => 19;
        public string AttributionText => "Fake";
        public string AttributionUrl => "https://example.com";
        public string UrlFor(TileIndex index) => $"https://example.com/{index.Level}/{index.Col}/{index.Row}.png";

        public async Task<byte[]?> GetTileAsync(
            HttpClient httpClient, TileIndex index, CancellationToken token = default)
        {
            // Deliberately no ConfigureAwait(false). That omission is what makes an awaited call
            // hazardous to block on -- with it, the continuation would not need the caller's
            // context and there would be no deadlock to reproduce. It was BruTile that behaved
            // this way; our own HttpTileSource does use ConfigureAwait(false), which makes this
            // fake the *stricter* case rather than an imitation of the real one.
            await Task.Delay(20);
            return _payload;
        }
    }

    static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public void The_harness_reproduces_the_original_deadlock()
    {
        // A test that has never gone red is not a test. This one runs the exact shape
        // FetchTileBytes used to have -- await the library task and block on the result, on
        // a thread that owns a SynchronizationContext -- and asserts that it does NOT
        // finish. If this ever starts completing, the harness has stopped being sensitive
        // and the test below has stopped proving anything.
        var source = new FakeHttpTileSource(new byte[] { 1 });
        var index0 = TileFetchNodes.CreateTileIndex(0, 0, 0);

        using var context = new SingleThreadedContext();

        // xUnit1031 says not to block on a task because it can deadlock. Quite so: causing
        // that deadlock on purpose is the entire point of this test.
#pragma warning disable xUnit1031
        bool finished = context.TryRun(
            () => _ = source.GetTileAsync(new HttpClient(), index0).GetAwaiter().GetResult(),
            TimeSpan.FromSeconds(2));
#pragma warning restore xUnit1031

        Assert.False(finished,
            "The naive sync-over-async pattern completed, so this harness can no longer "
            + "detect the deadlock it exists to guard against.");

        // The blocked worker is a background thread, so it does not hold up the test run.
    }

    [Fact]
    public void FetchTileBytes_returns_under_a_SynchronizationContext()
    {
        // The actual regression. Before the fix this call never came back and the test would
        // fail on the timeout rather than on an assertion.
        var source = new FakeHttpTileSource(new byte[] { 1, 2, 3, 4, 5 });
        var index = TileFetchNodes.CreateTileIndex(3637, 1612, 12);

        byte[]? result = null;

        using var context = new SingleThreadedContext();
        bool finished = context.TryRun(
            () => result = TileFetchNodes.FetchTileBytes(source, index), Patience);

        Assert.True(finished,
            "FetchTileBytes did not return within 10s under a SynchronizationContext. "
            + "That is the sync-over-async deadlock: the awaited continuation is posted back "
            + "to the thread that is blocked waiting for it.");
        Assert.NotNull(result);
        Assert.Equal(5, result!.Length);
    }

    [Fact]
    public void FetchTileToFile_returns_under_a_SynchronizationContext()
    {
        // Shares FetchTileBytes' implementation, so it shared the deadlock.
        var source = new FakeHttpTileSource(new byte[] { 9, 9, 9 });
        var index = TileFetchNodes.CreateTileIndex(1, 2, 3);
        var directory = Path.Combine(Path.GetTempPath(), "VL.GIS.Tests", Guid.NewGuid().ToString("N"));

        string? path = null;

        try
        {
            using var context = new SingleThreadedContext();
            bool finished = context.TryRun(
                () => path = TileFetchNodes.FetchTileToFile(source, index, directory), Patience);

            Assert.True(finished, "FetchTileToFile deadlocked under a SynchronizationContext.");
            Assert.NotNull(path);
            Assert.True(File.Exists(path));
            Assert.Equal(3, new FileInfo(path!).Length);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FetchTileBytes_returns_null_rather_than_throwing_when_the_source_fails()
    {
        // The node swallows failures and returns null. Worth pinning, because it means a
        // patch showing nothing cannot be distinguished from a patch that is not running --
        // which is a large part of why the deadlock took so long to find.
        var index = TileFetchNodes.CreateTileIndex(0, 0, 0);

        byte[]? result = TileFetchNodes.FetchTileBytes(new ThrowingTileSource(), index);

        Assert.Null(result);
    }

    sealed class ThrowingTileSource : ITileSource
    {
        public string Name => "Throwing";
        public int MinZoom => 0;
        public int MaxZoom => 19;
        public string AttributionText => "";
        public string AttributionUrl => "";
        public string UrlFor(TileIndex index) => "https://example.invalid/";

        public Task<byte[]?> GetTileAsync(
            HttpClient httpClient, TileIndex index, CancellationToken token = default)
            => throw new HttpRequestException("simulated failure");
    }

    [Fact]
    public void FetchTileAsync_delivers_its_bytes_under_a_SynchronizationContext()
    {
        // The async node was never at risk, but it is the one the help patch should use, so
        // it deserves the same scrutiny.
        var source = new FakeHttpTileSource(new byte[] { 7, 7 });
        var index = TileFetchNodes.CreateTileIndex(3637, 1612, 12);

        byte[]? received = null;
        using var delivered = new ManualResetEventSlim(false);

        using var context = new SingleThreadedContext();
        context.TryRun(() =>
        {
            TileFetchNodes.FetchTileAsync(source, index).Subscribe(bytes =>
            {
                received = bytes;
                delivered.Set();
            });
        }, Patience);

        Assert.True(delivered.Wait(Patience), "FetchTileAsync never emitted.");
        Assert.NotNull(received);
        Assert.Equal(2, received!.Length);
    }
}
