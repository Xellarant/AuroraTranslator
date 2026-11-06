using Aurora.Content;

namespace AuroraTranslator.Application;

public sealed record RunParameters(string ContentRoot = "", string DatabasePath = "",
    bool SkipUnusable = false, bool CompareSources = false);
public sealed record SavedPreset(Guid Id, string Name, RunParameters Parameters);
public enum RunKind { Import, Inspection }
public enum RunStatus { Running, Cancelling, Completed, CompletedWithIssues, Failed, Cancelled, Interrupted }
public sealed record DiagnosticRow(string Area, string Kind, string Message,
    string? FilePath = null, string? ElementId = null, int Count = 1);
public sealed record OperationResult(ContentDatabaseMetadata? Metadata, IReadOnlyList<DiagnosticRow> Issues,
    int? FilesChanged = null, int? FilesUnchanged = null, int? ElementsWritten = null,
    bool? IntegrityPassed = null, IReadOnlyList<string>? Log = null)
{
    public bool HasIssues => Issues.Any(i => i.Kind != "Expected");
}
public sealed record RunRecord(Guid Id, RunKind Kind, RunParameters Parameters, DateTimeOffset StartedUtc,
    RunStatus Status = RunStatus.Running, DateTimeOffset? FinishedUtc = null,
    OperationResult? Result = null, string? Error = null);
public sealed record WorkspaceDocument(int Version, RunParameters Selection,
    IReadOnlyList<SavedPreset> Presets, IReadOnlyList<RunRecord> Runs);
