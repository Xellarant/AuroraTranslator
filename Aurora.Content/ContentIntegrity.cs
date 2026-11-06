#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using Microsoft.Data.Sqlite;

namespace Aurora.Content;

/// <summary>Read-only structural/prepared-projection checks, separate from content review warnings.</summary>
public sealed record ContentIntegrityReport(IReadOnlyList<string> Failures)
{
    public bool Passed => Failures.Count == 0;
}

public static class ContentIntegrity
{
    /// <summary>
    /// Checks an existing database without creating or repairing it. When supplied, sourceRoot
    /// is compared with captured inputs. Failure to finish inspection is itself a reported failure.
    /// Content review/recovery warnings remain available separately through ContentDatabaseReader.
    /// </summary>
    public static ContentIntegrityReport Check(string databasePath, string? sourceRoot = null)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            return new(["Select an existing database to inspect."]);
        if (!File.Exists(databasePath))
            return new(["Database unavailable: " + databasePath]);
        try { return new(AuroraDataIntegrity.Check(databasePath, sourceRoot).ToArray()); }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException
            or XmlException or ArgumentException or InvalidOperationException)
        {
            return new(["Integrity inspection could not complete: " + ex.Message]);
        }
    }
}
