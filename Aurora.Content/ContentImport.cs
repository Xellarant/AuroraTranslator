#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Aurora.Content.Preparation;

namespace Aurora.Content;

public enum ContentImportPhase
{
    /// <summary>Reading, hashing and staging content files; evaluating local corrections.</summary>
    Preparing,
    /// <summary>Parsing staged XML into the import catalog.</summary>
    Reading,
    /// <summary>Comparing file hashes with the database to find changed files.</summary>
    Comparing,
    /// <summary>Writing elements from changed files.</summary>
    Writing,
    /// <summary>Re-resolving precedence and rebuilding derived catalogs.</summary>
    Resolving,
    /// <summary>Validating the candidate database, mirroring corrections and activating it.</summary>
    Activating,
    Complete
}

/// <summary>
/// Import progress. <see cref="Completed"/>/<see cref="Total"/> count files in the Preparing,
/// Reading and Comparing phases and elements in the Writing phase; they are zero otherwise.
/// </summary>
public sealed record ContentImportProgress(
    ContentImportPhase Phase,
    int Completed,
    int Total,
    int FilesChanged,
    int ElementsWritten,
    string? CurrentFile);

/// <summary>
/// Something the import left out, kept so the user can put it right. <see cref="Kind"/> is
/// "unreadable" (the entire file could not be interpreted), "definition-conflict" (all suppliers
/// of an ambiguous ID were excluded, leaving unrelated definitions available), or "append"
/// (one operation was dropped; the rest of the file was imported). Correction failures refuse
/// activation instead of producing skip records. Older databases may contain other skip kinds.
/// </summary>
public sealed record ContentImportSkip(
    string Path,
    string RelativePath,
    string Kind,
    string Detail,
    string? RelatedPath);

public sealed record ContentImportResult(
    int ElementsWritten,
    int FilesChanged,
    int FilesUnchanged,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<ContentImportSkip> Skipped);

/// <summary>Builds or refreshes a content database in-process.</summary>
public static class ContentImport
{
    /// <summary>
    /// Imports one content root through the correction-aware preparation workflow into
    /// <paramref name="databasePath"/>, the same path as the Translator's sqlite-import command.
    /// The database is replaced only after the candidate validates; failures and cancellation leave
    /// the existing database untouched. A first import excludes conflicting IDs and reports them
    /// as unavailable; later new conflicts refuse activation. Throws on failure.
    /// </summary>
    /// <param name="skipUnusableContent">
    /// Leaves a file the import cannot use out of this import, listed in
    /// <see cref="ContentImportResult.Skipped"/> and in the database, instead of refusing the whole
    /// import over it. The files themselves are never touched, so the next import reads them again.
    /// Off by default. This option does not permit invalid corrections or new conflicts during
    /// refresh; first-import identity quarantine is reported independently of this option.
    /// </param>
    public static async Task<ContentImportResult> ImportAsync(
        string contentRoot,
        string databasePath,
        IProgress<ContentImportProgress>? progress = null,
        CancellationToken cancellationToken = default,
        Action<string>? onDiagnostic = null,
        string? srdMonstersJsonPath = null,
        bool skipUnusableContent = false)
    {
        if (!Directory.Exists(contentRoot))
            throw new DirectoryNotFoundException($"Aurora path was not found: {contentRoot}");

        var reporter = progress == null ? null : new ImportProgressReporter(progress);
        var diagnostics = new List<string>();
        var skipped = new List<ContentImportSkip>();
        AuroraSqliteImporter.ImportSummary summary = default;
        await LocalCorrectionSync.ImportAsync(new[] { contentRoot }, databasePath,
            (roots, candidate, cancellation) =>
            {
                var catalog = AuroraCatalogBuilder.BuildAuroraImportCatalog(roots[0], reporter, cancellation);
                summary = AuroraSqliteImporter.ImportFinalized(catalog, null, candidate, srdMonstersJsonPath, reporter, cancellation);
                return Task.FromResult(new CorrectionImportResult(true));
            },
            cancellationToken, reporter,
            diagnostic => { diagnostics.Add(diagnostic); onDiagnostic?.Invoke(diagnostic); },
            skipUnusableContent, skipped.AddRange);
        reporter?.Report(ContentImportPhase.Complete, 0, 0);
        return new(summary.ElementsWritten, summary.FilesChanged, summary.FilesUnchanged, diagnostics, skipped);
    }
}

/// <summary>Throttles progress to phase changes, final counts and at most one update per 100 ms.</summary>
internal sealed class ImportProgressReporter(IProgress<ContentImportProgress> target)
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private ContentImportPhase? _lastPhase;
    private long _lastReportMs = -1;

    public int FilesChanged { get; set; }
    public int ElementsWritten { get; set; }

    public void Report(ContentImportPhase phase, int completed, int total, string? currentFile = null)
    {
        long now = _clock.ElapsedMilliseconds;
        if (phase == _lastPhase && completed < total && now - _lastReportMs < 100) return;
        _lastPhase = phase;
        _lastReportMs = now;
        target.Report(new(phase, completed, total, FilesChanged, ElementsWritten, currentFile));
    }
}
