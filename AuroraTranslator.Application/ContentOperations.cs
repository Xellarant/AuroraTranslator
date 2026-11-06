using Aurora.Content;

namespace AuroraTranslator.Application;

public interface IContentOperations
{
    Task<OperationResult> ImportAsync(RunParameters parameters, IProgress<ContentImportProgress> progress,
        Action<string> diagnostic, CancellationToken cancellationToken);
    OperationResult Inspect(RunParameters parameters, CancellationToken cancellationToken);
}

public sealed class ContentOperations : IContentOperations
{
    public async Task<OperationResult> ImportAsync(RunParameters p, IProgress<ContentImportProgress> progress,
        Action<string> diagnostic, CancellationToken cancellationToken)
    {
        var result = await ContentImport.ImportAsync(p.ContentRoot, p.DatabasePath, progress,
            cancellationToken, diagnostic, skipUnusableContent: p.SkipUnusable);
        // Activation already succeeded. A subsequent query failure must never turn this into
        // a failed/cancelled import or claim the old database is still active.
        var issues = result.Skipped.Select(s => new DiagnosticRow("Import", s.Kind, s.Detail, s.Path)).ToList();
        ContentDatabaseMetadata? metadata = null;
        try
        {
            metadata = ContentDatabaseReader.ReadMetadata(p.DatabasePath);
            AddContentIssues(p.DatabasePath, issues);
        }
        catch (Exception ex) { issues.Add(new("Inspection", "unavailable", "Import completed, but its content summary could not be read: " + ex.Message)); }
        if (result.Diagnostics.Count > 0 && issues.Count == 0)
            issues.AddRange(result.Diagnostics.Select(d => new DiagnosticRow("Import", "diagnostic", d)));
        return new(metadata, issues, result.FilesChanged, result.FilesUnchanged, result.ElementsWritten, Log: result.Diagnostics);
    }

    public OperationResult Inspect(RunParameters p, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var integrity = ContentIntegrity.Check(p.DatabasePath, p.CompareSources ? p.ContentRoot : null);
        var issues = integrity.Failures.Select(f => new DiagnosticRow("Integrity", "failure", f)).ToList();
        ContentDatabaseMetadata? metadata = null;
        try
        {
            metadata = ContentDatabaseReader.ReadMetadata(p.DatabasePath);
            if (metadata?.SchemaVersion == ContentDatabaseReader.CurrentSchemaVersion
                && metadata.DataVersion == ContentDatabaseReader.CurrentDataVersion)
            {
                issues.AddRange(ContentDatabaseReader.ReadSkippedContent(p.DatabasePath)
                    .Select(s => new DiagnosticRow("Import", s.Kind, s.Detail, s.Path)));
                AddContentIssues(p.DatabasePath, issues);
            }
        }
        catch (Exception ex) { issues.Add(new("Inspection", "unavailable", "Content summary could not be read: " + ex.Message)); }
        cancellationToken.ThrowIfCancellationRequested();
        return new(metadata, issues, IntegrityPassed: integrity.Passed);
    }

    private static void AddContentIssues(string database, List<DiagnosticRow> issues)
    {
        var health = ContentDatabaseReader.ReadHealth(database);
        if (health != null)
        {
            if (health.ProjectionIssues > 0)
                issues.Add(new("Content", "projection", "Resolved spell, item or companion rows are missing.", Count: health.ProjectionIssues));
            issues.AddRange(health.Groups.Select(g => new DiagnosticRow("Content", g.Impact.ToString(),
                $"{g.Kind}: {g.Reason} ({g.Status})", g.FilePath, Count: g.Count)));
            issues.AddRange(health.Samples.Select(s => new DiagnosticRow("Content example", s.Impact.ToString(),
                $"{s.Kind}: {s.Reason} — {s.Text}", s.FilePath, s.Key)));
        }
        issues.AddRange(ContentDatabaseReader.ReadLocalCorrections(database)
            .Where(c => c.Status is not "retired" and not "approved-local")
            .Select(c => new DiagnosticRow("Corrections", c.Status, c.ReviewDetails, c.FilePath)));
    }
}
