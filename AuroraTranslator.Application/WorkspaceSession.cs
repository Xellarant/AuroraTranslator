using Aurora.Content;

namespace AuroraTranslator.Application;

/// <summary>Owns operation lifetime independently of any page. Snapshots are immutable.</summary>
public sealed class WorkspaceSession
{
    private readonly object gate = new();
    private readonly WorkspaceStore store;
    private readonly IContentOperations operations;
    private WorkspaceDocument document;
    private CancellationTokenSource? cancellation;
    private Guid? activeId;
    private ContentImportProgress? progress;
    private readonly Queue<string> messages = new();
    public event Action? Changed;

    public WorkspaceSession(WorkspaceStore store, IContentOperations operations)
    { this.store = store; this.operations = operations; document = store.Load(); }
    public WorkspaceDocument Document { get { lock (gate) return document; } }
    public string? SettingsWarning { get { lock (gate) return store.Warning; } }
    public bool IsBusy { get { lock (gate) return activeId != null; } }
    public RunRecord? ActiveRun { get { lock (gate) return document.Runs.FirstOrDefault(r => r.Id == activeId); } }
    public ContentImportProgress? Progress { get { lock (gate) return progress; } }
    public string[] Messages { get { lock (gate) return messages.ToArray(); } }

    public void Select(RunParameters selection)
    { lock (gate) { document = document with { Selection = selection }; Save(); } Notify(); }
    public void SavePreset(string name)
    {
        name = name.Trim();
        if (name.Length is 0 or > 100) throw new ArgumentException("Enter a preset name of 1–100 characters.");
        lock (gate)
        {
            var old = document.Presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            var saved = new SavedPreset(old?.Id ?? Guid.NewGuid(), name, document.Selection);
            document = document with { Presets = document.Presets.Where(p => p.Id != saved.Id).Append(saved).ToArray() };
            Save();
        }
        Notify();
    }

    public Task StartAsync(RunKind kind)
    {
        RunRecord run;
        CancellationToken token;
        lock (gate)
        {
            if (activeId != null) throw new InvalidOperationException("Wait for the current run to finish.");
            var p = document.Selection;
            if (string.IsNullOrWhiteSpace(p.DatabasePath)) throw new ArgumentException("Choose a database path.");
            p = p with { DatabasePath = Path.GetFullPath(p.DatabasePath.Trim()), ContentRoot = p.ContentRoot.Trim() };
            if (kind == RunKind.Import || p.CompareSources)
            {
                if (!Directory.Exists(p.ContentRoot)) throw new ArgumentException("Choose an existing content folder.");
                p = p with { ContentRoot = Path.GetFullPath(p.ContentRoot) };
            }
            if (kind == RunKind.Inspection && !File.Exists(p.DatabasePath)) throw new ArgumentException("Choose an existing database to inspect.");
            if (Directory.Exists(p.DatabasePath) || !new[] { ".sqlite", ".db", ".sqlite3" }.Contains(Path.GetExtension(p.DatabasePath), StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException("Choose a database file ending in .sqlite, .sqlite3 or .db.");
            run = new(Guid.NewGuid(), kind, p, DateTimeOffset.UtcNow);
            cancellation = new(); token = cancellation.Token; activeId = run.Id;
            progress = null; messages.Clear();
            document = document with { Selection = p, Runs = new[] { run }.Concat(document.Runs).Take(30).ToArray() };
            Save();
        }
        Notify();
        return Task.Run(async () =>
        {
            RunRecord finished;
            try
            {
                token.ThrowIfCancellationRequested();
                var result = kind == RunKind.Import
                    ? await operations.ImportAsync(run.Parameters, new InlineProgress(p => Update(run.Id, p)),
                        text => Message(run.Id, text), token).ConfigureAwait(false)
                    : operations.Inspect(run.Parameters, token);
                // Once the backend reports success, a late cancel cannot relabel activation.
                finished = run with { Result = result, Status = result.HasIssues ? RunStatus.CompletedWithIssues : RunStatus.Completed };
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            { finished = run with { Status = RunStatus.Cancelled, Error = "Cancelled before completion." }; }
            catch (Exception ex)
            { finished = run with { Status = RunStatus.Failed, Error = ex.Message }; }
            lock (gate)
            {
                document = document with { Runs = document.Runs.Select(r => r.Id == run.Id ? finished with { FinishedUtc = DateTimeOffset.UtcNow } : r).ToArray() };
                activeId = null; progress = null; cancellation!.Dispose(); cancellation = null; Save();
            }
            Notify();
        });
    }

    public void Cancel()
    {
        lock (gate)
        {
            if (activeId == null) return;
            document = document with { Runs = document.Runs.Select(r => r.Id == activeId ? r with { Status = RunStatus.Cancelling } : r).ToArray() };
            cancellation!.Cancel();
        }
        Notify();
    }
    private void Update(Guid id, ContentImportProgress value)
    { lock (gate) { if (activeId != id) return; progress = value; } Notify(); }
    private void Message(Guid id, string value)
    { lock (gate) { if (activeId != id) return; messages.Enqueue(value); while (messages.Count > 200) messages.Dequeue(); } Notify(); }
    private void Save() => store.Save(document);
    private void Notify() => Changed?.Invoke();
    private sealed class InlineProgress(Action<ContentImportProgress> report) : IProgress<ContentImportProgress>
    { public void Report(ContentImportProgress value) => report(value); }
}
