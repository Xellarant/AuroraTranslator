using System.Security.Cryptography;
using System.Text.Json;
using Aurora.Content;
using AuroraTranslator.Application;

internal static class DesktopWorkflowTests
{
    private sealed class Fixture : IDisposable
    {
        private readonly TestWorkspace workspace = TestWorkspace.Create();
        public string Root => Path.Combine(workspace.DirectoryPath, "content");
        public string State => Path.Combine(workspace.DirectoryPath, "settings");
        public string Database => workspace.DatabasePath;
        public Fixture()
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(Path.Combine(Root, "base.xml"), "<elements><element id='ID_DESKTOP' name='Original' type='Class Feature' source='Test'/></elements>");
        }
        public WorkspaceSession Session(IContentOperations? operations = null)
        {
            var session = new WorkspaceSession(new(State), operations ?? new ContentOperations());
            session.Select(new(Root, Database));
            return session;
        }
        public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); workspace.Dispose(); }
    }
    private sealed class ControlledOperations : IContentOperations
    {
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IProgress<ContentImportProgress>? Progress;
        public Action<string>? Diagnostic;
        public bool HonorCancellation = true;
        public RunParameters? Parameters;
        public async Task<OperationResult> ImportAsync(RunParameters p, IProgress<ContentImportProgress> progress,
            Action<string> diagnostic, CancellationToken cancellationToken)
        {
            Parameters = p; Progress = progress; Diagnostic = diagnostic;
            progress.Report(new(ContentImportPhase.Preparing, 1, 3, 0, 0, "first.xml"));
            Entered.SetResult();
            await Release.Task.WaitAsync(HonorCancellation ? cancellationToken : CancellationToken.None);
            return new(null, [], 1, 0, 1);
        }
        public OperationResult Inspect(RunParameters p, CancellationToken cancellationToken) => new(null, [], IntegrityPassed: true);
    }
    private static void Await(Task task) => task.WaitAsync(TimeSpan.FromSeconds(45)).GetAwaiter().GetResult();
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

    public static void SettingsRoundTrip()
    {
        using var f = new Fixture();
        var session = f.Session(new ControlledOperations());
        var parameters = new RunParameters(f.Root, f.Database, true, true);
        session.Select(parameters); session.SavePreset("Campaign"); session.SavePreset("campaign");
        var store = new WorkspaceStore(f.State);
        var loaded = store.Load();
        TestAssert.Equal(parameters, loaded.Selection);
        TestAssert.Equal(1, loaded.Presets.Count);
        var interrupted = new RunRecord(Guid.NewGuid(), RunKind.Import, parameters, DateTimeOffset.UtcNow);
        store.Save(loaded with { Runs = [interrupted] });
        TestAssert.Equal(RunStatus.Interrupted, new WorkspaceStore(f.State).Load().Runs.Single().Status);
        var invalidDocuments = new[]
        {
            loaded with { Selection = parameters with { ContentRoot = null! } },
            loaded with { Runs = [interrupted with { Result = new(null, null!) }] },
            loaded with { Runs = [interrupted with { Result = new(null, [null!]) }] },
            loaded with { Runs = [interrupted with { Result = new(null, [new(null!, "failure", "message")]) }] }
        };
        // Write malformed inputs without invoking computed properties on invalid models.
        var invalidJsonOptions = new JsonSerializerOptions(WorkspaceStore.JsonOptions) { IgnoreReadOnlyProperties = true };
        foreach (string bad in new[] { "broken json", "{\"Version\":99,\"Selection\":{},\"Presets\":[],\"Runs\":[]}" }
            .Concat(invalidDocuments.Select(d => JsonSerializer.Serialize(d, invalidJsonOptions))))
        {
            File.WriteAllText(store.FilePath, bad);
            var broken = new WorkspaceStore(f.State); var fallback = broken.Load();
            Require(broken.Warning != null, "Unreadable settings must be visible.");
            broken.Save(fallback);
            TestAssert.Equal(bad, File.ReadAllText(store.FilePath));
        }
    }

    public static void RunLifetime()
    {
        using var f = new Fixture(); var backend = new ControlledOperations(); var session = f.Session(backend);
        int notifications = 0;
        void Page() => Interlocked.Increment(ref notifications);
        session.Changed += Page;
        Task first = session.StartAsync(RunKind.Import); Await(backend.Entered.Task);
        var selected = session.Document.Selection;
        Require(session.IsBusy && session.Progress?.Completed == 1, "Progress must be available while running.");
        bool refused = false;
        try { session.StartAsync(RunKind.Inspection); } catch (InvalidOperationException) { refused = true; }
        Require(refused, "Overlapping jobs must be refused.");
        // Leaving a page only detaches its observer; it does not cancel the operation.
        session.Changed -= Page;
        session.Select(selected with { DatabasePath = Path.Combine(f.Root, "next.sqlite") });
        backend.Release.SetResult(); Await(first);
        TestAssert.Equal(selected, backend.Parameters);
        TestAssert.Equal(RunStatus.Completed, session.Document.Runs.Single().Status);
        Require(!session.IsBusy && notifications > 0, "A detached page must not own the job.");
        backend.Progress!.Report(new(ContentImportPhase.Writing, 99, 100, 1, 99, "stale.xml"));
        backend.Diagnostic!("stale message");
        Require(session.Progress == null && session.Messages.Length == 0, "Late callbacks must not change a completed run.");
        var reloaded = new WorkspaceStore(f.State).Load();
        TestAssert.Equal(selected, reloaded.Runs.Single().Parameters);
        TestAssert.Equal(session.Document.Selection, reloaded.Selection);
        var exported = JsonSerializer.Deserialize<RunRecord>(WorkspaceStore.Export(reloaded.Runs.Single()), WorkspaceStore.JsonOptions)!;
        TestAssert.Equal(RunStatus.Completed, exported.Status);
        TestAssert.Equal(1, exported.Result!.ElementsWritten);
    }

    public static void Cancellation()
    {
        using var f = new Fixture(); var backend = new ControlledOperations(); var session = f.Session(backend);
        Task task = session.StartAsync(RunKind.Import); Await(backend.Entered.Task);
        session.Cancel(); Await(task);
        TestAssert.Equal(RunStatus.Cancelled, session.Document.Runs.First().Status);
        Require(!session.IsBusy, "Cancellation must release the job slot.");
        var late = new ControlledOperations { HonorCancellation = false };
        session = f.Session(late);
        task = session.StartAsync(RunKind.Import); Await(late.Entered.Task);
        session.Cancel(); late.Release.SetResult(); Await(task);
        TestAssert.Equal(RunStatus.Completed, session.Document.Runs.First().Status);
    }

    public static void RealImportAndInspection()
    {
        using var f = new Fixture(); var session = f.Session();
        Await(session.StartAsync(RunKind.Import));
        TestAssert.Equal(RunStatus.Completed, session.Document.Runs.First().Status);
        string before = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f.Database)));
        session.Select(new(f.Root, f.Database, CompareSources: true));
        Await(session.StartAsync(RunKind.Inspection));
        Require(session.Document.Runs.First().Result?.IntegrityPassed == true, "Prepared fixture must pass integrity inspection.");
        TestAssert.Equal(before, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f.Database))));
        File.WriteAllText(Path.Combine(f.Root, "bad.xml"), "<elements><broken");
        Await(session.StartAsync(RunKind.Import));
        TestAssert.Equal(RunStatus.Failed, session.Document.Runs.First().Status);
        TestAssert.Equal(before, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f.Database))));
        session.Select(new(f.Root, f.Database, SkipUnusable: true));
        Await(session.StartAsync(RunKind.Import));
        TestAssert.Equal(RunStatus.CompletedWithIssues, session.Document.Runs.First().Status);
        Require(session.Document.Runs.First().Result!.Issues.Any(i => i.Kind == "unreadable"), "Skipped files need actionable results.");
        Require(ContentCatalogReader.ReadSummaries(f.Database).Entries.Count == 1, "Best effort preserves the usable record.");
    }

    public static void RealCancellation()
    {
        using var f = new Fixture();
        ContentImport.ImportAsync(f.Root, f.Database).GetAwaiter().GetResult();
        byte[] before = File.ReadAllBytes(f.Database);
        File.WriteAllText(Path.Combine(f.Root, "base.xml"), "<elements><element id='ID_DESKTOP' name='Changed' type='Class Feature' source='Test'/></elements>");
        using var cancellation = new CancellationTokenSource();
        bool cancelled = false;
        try { ContentImport.ImportAsync(f.Root, f.Database, new CancelProgress(cancellation), cancellation.Token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { cancelled = true; }
        Require(cancelled && before.SequenceEqual(File.ReadAllBytes(f.Database)), "Cancellation must preserve the active database.");
    }
    private sealed class CancelProgress(CancellationTokenSource cancellation) : IProgress<ContentImportProgress>
    { public void Report(ContentImportProgress p) { if (p.Phase == ContentImportPhase.Writing) cancellation.Cancel(); } }

    public static void PublicIntegrity()
    {
        using var f = new Fixture();
        Require(!ContentIntegrity.Check(f.Database).Passed && !File.Exists(f.Database), "Inspection must not create a missing database.");
        File.WriteAllText(f.Database, "not a database");
        Require(!ContentIntegrity.Check(f.Database).Passed, "Invalid SQLite must be reported as a failed inspection.");
        TestAssert.Equal("not a database", File.ReadAllText(f.Database));
    }
}
