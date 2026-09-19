#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Aurora.Content;

public enum ContentDatabaseHealthStatus
{
    Healthy,
    Warning,
    Error
}

public enum ContentDatabaseTrustImpact
{
    Blocking,
    ManualReview,
    AutoRecovered,
    Expected
}

public sealed record ContentDatabaseHealthIssueGroup(
    ContentDatabaseTrustImpact Impact,
    string Area,
    string Status,
    string Reason,
    string Kind,
    string FilePath,
    int Count);

public sealed record ContentDatabaseHealthIssueSample(
    ContentDatabaseTrustImpact Impact,
    string Area,
    string Status,
    string Reason,
    string Kind,
    string FilePath,
    string Owner,
    string Key,
    string Text);

public sealed record ContentDatabaseHealthReport(
    int ActionableUnresolvedLinks,
    int ClassifiedUnresolvedLinks,
    int SourceIntegrityIssues,
    int MissingResolvedSpellRows,
    int MissingResolvedItemRows,
    int MissingResolvedCompanionRows,
    IReadOnlyList<ContentDatabaseHealthIssueGroup>? IssueGroups = null,
    IReadOnlyList<ContentDatabaseHealthIssueSample>? IssueSamples = null)
{
    public int ProjectionIssues =>
        MissingResolvedSpellRows + MissingResolvedItemRows + MissingResolvedCompanionRows;

    public int TotalIssues =>
        ActionableUnresolvedLinks + SourceIntegrityIssues + ProjectionIssues;

    public int BlockingIssueCount =>
        ProjectionIssues + Groups.Where(g => g.Impact == ContentDatabaseTrustImpact.Blocking).Sum(g => g.Count);

    public int ManualReviewIssueCount =>
        Groups.Where(g => g.Impact == ContentDatabaseTrustImpact.ManualReview).Sum(g => g.Count);

    public int AutoRecoveredIssueCount =>
        Groups.Where(g => g.Impact == ContentDatabaseTrustImpact.AutoRecovered).Sum(g => g.Count);

    public int ExpectedIssueCount =>
        Groups.Where(g => g.Impact == ContentDatabaseTrustImpact.Expected).Sum(g => g.Count);

    public ContentDatabaseHealthStatus Status =>
        BlockingIssueCount > 0
            ? ContentDatabaseHealthStatus.Error
            : ManualReviewIssueCount > 0
                ? ContentDatabaseHealthStatus.Warning
                : ContentDatabaseHealthStatus.Healthy;

    public IReadOnlyList<ContentDatabaseHealthIssueGroup> Groups => IssueGroups ?? [];

    public IReadOnlyList<ContentDatabaseHealthIssueSample> Samples => IssueSamples ?? [];
}

public sealed record ContentDatabaseMetadata(
    int SchemaVersion,
    int DataVersion,
    string ImporterVersion,
    string BuiltUtc,
    int SourceFileCount,
    int ElementCount,
    string? ContentRootHash);
