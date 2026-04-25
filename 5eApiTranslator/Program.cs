using AuroraTranslator.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;

namespace AuroraTranslator
{
    class Program
    {
        static string projectRootPath = ResolveProjectRootPath();
        static string defaultAuroraPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "5e Character Builder",
            "custom");
        static string defaultSqlitePath = Path.Combine(
            projectRootPath,
            "Data",
            "aurora-character-loading.sqlite");
        static string sqliteSchemaPath = Path.Combine(
            projectRootPath,
            "Data",
            "sqlite-character-loading.sql");
        static string defaultDiagnosticsBaselinePath = Path.Combine(
            projectRootPath,
            "Data",
            "diagnostics-regression-baseline.json");
        static string defaultFirstPartyBaselineSqlitePath = Path.Combine(
            projectRootPath,
            "Data",
            "aurora-first-party-regression.sqlite");

        static async Task Main(string[] args)
        {
            try
            {
                await RunAsync(args);
            }
            catch (Exception ex)
            {
                WriteError(ex, args);
                Environment.ExitCode = 1;
            }
        }

        static string defaultSrdMonstersPath = Path.Combine(
            projectRootPath,
            "Data",
            "5e-SRD-Monsters.json");

        static string defaultXellarantXmlPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "5e Character Builder",
            "custom",
            "supplements",
            "the-book-of-xellarant",
            "creatures.xml");

        private static async Task RunAsync(string[] args)
        {
            if (args.Length > 0
                && string.Equals(args[0], "sqlite-import", StringComparison.OrdinalIgnoreCase))
            {
                string auroraPath = args.Length > 1 ? args[1] : defaultAuroraPath;
                string sqlitePath = args.Length > 2 ? args[2] : defaultSqlitePath;

                ImportAuroraToSqlite(auroraPath, sqlitePath);
                return;
            }

            if (args.Length > 0
                && string.Equals(args[0], "srd-creatures", StringComparison.OrdinalIgnoreCase))
            {
                string jsonPath   = args.Length > 1 ? args[1] : defaultSrdMonstersPath;
                string sqlitePath = args.Length > 2 ? args[2] : defaultSqlitePath;

                AuroraSqliteImporter.ImportSrdCreatures(jsonPath, sqlitePath);
                return;
            }

            if (args.Length > 0
                && string.Equals(args[0], "generate-xellarant-xml", StringComparison.OrdinalIgnoreCase))
            {
                string jsonPath   = args.Length > 1 ? args[1] : defaultSrdMonstersPath;
                string sqlitePath = args.Length > 2 ? args[2] : defaultSqlitePath;
                string outputPath = args.Length > 3 ? args[3] : defaultXellarantXmlPath;

                XellarantXmlGenerator.Generate(jsonPath, sqlitePath, outputPath);
                return;
            }

            if (args.Length > 0
                && string.Equals(args[0], "eval-expression", StringComparison.OrdinalIgnoreCase))
            {
                string expressionText = args.Length > 1 ? args[1] : null;
                string contextJsonPath = args.Length > 2 ? args[2] : null;

                EvaluateExpression(expressionText, contextJsonPath);
                return;
            }

            if (args.Length > 0
                && string.Equals(args[0], "packages", StringComparison.OrdinalIgnoreCase))
            {
                string sqlitePath = args.Length > 1 ? args[1] : defaultSqlitePath;
                ListContentPackages(sqlitePath);
                return;
            }

            if (args.Length > 0
                && string.Equals(args[0], "set-package-enabled", StringComparison.OrdinalIgnoreCase))
            {
                string packageKey = args.Length > 1 ? args[1] : null;
                string enabledText = args.Length > 2 ? args[2] : null;
                string sqlitePath = args.Length > 3 ? args[3] : defaultSqlitePath;

                SetPackageEnabled(packageKey, enabledText, sqlitePath);
                return;
            }

            if (args.Length > 0
                && string.Equals(args[0], "set-package-rank", StringComparison.OrdinalIgnoreCase))
            {
                string packageKey = args.Length > 1 ? args[1] : null;
                string rankText = args.Length > 2 ? args[2] : null;
                string sqlitePath = args.Length > 3 ? args[3] : defaultSqlitePath;

                SetPackageRank(packageKey, rankText, sqlitePath);
                return;
            }

            if (args.Length > 0
                && string.Equals(args[0], "refresh-package-resolution", StringComparison.OrdinalIgnoreCase))
            {
                string sqlitePath = args.Length > 1 ? args[1] : defaultSqlitePath;
                RefreshPackageResolution(sqlitePath);
                return;
            }

            if (args.Length > 0
                && string.Equals(args[0], "refresh-package-admin-views", StringComparison.OrdinalIgnoreCase))
            {
                string sqlitePath = args.Length > 1 ? args[1] : defaultSqlitePath;
                RefreshPackageAdministrationViews(sqlitePath);
                return;
            }

            if (args.Length > 0
                && string.Equals(args[0], "check-package-refresh-parity", StringComparison.OrdinalIgnoreCase))
            {
                string packageKey = args.Length > 1 ? args[1] : null;
                string settingKind = args.Length > 2 ? args[2] : null;
                string settingValue = args.Length > 3 ? args[3] : null;
                string sqlitePath = args.Length > 4 ? args[4] : defaultSqlitePath;

                CheckPackageRefreshParity(packageKey, settingKind, settingValue, sqlitePath);
                return;
            }

            if (args.Length > 0
                && string.Equals(args[0], "summarize-unresolved-links", StringComparison.OrdinalIgnoreCase))
            {
                string sqlitePath = args.Length > 1 ? args[1] : defaultSqlitePath;
                string topCountText = args.Length > 2 ? args[2] : null;

                SummarizeUnresolvedLinks(sqlitePath, topCountText);
                return;
            }

            if (args.Length > 0
                && string.Equals(args[0], "summarize-source-integrity", StringComparison.OrdinalIgnoreCase))
            {
                string sqlitePath = args.Length > 1 ? args[1] : defaultSqlitePath;
                string topCountText = args.Length > 2 ? args[2] : null;

                SummarizeSourceIntegrity(sqlitePath, topCountText);
                return;
            }

            if (args.Length > 0
                && string.Equals(args[0], "capture-diagnostics-baseline", StringComparison.OrdinalIgnoreCase))
            {
                string sqlitePath = args.Length > 1 ? args[1] : defaultSqlitePath;
                string baselinePath = args.Length > 2 ? args[2] : defaultDiagnosticsBaselinePath;

                CaptureDiagnosticsBaseline(sqlitePath, baselinePath);
                return;
            }

            if (args.Length > 0
                && string.Equals(args[0], "check-diagnostics-regression", StringComparison.OrdinalIgnoreCase))
            {
                string sqlitePath = args.Length > 1 ? args[1] : defaultSqlitePath;
                string baselinePath = args.Length > 2 ? args[2] : defaultDiagnosticsBaselinePath;

                CheckDiagnosticsRegression(sqlitePath, baselinePath);
                return;
            }

            if (args.Length > 0
                && string.Equals(args[0], "capture-first-party-diagnostics-baseline", StringComparison.OrdinalIgnoreCase))
            {
                string auroraRootPath = args.Length > 1 ? args[1] : defaultAuroraPath;
                string sqlitePath = args.Length > 2 ? args[2] : defaultFirstPartyBaselineSqlitePath;
                string baselinePath = args.Length > 3 ? args[3] : defaultDiagnosticsBaselinePath;

                CaptureFirstPartyDiagnosticsBaseline(auroraRootPath, sqlitePath, baselinePath);
                return;
            }

            Console.WriteLine("Commands:");
            Console.WriteLine("  sqlite-import [auroraPath] [sqlitePath]              Import Aurora XML into the SQLite database.");
            Console.WriteLine("  srd-creatures [jsonPath] [sqlitePath]                Import SRD monsters and link to Aurora companions.");
            Console.WriteLine("  generate-xellarant-xml [jsonPath] [sqlitePath] [out] Generate The Book of Xellarant creatures XML.");
            Console.WriteLine("  eval-expression [expressionText] [contextJson]       Parse and evaluate an Aurora expression.");
            Console.WriteLine("  packages [sqlitePath]                                List content packages and current precedence settings.");
            Console.WriteLine("  set-package-enabled [key] [true|false] [sqlitePath] Enable or disable a content package.");
            Console.WriteLine("  set-package-rank [key] [rank] [sqlitePath]           Set a content package precedence rank.");
            Console.WriteLine("  refresh-package-resolution [sqlitePath]              Recompute precedence-driven link resolution.");
            Console.WriteLine("  refresh-package-admin-views [sqlitePath]             Recreate precedence/debug admin views.");
            Console.WriteLine("  check-package-refresh-parity [key] [rank|enabled] [value] [sqlitePath]");
            Console.WriteLine("                                                      Compare scoped package refresh against a full rebuild.");
            Console.WriteLine("  summarize-unresolved-links [sqlitePath] [topCount]  Show the biggest unresolved link patterns by kind.");
            Console.WriteLine("  summarize-source-integrity [sqlitePath] [topCount]  Show likely upstream/source data integrity issues.");
            Console.WriteLine("  capture-diagnostics-baseline [sqlitePath] [baselinePath]");
            Console.WriteLine("                                                      Save unresolved/source-integrity counts as a regression baseline.");
            Console.WriteLine("  check-diagnostics-regression [sqlitePath] [baselinePath]");
            Console.WriteLine("                                                      Compare current diagnostics against a saved baseline.");
            Console.WriteLine("  capture-first-party-diagnostics-baseline [auroraRoot] [sqlitePath] [baselinePath]");
            Console.WriteLine("                                                      Rebuild a canonical baseline from only core + supplements.");
            Console.WriteLine($"Default Aurora path:  {defaultAuroraPath}");
            Console.WriteLine($"Default SQLite path:  {defaultSqlitePath}");
            Console.WriteLine($"Default baseline:     {defaultDiagnosticsBaselinePath}");
            Console.WriteLine($"Default 1P SQLite:    {defaultFirstPartyBaselineSqlitePath}");
            Console.WriteLine($"Default SRD JSON:     {defaultSrdMonstersPath}");
            Console.WriteLine($"Default XML output:   {defaultXellarantXmlPath}");
        }

        private static void WriteError(Exception exception, string[] args)
        {
            Console.Error.WriteLine("The operation failed.");
            Console.Error.WriteLine(exception.Message);

            for (Exception inner = exception.InnerException; inner != null; inner = inner.InnerException)
            {
                Console.Error.WriteLine(inner.Message);
            }

            if (args.Length > 0
                && string.Equals(args[0], "sqlite-import", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("Usage: sqlite-import [auroraPath] [sqlitePath]");
                Console.Error.WriteLine($"Default Aurora path: {defaultAuroraPath}");
                Console.Error.WriteLine($"Default SQLite path: {defaultSqlitePath}");
            }
            else if (args.Length > 0
                && string.Equals(args[0], "srd-creatures", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("Usage: srd-creatures [jsonPath] [sqlitePath]");
                Console.Error.WriteLine($"Default JSON path:   {defaultSrdMonstersPath}");
                Console.Error.WriteLine($"Default SQLite path: {defaultSqlitePath}");
            }
            else if (args.Length > 0
                && string.Equals(args[0], "generate-xellarant-xml", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("Usage: generate-xellarant-xml [jsonPath] [sqlitePath] [outputPath]");
                Console.Error.WriteLine($"Default JSON path:   {defaultSrdMonstersPath}");
                Console.Error.WriteLine($"Default SQLite path: {defaultSqlitePath}");
                Console.Error.WriteLine($"Default output:      {defaultXellarantXmlPath}");
            }
            else if (args.Length > 0
                && string.Equals(args[0], "eval-expression", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("Usage: eval-expression [expressionText] [contextJsonPath]");
                Console.Error.WriteLine("Context JSON shape:");
                Console.Error.WriteLine(@"  { ""tokens"": [], ""numericValues"": {}, ""scalarValues"": {}, ""macroValues"": {} }");
            }
            else if (args.Length > 0
                && string.Equals(args[0], "packages", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("Usage: packages [sqlitePath]");
                Console.Error.WriteLine($"Default SQLite path: {defaultSqlitePath}");
            }
            else if (args.Length > 0
                && string.Equals(args[0], "set-package-enabled", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("Usage: set-package-enabled [packageKey] [true|false] [sqlitePath]");
                Console.Error.WriteLine($"Default SQLite path: {defaultSqlitePath}");
            }
            else if (args.Length > 0
                && string.Equals(args[0], "set-package-rank", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("Usage: set-package-rank [packageKey] [rank] [sqlitePath]");
                Console.Error.WriteLine($"Default SQLite path: {defaultSqlitePath}");
            }
            else if (args.Length > 0
                && string.Equals(args[0], "refresh-package-resolution", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("Usage: refresh-package-resolution [sqlitePath]");
                Console.Error.WriteLine($"Default SQLite path: {defaultSqlitePath}");
            }
            else if (args.Length > 0
                && string.Equals(args[0], "refresh-package-admin-views", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("Usage: refresh-package-admin-views [sqlitePath]");
                Console.Error.WriteLine($"Default SQLite path: {defaultSqlitePath}");
            }
            else if (args.Length > 0
                && string.Equals(args[0], "check-package-refresh-parity", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("Usage: check-package-refresh-parity [packageKey] [rank|enabled] [value] [sqlitePath]");
                Console.Error.WriteLine($"Default SQLite path: {defaultSqlitePath}");
            }
            else if (args.Length > 0
                && string.Equals(args[0], "summarize-unresolved-links", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("Usage: summarize-unresolved-links [sqlitePath] [topCount]");
                Console.Error.WriteLine($"Default SQLite path: {defaultSqlitePath}");
            }
            else if (args.Length > 0
                && string.Equals(args[0], "summarize-source-integrity", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("Usage: summarize-source-integrity [sqlitePath] [topCount]");
                Console.Error.WriteLine($"Default SQLite path: {defaultSqlitePath}");
            }
            else if (args.Length > 0
                && string.Equals(args[0], "capture-diagnostics-baseline", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("Usage: capture-diagnostics-baseline [sqlitePath] [baselinePath]");
                Console.Error.WriteLine($"Default SQLite path:   {defaultSqlitePath}");
                Console.Error.WriteLine($"Default baseline path: {defaultDiagnosticsBaselinePath}");
            }
            else if (args.Length > 0
                && string.Equals(args[0], "check-diagnostics-regression", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("Usage: check-diagnostics-regression [sqlitePath] [baselinePath]");
                Console.Error.WriteLine($"Default SQLite path:   {defaultSqlitePath}");
                Console.Error.WriteLine($"Default baseline path: {defaultDiagnosticsBaselinePath}");
            }
            else if (args.Length > 0
                && string.Equals(args[0], "capture-first-party-diagnostics-baseline", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("Usage: capture-first-party-diagnostics-baseline [auroraRoot] [sqlitePath] [baselinePath]");
                Console.Error.WriteLine($"Default Aurora path:   {defaultAuroraPath}");
                Console.Error.WriteLine($"Default SQLite path:   {defaultFirstPartyBaselineSqlitePath}");
                Console.Error.WriteLine($"Default baseline path: {defaultDiagnosticsBaselinePath}");
            }
        }

        private static string ResolveProjectRootPath()
        {
            string[] startingPaths = new[]
            {
                Directory.GetCurrentDirectory(),
                AppContext.BaseDirectory
            };

            foreach (string startingPath in startingPaths.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                DirectoryInfo directory = new DirectoryInfo(startingPath);

                while (directory != null)
                {
                    if (File.Exists(Path.Combine(directory.FullName, "AuroraTranslator.csproj")))
                    {
                        return directory.FullName;
                    }

                    directory = directory.Parent;
                }
            }

            throw new DirectoryNotFoundException("Could not locate the project root containing AuroraTranslator.csproj.");
        }

        private static void ImportAuroraToSqlite(string auroraPath, string sqlitePath)
        {
            if (!Directory.Exists(auroraPath))
            {
                throw new DirectoryNotFoundException($"Aurora path was not found: {auroraPath}");
            }

            if (!File.Exists(sqliteSchemaPath))
            {
                throw new FileNotFoundException("The SQLite schema file was not found.", sqliteSchemaPath);
            }

            AuroraImportCatalog catalog = BuildAuroraImportCatalog(auroraPath);
            string srdPath = File.Exists(defaultSrdMonstersPath) ? defaultSrdMonstersPath : null;
            AuroraSqliteImporter.Import(catalog, sqliteSchemaPath, sqlitePath, srdPath);

            Console.WriteLine($"Imported {catalog.Elements.Count} Aurora elements and {catalog.Spells.Count} Aurora spells into {sqlitePath}.");
        }

        private static void EvaluateExpression(string expressionText, string contextJsonPath)
        {
            if (string.IsNullOrWhiteSpace(expressionText))
                throw new ArgumentException("Expression text is required.", nameof(expressionText));

            if (!string.IsNullOrWhiteSpace(contextJsonPath) && !File.Exists(contextJsonPath))
                throw new FileNotFoundException("The expression context JSON file was not found.", contextJsonPath);

            AuroraExpressionParseResult parseResult = AuroraExpressionEngine.Parse(expressionText);
            AuroraExpressionEvaluationContext context = string.IsNullOrWhiteSpace(contextJsonPath)
                ? new AuroraExpressionEvaluationContext()
                : AuroraExpressionEvaluationContext.Load(contextJsonPath);

            bool evaluationResult = AuroraExpressionEngine.Evaluate(parseResult.RootNode, context);

            Console.WriteLine($"Expression: {expressionText}");
            Console.WriteLine($"Parse status: {parseResult.Status}");

            if (!string.IsNullOrWhiteSpace(parseResult.ErrorText))
                Console.WriteLine($"Parse error: {parseResult.ErrorText}");

            Console.WriteLine($"Root node: {DescribeExpressionNode(parseResult.RootNode)}");
            Console.WriteLine($"Evaluation result: {evaluationResult}");

            if (!string.IsNullOrWhiteSpace(contextJsonPath))
                Console.WriteLine($"Context JSON: {contextJsonPath}");
        }

        private static void ListContentPackages(string sqlitePath)
        {
            var packages = AuroraSqliteImporter.ListContentPackages(sqlitePath, sqliteSchemaPath);

            if (packages.Count == 0)
            {
                Console.WriteLine("No content packages were found in the SQLite database.");
                return;
            }

            Console.WriteLine($"Content packages in {sqlitePath}:");
            foreach (var package in packages)
            {
                Console.WriteLine(
                    $"- {package.PackageKey} | {package.PackageName} | {package.PackageKind} | " +
                    $"rank={package.PrecedenceRank} | enabled={(package.IsEnabled ? "yes" : "no")} | " +
                    $"files={package.FileCount} | winners={package.WinningElementCount} | duplicates={package.DuplicateElementCount}");
            }
        }

        private static void SetPackageEnabled(string packageKey, string enabledText, string sqlitePath)
        {
            if (string.IsNullOrWhiteSpace(packageKey))
                throw new ArgumentException("Package key is required.", nameof(packageKey));
            if (!TryParseBoolean(enabledText, out bool isEnabled))
                throw new ArgumentException($"Could not parse package enabled value '{enabledText}'. Use true/false, yes/no, on/off, or 1/0.");

            AuroraSqliteImporter.UpdateContentPackageSettings(sqlitePath, packageKey, isEnabled: isEnabled, schemaPath: sqliteSchemaPath);
            Console.WriteLine($"Updated package '{packageKey}' enabled state to {(isEnabled ? "enabled" : "disabled")}.");
        }

        private static void SetPackageRank(string packageKey, string rankText, string sqlitePath)
        {
            if (string.IsNullOrWhiteSpace(packageKey))
                throw new ArgumentException("Package key is required.", nameof(packageKey));
            if (!int.TryParse(rankText, out int rank))
                throw new ArgumentException($"Could not parse package rank '{rankText}'.");

            AuroraSqliteImporter.UpdateContentPackageSettings(sqlitePath, packageKey, precedenceRank: rank, schemaPath: sqliteSchemaPath);
            Console.WriteLine($"Updated package '{packageKey}' precedence rank to {rank}.");
        }

        private static void RefreshPackageResolution(string sqlitePath)
        {
            AuroraSqliteImporter.RefreshPackageResolution(sqlitePath, sqliteSchemaPath);
            Console.WriteLine($"Refreshed precedence-driven link resolution in {sqlitePath}.");
        }

        private static void RefreshPackageAdministrationViews(string sqlitePath)
        {
            AuroraSqliteImporter.RefreshPackageAdministrationViews(sqlitePath);
            Console.WriteLine($"Refreshed precedence/debug admin views in {sqlitePath}.");
        }

        private static void CheckPackageRefreshParity(
            string packageKey,
            string settingKind,
            string settingValue,
            string sqlitePath)
        {
            if (string.IsNullOrWhiteSpace(packageKey))
                throw new ArgumentException("Package key is required.", nameof(packageKey));
            if (string.IsNullOrWhiteSpace(settingKind))
                throw new ArgumentException("Setting kind is required. Use 'rank' or 'enabled'.", nameof(settingKind));

            int? precedenceRank = null;
            bool? isEnabled = null;

            switch (settingKind.Trim().ToLowerInvariant())
            {
                case "rank":
                    if (!int.TryParse(settingValue, out int rank))
                        throw new ArgumentException($"Could not parse package rank '{settingValue}'.");
                    precedenceRank = rank;
                    break;

                case "enabled":
                    if (!TryParseBoolean(settingValue, out bool enabled))
                        throw new ArgumentException($"Could not parse package enabled value '{settingValue}'. Use true/false, yes/no, on/off, or 1/0.");
                    isEnabled = enabled;
                    break;

                default:
                    throw new ArgumentException($"Unsupported setting kind '{settingKind}'. Use 'rank' or 'enabled'.");
            }

            var parity = AuroraSqliteImporter.ValidatePackageRefreshParity(
                sqlitePath,
                packageKey,
                precedenceRank: precedenceRank,
                isEnabled: isEnabled);

            Console.WriteLine($"Package refresh parity for '{packageKey}' in {sqlitePath}: {(parity.IsMatch ? "MATCH" : "MISMATCH")}");
            foreach (var table in parity.TableResults)
            {
                Console.WriteLine(
                    $"- {table.TableName} | scoped={table.ScopedRowCount} | full={table.FullRowCount} | " +
                    $"scoped-only={table.ScopedOnlyRowCount} | full-only={table.FullOnlyRowCount} | " +
                    $"status={(table.IsMatch ? "ok" : "diff")}");
            }

            if (!parity.IsMatch)
            {
                throw new InvalidOperationException(
                    $"Scoped package refresh diverged from a full rebuild for package '{packageKey}'.");
            }
        }

        private static void SummarizeUnresolvedLinks(string sqlitePath, string topCountText)
        {
            int topCount = 10;
            if (!string.IsNullOrWhiteSpace(topCountText) && !int.TryParse(topCountText, out topCount))
                throw new ArgumentException($"Could not parse top count '{topCountText}'.");
            if (topCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(topCountText), "Top count must be greater than zero.");

            var report = AuroraSqliteImporter.GetUnresolvedLinkDiagnostics(sqlitePath, topCount);

            Console.WriteLine($"Unresolved link diagnostics for {sqlitePath}:");
            Console.WriteLine($"Total unresolved rows: {report.TotalUnresolvedCount}");
            Console.WriteLine($"Actionable unresolved rows: {report.ActionableUnresolvedCount}");

            if (report.DeferredSummaries.Count > 0)
            {
                Console.WriteLine("Deferred/non-actionable rows:");
                foreach (var deferred in report.DeferredSummaries)
                {
                    string reasonSuffix = string.IsNullOrWhiteSpace(deferred.DiagnosticReason)
                        ? string.Empty
                        : $" ({deferred.DiagnosticReason})";
                    Console.WriteLine($"  - {deferred.LinkKind}: {deferred.Count} [{deferred.DiagnosticStatus}{reasonSuffix}]");
                }
            }

            if (report.KindSummaries.Count == 0)
            {
                Console.WriteLine("No actionable unresolved links were found.");
                return;
            }

            foreach (var kindSummary in report.KindSummaries)
            {
                Console.WriteLine();
                Console.WriteLine($"[{kindSummary.LinkKind}] total={kindSummary.TotalCount}");

                foreach (var pattern in kindSummary.Patterns)
                {
                    Console.WriteLine($"  - {pattern.DisplayKey} | count={pattern.Count}");

                    if (!string.IsNullOrWhiteSpace(pattern.DisplayText)
                        && !string.Equals(pattern.DisplayText, pattern.DisplayKey, StringComparison.Ordinal))
                    {
                        Console.WriteLine($"    text: {pattern.DisplayText}");
                    }

                    if (pattern.SampleOwners.Count > 0)
                        Console.WriteLine($"    owners: {string.Join("; ", pattern.SampleOwners)}");
                }
            }
        }

        private static void SummarizeSourceIntegrity(string sqlitePath, string topCountText)
        {
            int topCount = 10;
            if (!string.IsNullOrWhiteSpace(topCountText) && !int.TryParse(topCountText, out topCount))
                throw new ArgumentException($"Could not parse top count '{topCountText}'.");
            if (topCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(topCountText), "Top count must be greater than zero.");

            var report = AuroraSqliteImporter.GetSourceIntegrityDiagnostics(sqlitePath, topCount);

            Console.WriteLine($"Source integrity diagnostics for {sqlitePath}:");
            Console.WriteLine($"Total issues: {report.TotalIssueCount}");

            if (report.KindSummaries.Count == 0)
            {
                Console.WriteLine("No source integrity issues were found.");
                return;
            }

            foreach (var kindSummary in report.KindSummaries)
            {
                Console.WriteLine();
                Console.WriteLine($"[{kindSummary.IssueKind}] total={kindSummary.TotalCount}");

                foreach (var pattern in kindSummary.Patterns)
                {
                    Console.WriteLine($"  - {pattern.DisplayKey} | count={pattern.Count}");

                    if (!string.IsNullOrWhiteSpace(pattern.DisplayText)
                        && !string.Equals(pattern.DisplayText, pattern.DisplayKey, StringComparison.Ordinal))
                    {
                        Console.WriteLine($"    text: {pattern.DisplayText}");
                    }

                    if (pattern.SampleRows.Count > 0)
                        Console.WriteLine($"    samples: {string.Join("; ", pattern.SampleRows)}");
                }
            }
        }

        private static void CaptureDiagnosticsBaseline(string sqlitePath, string baselinePath)
        {
            var baseline = BuildDiagnosticsRegressionBaseline(sqlitePath, "Current SQLite content set");
            string baselineDirectory = Path.GetDirectoryName(baselinePath);
            if (!string.IsNullOrWhiteSpace(baselineDirectory))
                Directory.CreateDirectory(baselineDirectory);

            string json = JsonSerializer.Serialize(
                baseline,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

            File.WriteAllText(baselinePath, json);
            Console.WriteLine($"Captured diagnostics regression baseline to {baselinePath}.");
            Console.WriteLine($"  actionable unresolved: {baseline.ActionableUnresolvedCount}");
            Console.WriteLine($"  total source integrity issues: {baseline.TotalSourceIntegrityCount}");
        }

        private static void CaptureFirstPartyDiagnosticsBaseline(string auroraRootPath, string sqlitePath, string baselinePath)
        {
            if (!Directory.Exists(auroraRootPath))
                throw new DirectoryNotFoundException($"Aurora root path was not found: {auroraRootPath}");

            string[] requiredDirectories = { "core", "supplements" };
            foreach (string directoryName in requiredDirectories)
            {
                string sourceDirectory = Path.Combine(auroraRootPath, directoryName);
                if (!Directory.Exists(sourceDirectory))
                    throw new DirectoryNotFoundException($"Required first-party directory was not found: {sourceDirectory}");
            }

            string stagingRoot = Path.Combine(Path.GetTempPath(), "AuroraTranslatorFirstParty", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingRoot);

            try
            {
                foreach (string directoryName in requiredDirectories)
                {
                    CopyDirectory(
                        Path.Combine(auroraRootPath, directoryName),
                        Path.Combine(stagingRoot, directoryName));

                    string indexPath = Path.Combine(auroraRootPath, $"{directoryName}.index");
                    if (File.Exists(indexPath))
                    {
                        File.Copy(indexPath, Path.Combine(stagingRoot, $"{directoryName}.index"), overwrite: true);
                    }
                }

                string sqliteDirectory = Path.GetDirectoryName(sqlitePath);
                if (!string.IsNullOrWhiteSpace(sqliteDirectory))
                    Directory.CreateDirectory(sqliteDirectory);

                if (File.Exists(sqlitePath))
                    File.Delete(sqlitePath);

                ImportAuroraToSqlite(stagingRoot, sqlitePath);
                AuroraSqliteImporter.RefreshPackageResolution(sqlitePath, sqliteSchemaPath);

                var baseline = BuildDiagnosticsRegressionBaseline(
                    sqlitePath,
                    "Wizards first-party core + supplements");

                string baselineDirectory = Path.GetDirectoryName(baselinePath);
                if (!string.IsNullOrWhiteSpace(baselineDirectory))
                    Directory.CreateDirectory(baselineDirectory);

                string json = JsonSerializer.Serialize(
                    baseline,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                File.WriteAllText(baselinePath, json);

                Console.WriteLine($"Captured first-party diagnostics regression baseline to {baselinePath}.");
                Console.WriteLine($"SQLite source: {sqlitePath}");
                Console.WriteLine($"  actionable unresolved: {baseline.ActionableUnresolvedCount}");
                Console.WriteLine($"  total source integrity issues: {baseline.TotalSourceIntegrityCount}");
            }
            finally
            {
                try
                {
                    if (Directory.Exists(stagingRoot))
                        Directory.Delete(stagingRoot, recursive: true);
                }
                catch
                {
                    // Best-effort temp cleanup only.
                }
            }
        }

        private static void CheckDiagnosticsRegression(string sqlitePath, string baselinePath)
        {
            if (!File.Exists(baselinePath))
                throw new FileNotFoundException($"Diagnostics baseline not found: {baselinePath}");

            DiagnosticsRegressionBaseline baseline = JsonSerializer.Deserialize<DiagnosticsRegressionBaseline>(
                File.ReadAllText(baselinePath))
                ?? throw new InvalidDataException($"Could not deserialize diagnostics baseline: {baselinePath}");

            DiagnosticsRegressionBaseline current = BuildDiagnosticsRegressionBaseline(
                sqlitePath,
                baseline.CorpusLabel ?? "Current SQLite content set");
            List<string> failures = CompareDiagnosticsRegressionBaseline(baseline, current);

            Console.WriteLine($"Diagnostics regression check for {sqlitePath}:");
            Console.WriteLine($"Baseline: {baselinePath}");

            if (failures.Count == 0)
            {
                Console.WriteLine("PASS");
                Console.WriteLine($"  actionable unresolved: {current.ActionableUnresolvedCount}");
                Console.WriteLine($"  total source integrity issues: {current.TotalSourceIntegrityCount}");
                return;
            }

            Console.WriteLine("FAIL");
            foreach (string failure in failures)
                Console.WriteLine($"  - {failure}");

            Environment.ExitCode = 1;
        }

        private static DiagnosticsRegressionBaseline BuildDiagnosticsRegressionBaseline(string sqlitePath, string corpusLabel)
        {
            var unresolved = AuroraSqliteImporter.GetUnresolvedLinkDiagnostics(sqlitePath, topPatternsPerKind: 1, sampleOwnersPerPattern: 1);
            var sourceIntegrity = AuroraSqliteImporter.GetSourceIntegrityDiagnostics(sqlitePath, topPatternsPerKind: 1, sampleRowsPerPattern: 1);

            var deferredCounts = unresolved.DeferredSummaries
                .Select(summary => new DiagnosticsRegressionCount(
                    $"{summary.LinkKind}|{summary.DiagnosticStatus}|{summary.DiagnosticReason}",
                    summary.Count))
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .ToArray();

            var unresolvedKindCounts = unresolved.KindSummaries
                .Select(summary => new DiagnosticsRegressionCount(summary.LinkKind, summary.TotalCount))
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .ToArray();

            var sourceIntegrityKindCounts = sourceIntegrity.KindSummaries
                .Select(summary => new DiagnosticsRegressionCount(summary.IssueKind, summary.TotalCount))
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .ToArray();

            return new DiagnosticsRegressionBaseline(
                CapturedAtUtc: DateTime.UtcNow,
                CorpusLabel: corpusLabel,
                TotalUnresolvedCount: unresolved.TotalUnresolvedCount,
                ActionableUnresolvedCount: unresolved.ActionableUnresolvedCount,
                DeferredCounts: deferredCounts,
                UnresolvedKindCounts: unresolvedKindCounts,
                TotalSourceIntegrityCount: sourceIntegrity.TotalIssueCount,
                SourceIntegrityKindCounts: sourceIntegrityKindCounts);
        }

        private static List<string> CompareDiagnosticsRegressionBaseline(
            DiagnosticsRegressionBaseline expected,
            DiagnosticsRegressionBaseline actual)
        {
            var failures = new List<string>();

            if (expected.TotalUnresolvedCount != actual.TotalUnresolvedCount)
                failures.Add($"Total unresolved count changed: expected {expected.TotalUnresolvedCount}, got {actual.TotalUnresolvedCount}.");

            if (expected.ActionableUnresolvedCount != actual.ActionableUnresolvedCount)
                failures.Add($"Actionable unresolved count changed: expected {expected.ActionableUnresolvedCount}, got {actual.ActionableUnresolvedCount}.");

            if (expected.TotalSourceIntegrityCount != actual.TotalSourceIntegrityCount)
                failures.Add($"Total source integrity count changed: expected {expected.TotalSourceIntegrityCount}, got {actual.TotalSourceIntegrityCount}.");

            CompareCountSet("Deferred unresolved bucket", expected.DeferredCounts, actual.DeferredCounts, failures);
            CompareCountSet("Unresolved kind", expected.UnresolvedKindCounts, actual.UnresolvedKindCounts, failures);
            CompareCountSet("Source integrity kind", expected.SourceIntegrityKindCounts, actual.SourceIntegrityKindCounts, failures);

            return failures;
        }

        private static void CompareCountSet(
            string label,
            IReadOnlyList<DiagnosticsRegressionCount> expected,
            IReadOnlyList<DiagnosticsRegressionCount> actual,
            List<string> failures)
        {
            var expectedMap = expected.ToDictionary(x => x.Key, x => x.Count, StringComparer.Ordinal);
            var actualMap = actual.ToDictionary(x => x.Key, x => x.Count, StringComparer.Ordinal);

            foreach (string key in expectedMap.Keys.Union(actualMap.Keys, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
            {
                bool hasExpected = expectedMap.TryGetValue(key, out long expectedCount);
                bool hasActual = actualMap.TryGetValue(key, out long actualCount);

                if (!hasExpected)
                {
                    failures.Add($"{label} added: {key} => {actualCount}.");
                    continue;
                }

                if (!hasActual)
                {
                    failures.Add($"{label} removed: {key} was {expectedCount}.");
                    continue;
                }

                if (expectedCount != actualCount)
                    failures.Add($"{label} changed: {key} expected {expectedCount}, got {actualCount}.");
            }
        }

        private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);

            foreach (string filePath in Directory.GetFiles(sourceDirectory))
            {
                string destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(filePath));
                File.Copy(filePath, destinationPath, overwrite: true);
            }

            foreach (string childDirectory in Directory.GetDirectories(sourceDirectory))
            {
                string destinationChild = Path.Combine(destinationDirectory, Path.GetFileName(childDirectory));
                CopyDirectory(childDirectory, destinationChild);
            }
        }

        private static bool TryParseBoolean(string value, out bool result)
        {
            switch (value?.Trim().ToLowerInvariant())
            {
                case "1":
                case "true":
                case "yes":
                case "on":
                case "enabled":
                    result = true;
                    return true;
                case "0":
                case "false":
                case "no":
                case "off":
                case "disabled":
                    result = false;
                    return true;
                default:
                    result = false;
                    return false;
            }
        }

        private sealed record DiagnosticsRegressionCount(string Key, long Count);

        private sealed record DiagnosticsRegressionBaseline(
            DateTime CapturedAtUtc,
            string CorpusLabel,
            long TotalUnresolvedCount,
            long ActionableUnresolvedCount,
            IReadOnlyList<DiagnosticsRegressionCount> DeferredCounts,
            IReadOnlyList<DiagnosticsRegressionCount> UnresolvedKindCounts,
            int TotalSourceIntegrityCount,
            IReadOnlyList<DiagnosticsRegressionCount> SourceIntegrityKindCounts);

        private static string DescribeExpressionNode(AuroraExpressionNode node)
        {
            if (node == null)
                return "(none)";

            if (string.Equals(node.Kind, "value", StringComparison.OrdinalIgnoreCase))
                return $"{node.Kind}:{node.ValueType}:{node.ValueText}";

            return $"{node.Kind} ({node.Children.Count} child node(s))";
        }

        private static AuroraImportCatalog BuildAuroraImportCatalog(string auroraPath)
        {
            string[] files = Directory
                .GetFiles(auroraPath, "*.xml", SearchOption.AllDirectories)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            AuroraImportCatalog catalog = new();

            foreach (string file in files)
            {
                string relativePath = Path.GetRelativePath(auroraPath, file);
                XDocument xml = XDocument.Load(file);
                var info = xml.Root?.Element("info");

                catalog.Files.Add(new AuroraFileInfo
                {
                    RelativePath = relativePath,
                    FullPath = file,
                    Name = info?.Element("name")?.Value ?? Path.GetFileNameWithoutExtension(file),
                    Description = info?.Element("description")?.Value,
                    Author = new Author
                    {
                        name = info?.Element("author")?.Value,
                        url = info?.Element("author")?.Attribute("url")?.Value
                    },
                    FileVersion = new FileVersion
                    {
                        versionString = info?.Element("update")?.Attribute("version")?.Value,
                        fileName = info?.Element("update")?.Element("file")?.Attribute("name")?.Value,
                        fileUrl = info?.Element("update")?.Element("file")?.Attribute("url")?.Value
                    }
                });

                foreach (var element in xml.Root?.Elements("element") ?? Enumerable.Empty<XElement>())
                {
                    string name = element.Attribute("name")?.Value;
                    string source = element.Attribute("source")?.Value;
                    string id = element.Attribute("id")?.Value;
                    string type = element.Attribute("type")?.Value;

                    if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(id))
                        continue;

                    if (string.Equals(type, "spell", StringComparison.OrdinalIgnoreCase))
                    {
                        AuroraSpell spell = FillAuroraSpell(element, name, source, id);
                        spell.source_file_path = relativePath;
                        catalog.Spells.Add(spell);
                    }
                    else
                    {
                        AuroraElement auroraElement = FillAuroraElement(element, name, source, id, type);
                        auroraElement.source_file_path = relativePath;
                        catalog.Elements.Add(auroraElement);
                    }
                }
            }

            return catalog;
        }

        private static AuroraSpell FillAuroraSpell(XElement spellElement, string name, string source, string id)
        {
            var spell = new AuroraSpell();

            spell.name = name;
            spell.source = source;
            spell.aurora_id = id;
            spell.index = BuildSlug(spell.name);

            foreach (var childElement in spellElement.Elements())
            {
                // fill compendium_display
                if (childElement.Name == "compendium")
                {
                    spell.compendium_display = Convert.ToBoolean(childElement.Attribute("display")?.Value ?? "true");
                }

                // fill supports (for now just going into classes)
                if (childElement.Name == "supports")
                {
                    spell.classes = new();

                    AuroraTextCollection supports = ParseAuroraTextCollection(childElement.Value);

                    foreach (var support in supports ?? Enumerable.Empty<string>())
                        spell.classes.Add(new BaseApiClass { name = support, index = support.ToLower().Replace(" ", "-") });
                }

                // fill descriptions
                if (childElement.Name == "description")
                {
                    spell.descriptionRawXml = childElement.ToString(SaveOptions.DisableFormatting);
                    spell.desc = new();
                    if (childElement.Value.Contains("At Higher Levels."))
                    {
                        spell.higher_level = new();

                        spell.desc.Add(childElement.Value.Substring(0, childElement.Value.IndexOf("At Higher Levels.") - 1));
                        spell.higher_level.Add(childElement.Value.Substring(childElement.Value.IndexOf("At Higher Levels.")));
                    }
                    else
                    {
                        spell.desc.Add(childElement.Value);
                    }
                }

                // fill setters
                if (childElement.Name == "setters")
                {
                    spell.setters = new();
                    FillSetters(spell.setters, childElement);
                }
            }

            if (spell.setters != null)
            {
                spell.url = spell.url ?? spell.setters.sourceUrl;

                if (spell.setters.level != 0)
                {
                    spell.level = spell.setters.level;
                }

                if (!string.IsNullOrWhiteSpace(spell.setters.school))
                {
                    spell.school = new BaseApiClass { index = spell.setters.school.ToLower() };
                }

                spell.casting_time = spell.setters.time;
                spell.duration = spell.setters.duration;
                spell.range = spell.setters.range;

                if (spell.components == null)
                    spell.components = new();

                if (spell.setters.hasVerbalComponent)
                {
                    spell.components.Add("V");
                }
                if (spell.setters.hasSomaticComponent)
                {
                    spell.components.Add("S");
                }
                if (spell.setters.hasMaterialComponent)
                {
                    spell.components.Add("M");
                }

                spell.material = spell.setters.materialComponent;
                spell.concentration = spell.setters.isConcentration;
                spell.ritual = spell.setters.isRitual;
            }

            string spellDescription = string.Join(" ", spell.desc ?? new List<string>()).ToLowerInvariant();

            if (spellDescription.Contains("melee spell attack"))
            {
                spell.attack_type = "melee";
            }
            else if (spellDescription.Contains("ranged spell attack"))
            {
                spell.attack_type = "ranged";
            }

            return spell;
        }

        private static AuroraElement FillAuroraElement(XElement element, string name, string source, string id, string type = null)
        {
            var auroraElement = new AuroraElement();

            auroraElement.name = name;
            auroraElement.type = type ?? "auroraElement";
            auroraElement.source = source;
            auroraElement.id = id;
            auroraElement.index = BuildSlug(auroraElement.name);

            foreach (var childElement in element.Elements())
            {
                string childName = childElement.Name.LocalName;
                bool handled = false;

                // fill compendium_display
                if (childName == "compendium")
                {
                    auroraElement.compendium.display = Convert.ToBoolean(childElement.Attribute("display")?.Value ?? "true");
                    handled = true;
                }

                // fill supports (for now just going into classes)
                if (childName == "supports")
                {
                    auroraElement.supports = ParseAuroraTextCollection(childElement.Value);
                    handled = true;
                }

                // Fill requirements...
                // TODO: figure out what to do with requirements (how to store/retrieve?)
                if (childName == "requirements")
                {
                    auroraElement.requirements = ParseAuroraTextCollection(childElement.Value);
                    handled = true;
                }

                if (childName == "prerequisites")
                {
                    auroraElement.prerequisites = ParsePrerequisitesCollection(childElement);
                    handled = true;
                }

                if (childName == "prerequisite")
                {
                    auroraElement.prerequisite = childElement.Value;
                    handled = true;
                }

                // fill descriptions
                if (childName == "description")
                {
                    auroraElement.description = childElement.Value;
                    auroraElement.descriptionRawXml = childElement.ToString(SaveOptions.DisableFormatting);

                    //if (childElement.Value.Contains("At Higher Levels."))
                    //{
                    //    auroraElement.higher_level = new();

                    //    auroraElement.desc.Add(childElement.Value.Substring(0, childElement.Value.IndexOf("At Higher Levels.") - 1));
                    //    auroraElement.higher_level.Add(childElement.Value.Substring(childElement.Value.IndexOf("At Higher Levels.")));
                    //}
                    //else
                    //{
                    //    auroraElement.desc.Add(childElement.Value);
                    //}
                    handled = true;
                }

                if (childName == "extract")
                {
                    auroraElement.extract = new AuroraExtract
                    {
                        description = childElement.Element("description")?.Value,
                        items = ParseAuroraItemEntries(childElement)
                    };
                    handled = true;
                }

                if (childName == "sheet")
                {
                    auroraElement.sheet = new();
                    auroraElement.sheet.rawXml = childElement.ToString(SaveOptions.DisableFormatting);

                    if (childElement.Attribute("display") != null)
                    {
                        auroraElement.sheet.display = Convert.ToBoolean(childElement.Attribute("display")?.Value);
                    }
                    auroraElement.sheet.alt = childElement.Attribute("alt")?.Value;
                    auroraElement.sheet.action = childElement.Attribute("action")?.Value;
                    auroraElement.sheet.usage = childElement.Attribute("usage")?.Value;

                    if (childElement.Elements("description")?.Any() == true)
                    {
                        auroraElement.sheet.description = new();
                    }

                    foreach (var desc in childElement.Elements("description"))
                    {
                        auroraElement.sheet.description.Add(
                            new Description
                            {
                                level = desc.Attribute("level")?.Value != null ?
                                    Convert.ToInt32(desc.Attribute("level")?.Value)
                                    : null,
                                text = desc.Value,
                                rawXml = desc.ToString(SaveOptions.DisableFormatting)
                            });
                    }

                    handled = true;
                }

                // fill setters
                if (childName == "setters" || childName == "setter")
                {
                    auroraElement.setters ??= new();
                    FillSetters(auroraElement.setters, childElement);
                    handled = true;
                }

                if (childName == "spellcasting")
                {
                    // used if this element is a spellcasting class or archetype.

                    auroraElement.spellcasting = new();
                    auroraElement.spellcasting.name = childElement.Attribute("name")?.Value;
                    auroraElement.spellcasting.ability = childElement.Attribute("ability")?.Value;
                    auroraElement.spellcasting.prepare = ParseNullableBoolean(childElement.Attribute("prepare")?.Value);
                    auroraElement.spellcasting.allowReplace = ParseNullableBoolean(childElement.Attribute("allowReplace")?.Value);
                    
                    if (childElement.Element("list") != null)
                    {
                        auroraElement.spellcasting.list = ParseAuroraTextCollection(childElement.Element("list")?.Value);
                    }
                    
                    auroraElement.spellcasting.extend = ParseNullableBoolean(childElement.Attribute("extend")?.Value) ?? false;

                    if (childElement.Element("extend") != null)
                    {
                        auroraElement.spellcasting.extendList = ParseAuroraTextCollection(childElement.Element("extend")?.Value);
                    }

                    handled = true;
                }

                if (childName == "multiclass")
                {
                    // used for class-type elements.
                    // used to describe what's required to multiclass from or into this class.

                    auroraElement.multiclass = new();
                    auroraElement.multiclass.id = childElement.Attribute("id")?.Value;
                    auroraElement.multiclass.prerequisite = childElement.Element("prerequisite")?.Value;

                    if (childElement.Element("requirements") != null)
                    {
                        auroraElement.multiclass.requirements = ParseAuroraTextCollection(childElement.Element("requirements")?.Value);
                    }

                    XElement mcSetters = childElement.Element("setters");
                    if (mcSetters != null)
                    {
                        auroraElement.multiclass.setters = new();
                        FillSetters(auroraElement.multiclass.setters, mcSetters);
                    }

                    XElement mcRules = childElement.Element("rules");
                    if (mcRules != null)
                    {
                        auroraElement.multiclass.rules = FillRules(mcRules);
                    }

                    handled = true;
                }

                if (childName == "rules")
                {
                    auroraElement.rules = FillRules(childElement);
                    handled = true;
                }

                if (childName == "grant")
                {
                    auroraElement.rules ??= new Rules { grants = new(), selects = new(), stats = new() };
                    auroraElement.rules.grants.Add(ParseGrant(childElement));
                    handled = true;
                }

                if (childName == "select")
                {
                    auroraElement.rules ??= new Rules { grants = new(), selects = new(), stats = new() };
                    auroraElement.rules.selects.Add(ParseSelect(childElement));
                    handled = true;
                }

                if (childName == "stat")
                {
                    auroraElement.rules ??= new Rules { grants = new(), selects = new(), stats = new() };
                    auroraElement.rules.stats.Add(ParseStat(childElement));
                    handled = true;
                }

                if (!handled)
                {
                    auroraElement.additionalBlocks ??= new();
                    auroraElement.additionalBlocks.Add(ParseAuroraBlockEntry(childElement));
                }
            }

            return auroraElement;
        }

        private static Rules FillRules(XElement parentElement)
        {
            var rules = new Rules
            {
                grants = new(),
                selects = new(),
                stats = new()
            };

            foreach (var grant in parentElement.Elements("grant"))
            {
                rules.grants.Add(ParseGrant(grant));
            }

            foreach (var select in parentElement.Elements("select"))
            {
                rules.selects.Add(ParseSelect(select));
            }

            foreach (var stat in parentElement.Elements("stat"))
            {
                rules.stats.Add(ParseStat(stat));
            }

            return rules;
        }

        private static Grant ParseGrant(XElement grant)
        {
            return new Grant
            {
                type = grant.Attribute("type")?.Value,
                id = grant.Attribute("id")?.Value,
                name = grant.Attribute("name")?.Value,
                level = grant.Attribute("level")?.Value != null ?
                        Convert.ToInt32(grant.Attribute("level")?.Value) :
                        null,
                spellcasting = grant.Attribute("spellcasting")?.Value,
                prepared = grant.Attribute("prepared")?.Value is { } p ? p == "true" : null,
                requirements = ParseAuroraTextCollection(grant.Attribute("requirements")?.Value),
                rawXml = grant.ToString(SaveOptions.DisableFormatting)
            };
        }

        private static Select ParseSelect(XElement select)
        {
            return new Select
            {
                type = select.Attribute("type")?.Value,
                name = select.Attribute("name")?.Value,
                supports = ParseAuroraTextCollection(select.Attribute("supports")?.Value),
                level = select.Attribute("level")?.Value != null ?
                    Convert.ToInt32(select.Attribute("level")?.Value) :
                    null,
                requirements = ParseAuroraTextCollection(select.Attribute("requirements")?.Value),
                number = select.Attribute("number")?.Value != null ?
                    Convert.ToInt32(select.Attribute("number")?.Value) :
                    1,
                defaultChoice = select.Attribute("default")?.Value,
                optional = ParseNullableBoolean(select.Attribute("optional")?.Value) ?? false,
                spellcasting = select.Attribute("spellcasting")?.Value,
                items = ParseAuroraItemEntries(select),
                rawXml = select.ToString(SaveOptions.DisableFormatting)
            };
        }

        private static Stat ParseStat(XElement stat)
        {
            return new Stat
            {
                name = stat.Attribute("name")?.Value,
                value = stat.Attribute("value")?.Value,
                bonus = stat.Attribute("bonus")?.Value,
                equipped = ParseAuroraTextCollection(stat.Attribute("equipped")?.Value),
                level = stat.Attribute("level")?.Value != null ?
                    Convert.ToInt32(stat.Attribute("level")?.Value) :
                    null,
                requirements = ParseAuroraTextCollection(stat.Attribute("requirements")?.Value),
                inline = ParseNullableBoolean(stat.Attribute("inline")?.Value) ?? false,
                alt = stat.Attribute("alt")?.Value,
                rawXml = stat.ToString(SaveOptions.DisableFormatting)
            };
        }

        private static List<AuroraItemEntry> ParseAuroraItemEntries(XElement parentElement)
        {
            var items = new List<AuroraItemEntry>();

            foreach (var itemElement in parentElement.Elements("item"))
            {
                var item = new AuroraItemEntry
                {
                    value = itemElement.Value?.Trim()
                };

                foreach (var attribute in itemElement.Attributes())
                {
                    item.attributes[attribute.Name.LocalName] = attribute.Value;
                }

                items.Add(item);
            }

            return items;
        }

        private static AuroraTextCollection ParsePrerequisitesCollection(XElement prerequisitesElement)
        {
            if (prerequisitesElement == null)
                return null;

            var nestedPrerequisites = prerequisitesElement.Elements("prerequisite")
                .Select(x => x.Value?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            if (nestedPrerequisites.Any())
            {
                var collection = new AuroraTextCollection
                {
                    raw = string.Join(", ", nestedPrerequisites)
                };
                collection.AddRange(nestedPrerequisites);
                return collection;
            }

            return ParseAuroraTextCollection(prerequisitesElement.Value);
        }

        private static AuroraBlockEntry ParseAuroraBlockEntry(XElement element)
        {
            var block = new AuroraBlockEntry
            {
                name = element.Name.LocalName,
                value = element.Value,
                rawXml = element.ToString(SaveOptions.DisableFormatting)
            };

            foreach (var attribute in element.Attributes())
            {
                block.attributes[attribute.Name.LocalName] = attribute.Value;
            }

            return block;
        }

        private static void FillSetters(AuroraSetters setters, XElement parentElement)
        {
            var settersType = typeof(AuroraSetters);
            var setterProps = settersType.GetProperties().ToList();

            foreach (var setter in parentElement.Elements("set"))
            {
                string setterName = setter.Attribute("name")?.Value;

                if (string.IsNullOrWhiteSpace(setterName))
                    continue;

                var setterEntry = new AuroraSetterEntry
                {
                    name = setterName,
                    value = setter.Value
                };

                foreach (var attribute in setter.Attributes().Where(x => x.Name.LocalName != "name"))
                {
                    setterEntry.attributes[attribute.Name.LocalName] = attribute.Value;
                }

                setters.entries.Add(setterEntry);

                if (string.Equals(setterName, "keywords", StringComparison.OrdinalIgnoreCase))
                {
                    setters.keywords = SplitTopLevel(setter.Value, ',');
                    continue;
                }

                if (string.Equals(setterName, "names", StringComparison.OrdinalIgnoreCase))
                {
                    setters.names ??= new List<Names>();
                    setters.names.Add(new Names
                    {
                        type = setterEntry.GetAttribute("type"),
                        names = SplitTopLevel(setter.Value, ',')
                    });
                    continue;
                }

                if (string.Equals(setterName, "multiclass proficiencies", StringComparison.OrdinalIgnoreCase))
                {
                    setters.multiclass_proficiencies = SplitTopLevel(setter.Value, ',');
                    continue;
                }

                string normalizedSetterName = NormalizeSetterPropertyName(setterName);
                PropertyInfo setterProp = setterProps.FirstOrDefault(
                    x => string.Equals(x.Name, normalizedSetterName, StringComparison.OrdinalIgnoreCase));

                if (setterProp != null)
                {
                    string content = setter.Value;

                    if (setterProp.PropertyType.Equals(typeof(string)))
                    {
                        setterProp.SetValue(setters, content);
                    }
                    else if (!string.IsNullOrWhiteSpace(content))
                    {
                        TypeConverter typeConverter = TypeDescriptor.GetConverter(setterProp.PropertyType);

                        try
                        {
                            setterProp.SetValue(setters, typeConverter.ConvertFromString(content));
                        }
                        catch
                        {
                            // Keep the raw setter entry even when a typed projection does not parse cleanly.
                        }
                    }
                }
            }
        }

        private static AuroraTextCollection ParseAuroraTextCollection(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
                return null;

            var collection = new AuroraTextCollection
            {
                raw = rawText.Trim()
            };

            collection.AddRange(SplitTopLevel(rawText, ','));

            return collection;
        }

        private static List<string> SplitTopLevel(string input, char separator)
        {
            var values = new List<string>();

            if (string.IsNullOrWhiteSpace(input))
                return values;

            int parenthesesDepth = 0;
            int bracketsDepth = 0;
            int bracesDepth = 0;
            var current = new System.Text.StringBuilder();

            foreach (char ch in input)
            {
                switch (ch)
                {
                    case '(':
                        parenthesesDepth++;
                        break;
                    case ')':
                        parenthesesDepth = Math.Max(0, parenthesesDepth - 1);
                        break;
                    case '[':
                        bracketsDepth++;
                        break;
                    case ']':
                        bracketsDepth = Math.Max(0, bracketsDepth - 1);
                        break;
                    case '{':
                        bracesDepth++;
                        break;
                    case '}':
                        bracesDepth = Math.Max(0, bracesDepth - 1);
                        break;
                }

                if (ch == separator
                    && parenthesesDepth == 0
                    && bracketsDepth == 0
                    && bracesDepth == 0)
                {
                    string candidate = current.ToString().Trim();

                    if (!string.IsNullOrWhiteSpace(candidate))
                    {
                        values.Add(candidate);
                    }

                    current.Clear();
                    continue;
                }

                current.Append(ch);
            }

            string finalCandidate = current.ToString().Trim();

            if (!string.IsNullOrWhiteSpace(finalCandidate))
            {
                values.Add(finalCandidate);
            }

            return values;
        }

        private static bool? ParseNullableBoolean(string value)
        {
            if (bool.TryParse(value, out bool parsed))
            {
                return parsed;
            }

            return null;
        }

        private static string NormalizeSetterPropertyName(string setterName)
        {
            return setterName
                .Replace("-", "_")
                .Replace(" ", "_");
        }

        private static string BuildSlug(string value)
        {
            return value?.Trim().ToLower().Replace(" ", "-");
        }

    }
}

