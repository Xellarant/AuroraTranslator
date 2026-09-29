#nullable enable
using System.Collections.Generic;

namespace Aurora.Content;

/// <summary>Imported supplier classification, not a user's source availability preference.</summary>
public sealed record ContentCatalogSource(string RelativePath, string? PackageKey, string? PackageName, string? PackageKind);

public sealed record ContentCatalogSpell(int Level, string? School, bool IsRitual, bool IsConcentration);

/// <summary>One effective identity. SummaryText is untruncated imported text, not display HTML.</summary>
public sealed record ContentCatalogSummary(
    string AuroraId, string Name, string Type, string? SourceBook, bool CompendiumDisplay,
    string SummaryText, ContentCatalogSource? Supplier, ContentCatalogSpell? Spell,
    string? ResolutionKind, int SupersededDeclarationCount);

public sealed record ContentCatalogSnapshot(ContentDatabaseMetadata Metadata, IReadOnlyList<ContentCatalogSummary> Entries);

public sealed record ContentCatalogAlias(string SavedId, string TargetId, string Origin, string? Note);

public enum ContentCatalogLinkStatus { Resolved, MissingTarget, TypeMismatch }

/// <summary>
/// An explicit XML reference, not a claim of character eligibility. RuleXml retains grant conditions.
/// Target describes the effective definition even on a type mismatch; links are not traversed recursively.
/// </summary>
public sealed record ContentCatalogLink(
    string Kind, string RequestedId, string? ExpectedType, string? RuleXml,
    ContentCatalogLinkStatus Status, ContentCatalogSummary? Target, ContentCatalogAlias? Alias);

public sealed record ContentCatalogAppend(ContentCatalogSource Source, int Ordinal, string Xml);

/// <summary>
/// One database snapshot's detail. XML is imported Aurora markup, not sanitized HTML.
/// EffectiveXml is null for records without a prepared XML definition (for example external JSON imports).
/// Suppliers credits surviving base declarations; AppliedAppends credits materialized extensions separately.
/// </summary>
public sealed record ContentCatalogDetail(
    ContentDatabaseMetadata Metadata, string RequestedId, ContentCatalogAlias? Alias,
    ContentCatalogSummary Summary, string DescriptionText, string? DescriptionXml, string? EffectiveXml,
    IReadOnlyList<ContentCatalogSource> Suppliers, IReadOnlyList<ContentCatalogAppend> AppliedAppends,
    IReadOnlyList<ContentCatalogLink> Links);
