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
        static string defaultCharacterStatePath = Path.Combine(
            projectRootPath,
            "Data",
            "character-state-example.json");
        static string defaultCharacterStateBaselinePath = Path.Combine(
            projectRootPath,
            "Data",
            "character-state-regression-baseline.json");
        static string defaultFirstPartyBaselineSqlitePath = Path.Combine(
            projectRootPath,
            "Data",
            "aurora-first-party-regression.sqlite");
        static string defaultWpfParityBaselinePath = Path.Combine(
            projectRootPath,
            "Data",
            "wpf-parity-baseline.json");

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

            if (args.Length > 0
                && string.Equals(args[0], "capture-character-state-baseline", StringComparison.OrdinalIgnoreCase))
            {
                string sqlitePath = args.Length > 1 ? args[1] : defaultFirstPartyBaselineSqlitePath;
                string stateJsonPath = args.Length > 2 ? args[2] : defaultCharacterStatePath;
                string baselinePath = args.Length > 3 ? args[3] : defaultCharacterStateBaselinePath;

                CaptureCharacterStateBaseline(sqlitePath, stateJsonPath, baselinePath);
                return;
            }

            if (args.Length > 0
                && string.Equals(args[0], "check-character-state-regression", StringComparison.OrdinalIgnoreCase))
            {
                string sqlitePath = args.Length > 1 ? args[1] : defaultFirstPartyBaselineSqlitePath;
                string stateJsonPath = args.Length > 2 ? args[2] : defaultCharacterStatePath;
                string baselinePath = args.Length > 3 ? args[3] : defaultCharacterStateBaselinePath;

                CheckCharacterStateRegression(sqlitePath, stateJsonPath, baselinePath);
                return;
            }

            if (args.Length > 0
                && string.Equals(args[0], "capture-wpf-parity-baseline", StringComparison.OrdinalIgnoreCase))
            {
                string sqlitePath = args.Length > 1 ? args[1] : defaultFirstPartyBaselineSqlitePath;
                string baselinePath = args.Length > 2 ? args[2] : defaultWpfParityBaselinePath;

                CaptureWpfParityBaseline(sqlitePath, baselinePath);
                return;
            }

            if (args.Length > 0
                && string.Equals(args[0], "capture-first-party-wpf-parity-baseline", StringComparison.OrdinalIgnoreCase))
            {
                string auroraRootPath = args.Length > 1 ? args[1] : defaultAuroraPath;
                string sqlitePath = args.Length > 2 ? args[2] : defaultFirstPartyBaselineSqlitePath;
                string baselinePath = args.Length > 3 ? args[3] : defaultWpfParityBaselinePath;

                CaptureFirstPartyWpfParityBaseline(auroraRootPath, sqlitePath, baselinePath);
                return;
            }

            if (args.Length > 0
                && string.Equals(args[0], "check-wpf-parity-regression", StringComparison.OrdinalIgnoreCase))
            {
                string sqlitePath = args.Length > 1 ? args[1] : defaultFirstPartyBaselineSqlitePath;
                string baselinePath = args.Length > 2 ? args[2] : defaultWpfParityBaselinePath;

                CheckWpfParityRegression(sqlitePath, baselinePath);
                return;
            }

            if (args.Length > 0
                && string.Equals(args[0], "evaluate-character-state", StringComparison.OrdinalIgnoreCase))
            {
                string sqlitePath = args.Length > 1 ? args[1] : defaultSqlitePath;
                string stateJsonPath = args.Length > 2 ? args[2] : null;

                EvaluateCharacterState(sqlitePath, stateJsonPath);
                return;
            }

            if (args.Length > 0
                && string.Equals(args[0], "evaluate-character-state-json", StringComparison.OrdinalIgnoreCase))
            {
                string sqlitePath = args.Length > 1 ? args[1] : defaultSqlitePath;
                string stateJsonPath = args.Length > 2 ? args[2] : null;
                string outputPath = args.Length > 3 ? args[3] : null;

                EvaluateCharacterStateJson(sqlitePath, stateJsonPath, outputPath);
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
            Console.WriteLine("  capture-character-state-baseline [sqlitePath] [stateJson] [baselinePath]");
            Console.WriteLine("                                                      Save a computed-character regression baseline for a state fixture.");
            Console.WriteLine("  check-character-state-regression [sqlitePath] [stateJson] [baselinePath]");
            Console.WriteLine("                                                      Compare computed-character output against a saved baseline.");
            Console.WriteLine("  capture-wpf-parity-baseline [sqlitePath] [baselinePath]");
            Console.WriteLine("                                                      Save a WPF-authoritative parity snapshot for core DB loader surfaces.");
            Console.WriteLine("  capture-first-party-wpf-parity-baseline [auroraRoot] [sqlitePath] [baselinePath]");
            Console.WriteLine("                                                      Rebuild a canonical first-party DB and capture a WPF parity baseline.");
            Console.WriteLine("  check-wpf-parity-regression [sqlitePath] [baselinePath]");
            Console.WriteLine("                                                      Compare current DB loader surfaces against a saved WPF parity baseline.");
            Console.WriteLine("  evaluate-character-state [sqlitePath] [stateJson]");
            Console.WriteLine("                                                      Resolve a character state into active features, grants, and selects.");
            Console.WriteLine("  evaluate-character-state-json [sqlitePath] [stateJson] [outputPath]");
            Console.WriteLine("                                                      Emit a structured JSON character-state evaluation payload.");
            Console.WriteLine($"Default Aurora path:  {defaultAuroraPath}");
            Console.WriteLine($"Default SQLite path:  {defaultSqlitePath}");
            Console.WriteLine($"Default baseline:     {defaultDiagnosticsBaselinePath}");
            Console.WriteLine($"Default state JSON:   {defaultCharacterStatePath}");
            Console.WriteLine($"Default state base:   {defaultCharacterStateBaselinePath}");
            Console.WriteLine($"Default 1P SQLite:    {defaultFirstPartyBaselineSqlitePath}");
            Console.WriteLine($"Default WPF base:     {defaultWpfParityBaselinePath}");
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
            else if (args.Length > 0
                && string.Equals(args[0], "capture-character-state-baseline", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("Usage: capture-character-state-baseline [sqlitePath] [stateJsonPath] [baselinePath]");
                Console.Error.WriteLine($"Default SQLite path:   {defaultFirstPartyBaselineSqlitePath}");
                Console.Error.WriteLine($"Default state path:    {defaultCharacterStatePath}");
                Console.Error.WriteLine($"Default baseline path: {defaultCharacterStateBaselinePath}");
            }
            else if (args.Length > 0
                && string.Equals(args[0], "check-character-state-regression", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("Usage: check-character-state-regression [sqlitePath] [stateJsonPath] [baselinePath]");
                Console.Error.WriteLine($"Default SQLite path:   {defaultFirstPartyBaselineSqlitePath}");
                Console.Error.WriteLine($"Default state path:    {defaultCharacterStatePath}");
                Console.Error.WriteLine($"Default baseline path: {defaultCharacterStateBaselinePath}");
            }
            else if (args.Length > 0
                && string.Equals(args[0], "capture-wpf-parity-baseline", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("Usage: capture-wpf-parity-baseline [sqlitePath] [baselinePath]");
                Console.Error.WriteLine($"Default SQLite path:   {defaultFirstPartyBaselineSqlitePath}");
                Console.Error.WriteLine($"Default baseline path: {defaultWpfParityBaselinePath}");
            }
            else if (args.Length > 0
                && string.Equals(args[0], "capture-first-party-wpf-parity-baseline", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("Usage: capture-first-party-wpf-parity-baseline [auroraRoot] [sqlitePath] [baselinePath]");
                Console.Error.WriteLine($"Default Aurora path:   {defaultAuroraPath}");
                Console.Error.WriteLine($"Default SQLite path:   {defaultFirstPartyBaselineSqlitePath}");
                Console.Error.WriteLine($"Default baseline path: {defaultWpfParityBaselinePath}");
            }
            else if (args.Length > 0
                && string.Equals(args[0], "check-wpf-parity-regression", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("Usage: check-wpf-parity-regression [sqlitePath] [baselinePath]");
                Console.Error.WriteLine($"Default SQLite path:   {defaultFirstPartyBaselineSqlitePath}");
                Console.Error.WriteLine($"Default baseline path: {defaultWpfParityBaselinePath}");
            }
            else if (args.Length > 0
                && string.Equals(args[0], "evaluate-character-state", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("Usage: evaluate-character-state [sqlitePath] [stateJsonPath]");
                Console.Error.WriteLine("State JSON example:");
                Console.Error.WriteLine(@"  {");
                Console.Error.WriteLine(@"    ""classes"": [{ ""name"": ""Fighter"", ""packageKey"": ""core-players-handbook"", ""level"": 5 }],");
                Console.Error.WriteLine(@"    ""archetypes"": [{ ""name"": ""Champion"", ""packageKey"": ""core-players-handbook"" }],");
                Console.Error.WriteLine(@"    ""race"": { ""name"": ""Human"", ""packageKey"": ""core-players-handbook"" },");
                Console.Error.WriteLine(@"    ""background"": { ""name"": ""Acolyte"", ""packageKey"": ""core-players-handbook"" },");
                Console.Error.WriteLine(@"    ""numericValues"": { ""str"": 16, ""dex"": 14, ""con"": 14, ""int"": 10, ""wis"": 12, ""cha"": 8 }");
                Console.Error.WriteLine(@"  }");
            }
            else if (args.Length > 0
                && string.Equals(args[0], "evaluate-character-state-json", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("Usage: evaluate-character-state-json [sqlitePath] [stateJsonPath] [outputPath]");
                Console.Error.WriteLine("If outputPath is omitted, JSON is written to stdout.");
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

            // Running as a published bundle — .csproj won't be present; fall back to exe directory.
            // The schema SQL is published alongside the exe under Data/.
            return AppContext.BaseDirectory;
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

        private static void EvaluateCharacterState(string sqlitePath, string stateJsonPath)
        {
            if (string.IsNullOrWhiteSpace(stateJsonPath))
                throw new ArgumentException("A character state JSON path is required.", nameof(stateJsonPath));

            CharacterEvaluationResult result = AuroraCharacterStateEngine.Evaluate(sqlitePath, stateJsonPath);

            Console.WriteLine("Character State Evaluation");
            Console.WriteLine($"SQLite:     {sqlitePath}");
            Console.WriteLine($"State JSON: {stateJsonPath}");
            Console.WriteLine();

            Console.WriteLine($"Direct selections: {result.DirectSelections.Count}");
            foreach (ResolvedCharacterElement selection in result.DirectSelections)
            {
                string levelText = selection.Level.HasValue ? $" L{selection.Level.Value}" : string.Empty;
                Console.WriteLine($"  - {selection.TypeName}: {selection.Name}{levelText} [{selection.PackageKey ?? "(no package)"}]");
            }

            Console.WriteLine();
            Console.WriteLine($"Active features: {result.ActiveFeatures.Count}");
            foreach (IGrouping<string, ActiveCharacterFeature> ownerGroup in result.ActiveFeatures
                         .GroupBy(x => $"{x.OwnerTypeName}: {x.OwnerName}")
                         .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine($"  {ownerGroup.Key}");
                foreach (ActiveCharacterFeature feature in ownerGroup
                             .OrderBy(x => x.UnlockLevel)
                             .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                             .Take(12))
                {
                    Console.WriteLine($"    - L{feature.UnlockLevel}: {feature.Name} [{feature.PackageKey ?? "(no package)"}]");
                }

                int remainingCount = ownerGroup.Count() - 12;
                if (remainingCount > 0)
                    Console.WriteLine($"    ... {remainingCount} more");
            }

            Console.WriteLine();
            Console.WriteLine($"Active grants: {result.ActiveGrants.Count}");
            foreach (IGrouping<string, ActiveGrantResult> grantGroup in result.ActiveGrants
                         .GroupBy(x => x.GrantType)
                         .OrderByDescending(x => x.Count())
                         .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine($"  {grantGroup.Key}: {grantGroup.Count()}");
                foreach (ActiveGrantResult grant in grantGroup.Take(8))
                {
                    string targetText = !string.IsNullOrWhiteSpace(grant.TargetName)
                        ? $"{grant.TargetName} [{grant.TargetPackageKey ?? "(no package)"}]"
                        : (!string.IsNullOrWhiteSpace(grant.TargetSemanticName)
                            ? grant.TargetSemanticName
                            : grant.TargetSemanticKey ?? "(unresolved)");
                    string spellcastingText = !string.IsNullOrWhiteSpace(grant.SpellcastingName)
                        ? $" via {grant.SpellcastingName}{(grant.IsPrepared == true ? " (prepared)" : string.Empty)}"
                        : string.Empty;
                    Console.WriteLine($"    - {grant.OwnerName}: {targetText}{spellcastingText}");
                }

                int remainingCount = grantGroup.Count() - 8;
                if (remainingCount > 0)
                    Console.WriteLine($"    ... {remainingCount} more");
            }

            Console.WriteLine();
            Console.WriteLine($"Available selects: {result.AvailableSelects.Count}");
            foreach (CharacterSelectResult select in result.AvailableSelects
                         .OrderBy(x => x.OwnerTypeName, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(x => x.OwnerName, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(x => x.SelectLevel ?? 0)
                         .ThenBy(x => x.SelectName, StringComparer.OrdinalIgnoreCase))
            {
                int availableOptionCount = select.Options.Count(x => x.IsAvailable);
                int ownedOptionCount = select.Options.Count(x => x.IsAlreadyOwned);
                Console.WriteLine($"  - {select.OwnerTypeName}: {select.OwnerName} -> {select.SelectName} ({select.SelectType}, policy {select.SelectPolicy}, family {select.ChoiceFamily}, choose {select.NumberToChoose}, options {availableOptionCount}/{select.Options.Count})");

                foreach (CharacterSelectOptionResult option in select.Options
                             .Where(x => x.IsAvailable)
                             .OrderBy(x => x.OptionName ?? x.OptionText, StringComparer.OrdinalIgnoreCase)
                             .Take(8))
                {
                    WriteCharacterSelectOption(option, 3, "*", 12);
                }

                if (ownedOptionCount > 0)
                    Console.WriteLine($"      already-owned options in pool: {ownedOptionCount}");

                int remainingCount = availableOptionCount - 8;
                if (remainingCount > 0)
                    Console.WriteLine($"      ... {remainingCount} more available options");
            }

            if (result.AppliedChoices?.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine($"Applied choices: {result.AppliedChoices.Count}");
                foreach (AppliedCharacterChoiceResult appliedChoice in result.AppliedChoices)
                {
                    string optionText = appliedChoice.FollowUpOptionName
                        ?? appliedChoice.OptionName
                        ?? "(unspecified option)";
                    Console.WriteLine(
                        $"  - [{appliedChoice.Status}] {appliedChoice.OwnerTypeName}: {appliedChoice.OwnerName} -> {appliedChoice.SelectName} ({appliedChoice.ChoiceFamily}) => {optionText}");
                    if (!string.IsNullOrWhiteSpace(appliedChoice.Message))
                        Console.WriteLine($"      {appliedChoice.Message}");
                }
            }

            Console.WriteLine();
            Console.WriteLine($"Evaluation tokens: {result.EvaluationContext.Tokens.Count}");
            Console.WriteLine($"Evaluation numeric keys: {result.EvaluationContext.NumericValues.Count}");
            Console.WriteLine($"Evaluation macros: {result.EvaluationContext.MacroValues.Count}");
        }

        private static void EvaluateCharacterStateJson(string sqlitePath, string stateJsonPath, string outputPath)
        {
            if (string.IsNullOrWhiteSpace(stateJsonPath))
                throw new ArgumentException("A character state JSON path is required.", nameof(stateJsonPath));

            CharacterEvaluationResult result = AuroraCharacterStateEngine.Evaluate(sqlitePath, stateJsonPath);
            object export = BuildCharacterEvaluationExport(sqlitePath, stateJsonPath, result);
            string json = JsonSerializer.Serialize(
                export,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

            if (!string.IsNullOrWhiteSpace(outputPath))
            {
                File.WriteAllText(outputPath, json);
                Console.WriteLine($"Wrote character-state JSON evaluation to {outputPath}");
                return;
            }

            Console.WriteLine(json);
        }

        private static object BuildCharacterEvaluationExport(
            string sqlitePath,
            string stateJsonPath,
            CharacterEvaluationResult result)
        {
            return new
            {
                sqlitePath,
                stateJsonPath,
                generatedAtUtc = DateTime.UtcNow,
                summary = new
                {
                    directSelectionCount = result.DirectSelections.Count,
                    activeFeatureCount = result.ActiveFeatures.Count,
                activeGrantCount = result.ActiveGrants.Count,
                availableSelectCount = result.AvailableSelects.Count,
                pendingChoiceCount = result.ComputedCharacter?.PendingChoices.Count ?? 0,
                warningCount = result.ComputedCharacter?.Warnings.Count ?? 0,
                evaluationTokenCount = result.EvaluationContext.Tokens.Count,
                evaluationNumericKeyCount = result.EvaluationContext.NumericValues.Count,
                evaluationMacroCount = result.EvaluationContext.MacroValues.Count
            },
                directSelections = result.DirectSelections.Select(selection => new
                {
                    selection.ElementId,
                    selection.AuroraId,
                    selection.Name,
                    selection.TypeName,
                    selection.PackageKey,
                    selection.SourcePath,
                    selection.Level
                }).ToList(),
                activeFeatures = result.ActiveFeatures.Select(feature => new
                {
                    feature.ElementId,
                    feature.AuroraId,
                    feature.Name,
                    feature.TypeName,
                    feature.PackageKey,
                    feature.SourcePath,
                    feature.UnlockLevel,
                    feature.OwnerName,
                    feature.OwnerTypeName
                }).ToList(),
                activeGrants = result.ActiveGrants.Select(grant => new
                {
                    grant.GrantId,
                    grant.OwnerName,
                    grant.OwnerTypeName,
                    grant.GrantType,
                    grant.GrantLevel,
                    grant.SpellcastingName,
                    grant.IsPrepared,
                    grant.RequirementsText,
                    grant.TargetElementId,
                    grant.TargetAuroraId,
                    grant.TargetName,
                    grant.TargetTypeName,
                    grant.TargetPackageKey,
                    grant.TargetSemanticKey,
                    grant.TargetSemanticKind,
                    grant.TargetSemanticName
                }).ToList(),
                appliedChoices = result.AppliedChoices.Select(choice => new
                {
                    choice.ChoiceIndex,
                    choice.SelectId,
                    choice.ChoiceKey,
                    choice.ChoiceRowKey,
                    choice.OwnerName,
                    choice.OwnerTypeName,
                    choice.SelectName,
                    choice.SelectType,
                    choice.ChoiceFamily,
                    choice.OptionName,
                    choice.OptionAuroraId,
                    choice.FollowUpOptionName,
                    choice.FollowUpOptionAuroraId,
                    choice.Status,
                    choice.Message
                }).ToList(),
                computedCharacter = result.ComputedCharacter == null
                    ? null
                    : new
                    {
                        abilityScores = result.ComputedCharacter.AbilityScores.Select(score => new
                        {
                            score.AbilityKey,
                            score.AbilityName,
                            score.BaseValue,
                            score.ModifierTotal,
                            score.FinalValue,
                            provenance = score.Provenance.Select(BuildCharacterProvenanceExport).ToList()
                        }).ToList(),
                        proficiencies = result.ComputedCharacter.Proficiencies.Select(BuildComputedCharacterItemExport).ToList(),
                        languages = result.ComputedCharacter.Languages.Select(BuildComputedCharacterItemExport).ToList(),
                        feats = result.ComputedCharacter.Feats.Select(BuildComputedCharacterItemExport).ToList(),
                        features = result.ComputedCharacter.Features.Select(BuildComputedCharacterItemExport).ToList(),
                        grantedSpells = result.ComputedCharacter.GrantedSpells.Select(BuildComputedGrantedSpellExport).ToList(),
                        spellcastingProfiles = result.ComputedCharacter.SpellcastingProfiles.Select(BuildSpellcastingProfileExport).ToList(),
                        effectRows = result.ComputedCharacter.EffectRows.Select(BuildComputedEffectRowExport).ToList(),
                        choiceSelections = result.ComputedCharacter.ChoiceSelections.Select(BuildComputedCharacterItemExport).ToList(),
                        movements = result.ComputedCharacter.Traits
                            .Where(item => string.Equals(item.TypeName, "movement", StringComparison.OrdinalIgnoreCase)
                                           || string.Equals(item.Category, "movement", StringComparison.OrdinalIgnoreCase))
                            .Select(BuildComputedCharacterItemExport)
                            .ToList(),
                        senses = result.ComputedCharacter.Traits
                            .Where(item => string.Equals(item.TypeName, "sense", StringComparison.OrdinalIgnoreCase)
                                           || string.Equals(item.Category, "sense", StringComparison.OrdinalIgnoreCase))
                            .Select(BuildComputedCharacterItemExport)
                            .ToList(),
                        traits = result.ComputedCharacter.Traits.Select(BuildComputedCharacterItemExport).ToList(),
                        pendingChoices = result.ComputedCharacter.PendingChoices.Select(choice => new
                        {
                            choice.SelectId,
                            choice.ChoiceKey,
                            choice.ChoiceRowKey,
                            choice.OwnerName,
                            choice.OwnerTypeName,
                            choice.OwnerPackageKey,
                            choice.SelectName,
                            choice.SelectType,
                            choice.SelectPolicy,
                            choice.ChoiceFamily,
                            choice.NumberToChoose,
                            choice.ChosenCount,
                            choice.AlreadyOwnedCount,
                            choice.RemainingCount,
                            choice.AvailableOptionCount,
                            choice.IsOptional,
                            choice.IsBlocking
                        }).ToList(),
                        warnings = result.ComputedCharacter.Warnings.Select(warning => new
                        {
                            warning.WarningKind,
                            warning.Severity,
                            warning.Message,
                            warning.OwnerName,
                            warning.OwnerTypeName,
                            warning.SelectName
                        }).ToList(),
                        provenance = result.ComputedCharacter.Provenance.Select(BuildCharacterProvenanceExport).ToList()
                    },
                availableSelects = result.AvailableSelects.Select(select => new
                {
                    select.SelectId,
                    select.ChoiceKey,
                    select.ChoiceRowKey,
                    select.OwnerName,
                    select.OwnerTypeName,
                    select.OwnerPackageKey,
                    select.SelectName,
                    select.SelectType,
                    select.SelectPolicy,
                    select.ChoiceFamily,
                    select.SupportsText,
                    select.SelectLevel,
                    select.NumberToChoose,
                    select.IsOptional,
                    select.RequirementsText,
                    availableOptionCount = select.Options.Count(option => option.IsAvailable),
                    alreadyOwnedOptionCount = select.Options.Count(option => option.IsAlreadyOwned),
                    options = select.Options.Select(BuildCharacterSelectOptionExport).ToList()
                }).ToList(),
                evaluationContext = new
                {
                    tokens = result.EvaluationContext.Tokens
                        .OrderBy(token => token, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    numericValues = result.EvaluationContext.NumericValues
                        .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(pair => pair.Key, pair => pair.Value),
                    scalarValues = result.EvaluationContext.ScalarValues
                        .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(pair => pair.Key, pair => pair.Value),
                    macroValues = result.EvaluationContext.MacroValues
                        .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(
                            pair => pair.Key,
                            pair => pair.Value.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList())
                },
                appContract = new
                {
                    choiceRows = result.AvailableSelects.Select(BuildChoiceRowExport).ToList(),
                    pendingChoiceRows = result.ComputedCharacter?.PendingChoices.Select(BuildPendingChoiceRowExport).ToList()
                        ?? new List<object>(),
                    grantedSpells = result.ComputedCharacter?.GrantedSpells.Select(BuildComputedGrantedSpellExport).ToList()
                        ?? new List<object>(),
                    spellcastingProfiles = result.ComputedCharacter?.SpellcastingProfiles.Select(BuildSpellcastingProfileExport).ToList()
                        ?? new List<object>(),
                    effectRows = result.ComputedCharacter?.EffectRows.Select(BuildComputedEffectRowExport).ToList()
                        ?? new List<object>()
                }
            };
        }

        private static object BuildComputedCharacterItemExport(ComputedCharacterItemResult item)
        {
            return new
            {
                item.Category,
                item.Key,
                item.Name,
                item.TypeName,
                item.PackageKey,
                item.IsDirectSelection,
                provenance = item.Provenance.Select(BuildCharacterProvenanceExport).ToList()
            };
        }

        private static object BuildComputedGrantedSpellExport(ComputedGrantedSpellResult spell)
        {
            return new
            {
                spell.SpellKey,
                spell.SpellAuroraId,
                spell.SpellName,
                spell.SpellPackageKey,
                spell.SpellcastingName,
                spell.IsPrepared,
                spell.GrantLevel,
                provenance = spell.Provenance.Select(BuildCharacterProvenanceExport).ToList()
            };
        }

        private static object BuildSpellcastingProfileExport(ComputedSpellcastingProfileResult profile)
        {
            return new
            {
                profile.ProfileKey,
                profile.SpellcastingName,
                profile.GrantedSpellCount,
                profile.PreparedSpellCount,
                profile.UnpreparedSpellCount,
                spellKeys = profile.SpellKeys.ToList(),
                provenance = profile.Provenance.Select(BuildCharacterProvenanceExport).ToList()
            };
        }

        private static object BuildComputedEffectRowExport(ComputedEffectRowResult effect)
        {
            return new
            {
                effect.EffectKind,
                effect.EffectSubkind,
                effect.EffectKey,
                effect.DisplayName,
                effect.ValueText,
                effect.NumericValue,
                effect.PackageKey,
                effect.IsDirectSelection,
                provenance = effect.Provenance.Select(BuildCharacterProvenanceExport).ToList()
            };
        }

        private static object BuildCharacterProvenanceExport(CharacterProvenanceEntry provenance)
        {
            return new
            {
                provenance.Category,
                provenance.Key,
                provenance.SourceKind,
                provenance.OwnerName,
                provenance.OwnerTypeName,
                provenance.PackageKey,
                provenance.ElementAuroraId,
                provenance.ElementName,
                provenance.Detail
            };
        }

        private static object BuildCharacterSelectOptionExport(CharacterSelectOptionResult option)
        {
            return new
            {
                option.OptionKind,
                option.OptionElementId,
                option.OptionAuroraId,
                option.OptionName,
                option.OptionTypeName,
                option.OptionPackageKey,
                option.OptionText,
                option.IsAvailable,
                option.IsAlreadyOwned,
                option.RequirementText,
                option.FollowUpKind,
                followUpOptionCount = option.FollowUpOptions?.Count ?? 0,
                followUpOptions = option.FollowUpOptions?.Select(BuildCharacterSelectOptionExport).ToList()
                    ?? new List<object>()
            };
        }

        private static object BuildChoiceRowExport(CharacterSelectResult select)
        {
            return new
            {
                select.SelectId,
                select.ChoiceKey,
                select.ChoiceRowKey,
                select.OwnerName,
                select.OwnerTypeName,
                select.OwnerPackageKey,
                select.SelectName,
                select.SelectType,
                select.SelectPolicy,
                select.ChoiceFamily,
                select.SupportsText,
                select.SelectLevel,
                select.NumberToChoose,
                select.IsOptional,
                select.RequirementsText,
                availableOptionCount = select.Options.Count(option => option.IsAvailable),
                alreadyOwnedOptionCount = select.Options.Count(option => option.IsAlreadyOwned)
            };
        }

        private static object BuildPendingChoiceRowExport(PendingCharacterChoiceResult choice)
        {
            return new
            {
                choice.SelectId,
                choice.ChoiceKey,
                choice.ChoiceRowKey,
                choice.OwnerName,
                choice.OwnerTypeName,
                choice.OwnerPackageKey,
                choice.SelectName,
                choice.SelectType,
                choice.SelectPolicy,
                choice.ChoiceFamily,
                choice.NumberToChoose,
                choice.ChosenCount,
                choice.AlreadyOwnedCount,
                choice.RemainingCount,
                choice.AvailableOptionCount,
                choice.IsOptional,
                choice.IsBlocking
            };
        }

        private static void WriteCharacterSelectOption(
            CharacterSelectOptionResult option,
            int indentLevel,
            string bullet,
            int maxItemsPerLevel)
        {
            string indent = new string(' ', indentLevel * 2);
            string optionLabel = option.OptionName ?? option.OptionText ?? "(unnamed option)";
            string ownedSuffix = option.IsAlreadyOwned ? " [already owned]" : string.Empty;
            string packageSuffix = !string.IsNullOrWhiteSpace(option.OptionPackageKey)
                ? $" [{option.OptionPackageKey}]"
                : string.Empty;
            Console.WriteLine($"{indent}{bullet} {optionLabel}{packageSuffix}{ownedSuffix}");

            IReadOnlyList<CharacterSelectOptionResult> followUpOptions = option.FollowUpOptions
                ?.Where(x => x.IsAvailable)
                .OrderBy(x => x.OptionName ?? x.OptionText, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (option.IsAlreadyOwned && followUpOptions?.Count > 0)
            {
                string followUpIndent = new string(' ', (indentLevel + 1) * 2);
                Console.WriteLine($"{followUpIndent}follow-up preview hidden for already-owned option");
                return;
            }

            if (followUpOptions?.Count > 0)
            {
                string followUpIndent = new string(' ', (indentLevel + 1) * 2);
                Console.WriteLine($"{followUpIndent}follow-up ({option.FollowUpKind ?? "choices"}): {followUpOptions.Count}");

                foreach (CharacterSelectOptionResult followUp in followUpOptions.Take(maxItemsPerLevel))
                {
                    WriteCharacterSelectOption(followUp, indentLevel + 2, "-", maxItemsPerLevel);
                }

                int remainingFollowUpCount = followUpOptions.Count - maxItemsPerLevel;
                if (remainingFollowUpCount > 0)
                {
                    string overflowIndent = new string(' ', (indentLevel + 2) * 2);
                    Console.WriteLine($"{overflowIndent}... {remainingFollowUpCount} more follow-up options");
                }
            }
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

        private static void CaptureCharacterStateBaseline(string sqlitePath, string stateJsonPath, string baselinePath)
        {
            CharacterStateRegressionBaseline baseline = BuildCharacterStateRegressionBaseline(sqlitePath, stateJsonPath);
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
            Console.WriteLine($"Captured character-state regression baseline to {baselinePath}.");
            Console.WriteLine($"  direct selections: {baseline.DirectSelectionCount}");
            Console.WriteLine($"  active features:   {baseline.ActiveFeatureCount}");
            Console.WriteLine($"  pending choices:   {baseline.PendingChoiceCount}");
            Console.WriteLine($"  warnings:          {baseline.WarningCount}");
        }

        private static void CheckCharacterStateRegression(string sqlitePath, string stateJsonPath, string baselinePath)
        {
            if (!File.Exists(baselinePath))
                throw new FileNotFoundException($"Character-state baseline not found: {baselinePath}");

            CharacterStateRegressionBaseline expected = JsonSerializer.Deserialize<CharacterStateRegressionBaseline>(
                File.ReadAllText(baselinePath))
                ?? throw new InvalidDataException($"Could not deserialize character-state baseline: {baselinePath}");

            CharacterStateRegressionBaseline actual = BuildCharacterStateRegressionBaseline(sqlitePath, stateJsonPath);
            List<string> failures = CompareCharacterStateRegressionBaseline(expected, actual);

            Console.WriteLine($"Character-state regression check for {sqlitePath}:");
            Console.WriteLine($"State JSON: {stateJsonPath}");
            Console.WriteLine($"Baseline:   {baselinePath}");

            if (failures.Count == 0)
            {
                Console.WriteLine("PASS");
                Console.WriteLine($"  direct selections: {actual.DirectSelectionCount}");
                Console.WriteLine($"  active features:   {actual.ActiveFeatureCount}");
                Console.WriteLine($"  pending choices:   {actual.PendingChoiceCount}");
                Console.WriteLine($"  warnings:          {actual.WarningCount}");
                return;
            }

            Console.WriteLine("FAIL");
            foreach (string failure in failures)
                Console.WriteLine($"  - {failure}");

            Environment.ExitCode = 1;
        }

        private static void CaptureWpfParityBaseline(string sqlitePath, string baselinePath)
        {
            WpfParityRegressionBaseline baseline = BuildWpfParityRegressionBaseline(
                sqlitePath,
                "Current SQLite content set");

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
            Console.WriteLine($"Captured WPF parity baseline to {baselinePath}.");
            Console.WriteLine($"  total elements:      {baseline.TotalElementCount}");
            Console.WriteLine($"  spell profiles:      {baseline.SpellcastingProfileCount}");
            Console.WriteLine($"  extended profiles:   {baseline.ExtendedSpellcastingProfileCount}");
            Console.WriteLine($"  spell access rows:   {baseline.SpellAccessCount}");
            Console.WriteLine($"  companions:          {baseline.CompanionCount}");
        }

        private static void CaptureFirstPartyWpfParityBaseline(string auroraRootPath, string sqlitePath, string baselinePath)
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

                WpfParityRegressionBaseline baseline = BuildWpfParityRegressionBaseline(
                    sqlitePath,
                    "WPF-authoritative first-party core + supplements");

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

                Console.WriteLine($"Captured first-party WPF parity baseline to {baselinePath}.");
                Console.WriteLine($"SQLite source: {sqlitePath}");
                Console.WriteLine($"  total elements:      {baseline.TotalElementCount}");
                Console.WriteLine($"  spell profiles:      {baseline.SpellcastingProfileCount}");
                Console.WriteLine($"  extended profiles:   {baseline.ExtendedSpellcastingProfileCount}");
                Console.WriteLine($"  spell access rows:   {baseline.SpellAccessCount}");
                Console.WriteLine($"  companions:          {baseline.CompanionCount}");
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

        private static void CheckWpfParityRegression(string sqlitePath, string baselinePath)
        {
            if (!File.Exists(baselinePath))
                throw new FileNotFoundException($"WPF parity baseline not found: {baselinePath}");

            WpfParityRegressionBaseline expected = JsonSerializer.Deserialize<WpfParityRegressionBaseline>(
                File.ReadAllText(baselinePath))
                ?? throw new InvalidDataException($"Could not deserialize WPF parity baseline: {baselinePath}");

            WpfParityRegressionBaseline actual = BuildWpfParityRegressionBaseline(
                sqlitePath,
                expected.CorpusLabel ?? "Current SQLite content set");
            List<string> failures = CompareWpfParityRegressionBaseline(expected, actual);

            Console.WriteLine($"WPF parity regression check for {sqlitePath}:");
            Console.WriteLine($"Baseline: {baselinePath}");

            if (failures.Count == 0)
            {
                Console.WriteLine("PASS");
                Console.WriteLine($"  total elements:      {actual.TotalElementCount}");
                Console.WriteLine($"  spell profiles:      {actual.SpellcastingProfileCount}");
                Console.WriteLine($"  extended profiles:   {actual.ExtendedSpellcastingProfileCount}");
                Console.WriteLine($"  spell access rows:   {actual.SpellAccessCount}");
                Console.WriteLine($"  companions:          {actual.CompanionCount}");
                return;
            }

            Console.WriteLine("FAIL");
            foreach (string failure in failures)
                Console.WriteLine($"  - {failure}");

            Environment.ExitCode = 1;
        }

        private static CharacterStateRegressionBaseline BuildCharacterStateRegressionBaseline(string sqlitePath, string stateJsonPath)
        {
            CharacterEvaluationResult result = AuroraCharacterStateEngine.Evaluate(sqlitePath, stateJsonPath);
            ComputedCharacterResult computed = result.ComputedCharacter
                ?? throw new InvalidOperationException("Computed character output was unexpectedly null.");

            return new CharacterStateRegressionBaseline(
                CapturedAtUtc: DateTime.UtcNow,
                StateLabel: Path.GetFileName(stateJsonPath),
                DirectSelectionCount: result.DirectSelections.Count,
                DirectSelections: result.DirectSelections
                    .Select(x => $"{x.TypeName}|{x.Name}|{x.PackageKey}|{x.Level?.ToString() ?? string.Empty}")
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                ActiveFeatureCount: result.ActiveFeatures.Count,
                ActiveFeatures: result.ActiveFeatures
                    .Select(x => $"{x.OwnerTypeName}|{x.OwnerName}|{x.Name}|{x.UnlockLevel}")
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                ActiveGrantCount: result.ActiveGrants.Count,
                AvailableSelectCount: result.AvailableSelects.Count,
                SpellSelectOptionCounts: result.AvailableSelects
                    .Where(x => string.Equals(x.ChoiceFamily, "spell-pick", StringComparison.OrdinalIgnoreCase))
                    .Select(x => $"{x.OwnerTypeName}|{x.OwnerName}|{x.SelectName}|options={x.Options.Count}")
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                SpellSelectOptionKeys: result.AvailableSelects
                    .Where(x => string.Equals(x.ChoiceFamily, "spell-pick", StringComparison.OrdinalIgnoreCase))
                    .SelectMany(x => x.Options.Select(option =>
                        $"{x.OwnerTypeName}|{x.OwnerName}|{x.SelectName}|{option.OptionName}|{option.OptionPackageKey ?? string.Empty}|{option.OptionAuroraId ?? string.Empty}"))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                PendingChoiceCount: computed.PendingChoices.Count,
                BlockingPendingChoiceCount: computed.PendingChoices.Count(x => x.IsBlocking),
                WarningCount: computed.Warnings.Count,
                WarningKindCounts: BuildDiagnosticsCountList(computed.Warnings.GroupBy(x => $"{x.WarningKind}|{x.Severity}")),
                AbilityScores: computed.AbilityScores
                    .OrderBy(x => x.AbilityKey, StringComparer.OrdinalIgnoreCase)
                    .Select(x => new CharacterStateAbilityScoreBaseline(x.AbilityKey, x.FinalValue))
                    .ToArray(),
                ProficiencyKeys: computed.Proficiencies
                    .Select(x => x.Key)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                LanguageKeys: computed.Languages
                    .Select(x => x.Key)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                GrantedSpellKeys: computed.GrantedSpells
                    .Select(x => $"{x.SpellKey}|{x.SpellcastingName ?? string.Empty}|prepared={x.IsPrepared?.ToString() ?? string.Empty}")
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                SpellcastingProfileKeys: computed.SpellcastingProfiles
                    .Select(x => $"{x.ProfileKey}|granted={x.GrantedSpellCount}|prepared={x.PreparedSpellCount}|unprepared={x.UnpreparedSpellCount}")
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                FeatKeys: computed.Feats
                    .Select(x => x.Key)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                FeatureKeys: computed.Features
                    .Select(x => x.Key)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                ChoiceSelectionKeys: computed.ChoiceSelections
                    .Select(x => x.Key)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                TraitKeys: computed.Traits
                    .Select(x => x.Key)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                EffectRowKeys: computed.EffectRows
                    .Select(x => $"{x.EffectKind}|{x.EffectSubkind}|{x.EffectKey}|{x.ValueText ?? string.Empty}")
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                PendingChoiceKeys: computed.PendingChoices
                    .Select(x => $"{x.OwnerTypeName}|{x.OwnerName}|{x.SelectName}|family={x.ChoiceFamily}|remaining={x.RemainingCount}|blocking={x.IsBlocking}")
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                ProvenanceKindCounts: BuildDiagnosticsCountList(computed.Provenance.GroupBy(x => $"{x.Category}|{x.SourceKind}")),
                AppliedChoiceStates: result.AppliedChoices
                    .Select(x => $"{x.ChoiceIndex}|{x.Status}|{x.OwnerTypeName}|{x.OwnerName}|{x.SelectName}|family={x.ChoiceFamily}|{x.FollowUpOptionName ?? x.OptionName}")
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        }

        private static WpfParityRegressionBaseline BuildWpfParityRegressionBaseline(string sqlitePath, string corpusLabel)
        {
            using var connection = OpenSqliteConnection(sqlitePath);

            var metadata = ReadDatabaseMetadata(connection);

            return new WpfParityRegressionBaseline(
                CapturedAtUtc: DateTime.UtcNow,
                CorpusLabel: corpusLabel,
                SchemaVersion: metadata.SchemaVersion,
                DataVersion: metadata.DataVersion,
                SourceFileCount: metadata.SourceFileCount,
                MetadataElementCount: metadata.ElementCount,
                TotalElementCount: QueryInt64(connection, "SELECT COUNT(*) FROM elements;"),
                ElementTypeCounts: QueryCountList(
                    connection,
                    @"SELECT et.type_name AS key, COUNT(*) AS count
                      FROM elements AS e
                      JOIN element_types AS et
                        ON et.element_type_id = e.element_type_id
                      GROUP BY et.type_name"),
                ResolvedPackageCounts: QueryCountList(
                    connection,
                    @"SELECT COALESCE(package_key, '(none)') AS key, COUNT(*) AS count
                      FROM resolved_elements_cache
                      GROUP BY COALESCE(package_key, '(none)')"),
                FeatureKindCounts: QueryCountList(
                    connection,
                    @"SELECT feature_kind AS key, COUNT(*) AS count
                      FROM features
                      GROUP BY feature_kind"),
                SpellCount: QueryInt64(connection, "SELECT COUNT(*) FROM spells;"),
                SpellAccessCount: QueryInt64(connection, "SELECT COUNT(*) FROM spell_access;"),
                ClassCount: QueryInt64(connection, "SELECT COUNT(*) FROM classes;"),
                ArchetypeCount: QueryInt64(connection, "SELECT COUNT(*) FROM archetypes;"),
                MulticlassCount: QueryInt64(connection, "SELECT COUNT(*) FROM class_multiclass;"),
                SubraceCount: QueryInt64(connection, "SELECT COUNT(*) FROM subraces;"),
                RaceVariantCount: QueryInt64(connection, "SELECT COUNT(*) FROM race_variants;"),
                CompanionCount: QueryInt64(connection, "SELECT COUNT(*) FROM companions;"),
                CompanionCrTypeCounts: QueryCountList(
                    connection,
                    @"SELECT
                          COALESCE(creature_type, '(none)') || '|' || COALESCE(challenge_text, '(none)') AS key,
                          COUNT(*) AS count
                      FROM companions
                      GROUP BY COALESCE(creature_type, '(none)'), COALESCE(challenge_text, '(none)')"),
                SpellcastingProfileCount: QueryInt64(connection, "SELECT COUNT(*) FROM spellcasting_profiles;"),
                ExtendedSpellcastingProfileCount: QueryInt64(connection, "SELECT COUNT(*) FROM spellcasting_profiles WHERE is_extended = 1;"),
                PreparedSpellcastingProfileCount: QueryInt64(connection, "SELECT COUNT(*) FROM spellcasting_profiles WHERE prepare_spells = 1;"),
                AllowReplaceSpellcastingProfileCount: QueryInt64(connection, "SELECT COUNT(*) FROM spellcasting_profiles WHERE allow_replace = 1;"),
                SpellcastingProfilesWithExtendTextCount: QueryInt64(
                    connection,
                    "SELECT COUNT(*) FROM spellcasting_profiles WHERE extend_text IS NOT NULL AND trim(extend_text) <> '';"),
                RuleScopeCount: QueryInt64(connection, "SELECT COUNT(*) FROM rule_scopes;"),
                GrantCount: QueryInt64(connection, "SELECT COUNT(*) FROM grants;"),
                SelectCount: QueryInt64(connection, "SELECT COUNT(*) FROM selects;"),
                StatCount: QueryInt64(connection, "SELECT COUNT(*) FROM stats;"),
                SetterScopeCount: QueryInt64(connection, "SELECT COUNT(*) FROM setter_scopes;"),
                SetterEntryCount: QueryInt64(connection, "SELECT COUNT(*) FROM setter_entries;"),
                SetterAttributeCount: QueryInt64(connection, "SELECT COUNT(*) FROM setter_entry_attributes;"),
                RawGrantXmlCount: QueryInt64(connection, "SELECT COUNT(*) FROM grants WHERE raw_xml IS NOT NULL AND trim(raw_xml) <> '';"),
                RawSelectXmlCount: QueryInt64(connection, "SELECT COUNT(*) FROM selects WHERE raw_xml IS NOT NULL AND trim(raw_xml) <> '';"),
                RawStatXmlCount: QueryInt64(connection, "SELECT COUNT(*) FROM stats WHERE raw_xml IS NOT NULL AND trim(raw_xml) <> '';"),
                CuratedArchetypeLinks: BuildCuratedArchetypeLinks(connection),
                CuratedMulticlassRows: BuildCuratedMulticlassRows(connection),
                CuratedSpellcastingProfiles: BuildCuratedSpellcastingProfiles(connection),
                CuratedCompanionRows: BuildCuratedCompanionRows(connection),
                CuratedSpellAccessRows: BuildCuratedSpellAccessRows(connection));
        }

        private static List<string> CompareCharacterStateRegressionBaseline(
            CharacterStateRegressionBaseline expected,
            CharacterStateRegressionBaseline actual)
        {
            var failures = new List<string>();

            CompareScalar(expected.StateLabel, actual.StateLabel, "StateLabel", failures);
            CompareScalar(expected.DirectSelectionCount, actual.DirectSelectionCount, "DirectSelectionCount", failures);
            CompareScalar(expected.ActiveFeatureCount, actual.ActiveFeatureCount, "ActiveFeatureCount", failures);
            CompareScalar(expected.ActiveGrantCount, actual.ActiveGrantCount, "ActiveGrantCount", failures);
            CompareScalar(expected.AvailableSelectCount, actual.AvailableSelectCount, "AvailableSelectCount", failures);
            CompareOptionalStringList(expected.SpellSelectOptionCounts, actual.SpellSelectOptionCounts, "SpellSelectOptionCounts", failures);
            CompareOptionalStringList(expected.SpellSelectOptionKeys, actual.SpellSelectOptionKeys, "SpellSelectOptionKeys", failures);
            CompareScalar(expected.PendingChoiceCount, actual.PendingChoiceCount, "PendingChoiceCount", failures);
            CompareScalar(expected.BlockingPendingChoiceCount, actual.BlockingPendingChoiceCount, "BlockingPendingChoiceCount", failures);
            CompareScalar(expected.WarningCount, actual.WarningCount, "WarningCount", failures);
            CompareStringList(expected.DirectSelections, actual.DirectSelections, "DirectSelections", failures);
            CompareStringList(expected.ActiveFeatures, actual.ActiveFeatures, "ActiveFeatures", failures);
            CompareCountSet("Warning kind", expected.WarningKindCounts, actual.WarningKindCounts, failures);
            CompareAbilityScores(expected.AbilityScores, actual.AbilityScores, failures);
            CompareStringList(expected.ProficiencyKeys, actual.ProficiencyKeys, "ProficiencyKeys", failures);
            CompareStringList(expected.LanguageKeys, actual.LanguageKeys, "LanguageKeys", failures);
            CompareStringList(expected.GrantedSpellKeys, actual.GrantedSpellKeys, "GrantedSpellKeys", failures);
            CompareStringList(expected.SpellcastingProfileKeys, actual.SpellcastingProfileKeys, "SpellcastingProfileKeys", failures);
            CompareStringList(expected.FeatKeys, actual.FeatKeys, "FeatKeys", failures);
            CompareStringList(expected.FeatureKeys, actual.FeatureKeys, "FeatureKeys", failures);
            CompareStringList(expected.ChoiceSelectionKeys, actual.ChoiceSelectionKeys, "ChoiceSelectionKeys", failures);
            CompareStringList(expected.TraitKeys, actual.TraitKeys, "TraitKeys", failures);
            CompareStringList(expected.EffectRowKeys, actual.EffectRowKeys, "EffectRowKeys", failures);
            CompareStringList(expected.PendingChoiceKeys, actual.PendingChoiceKeys, "PendingChoiceKeys", failures);
            CompareCountSet("Provenance kind", expected.ProvenanceKindCounts, actual.ProvenanceKindCounts, failures);
            CompareStringList(expected.AppliedChoiceStates, actual.AppliedChoiceStates, "AppliedChoiceStates", failures);

            return failures;
        }

        private static List<string> CompareWpfParityRegressionBaseline(
            WpfParityRegressionBaseline expected,
            WpfParityRegressionBaseline actual)
        {
            var failures = new List<string>();

            CompareScalar(expected.CorpusLabel, actual.CorpusLabel, "CorpusLabel", failures);
            CompareScalar(expected.SchemaVersion, actual.SchemaVersion, "SchemaVersion", failures);
            CompareScalar(expected.DataVersion, actual.DataVersion, "DataVersion", failures);
            CompareScalar(expected.SourceFileCount, actual.SourceFileCount, "SourceFileCount", failures);
            CompareScalar(expected.MetadataElementCount, actual.MetadataElementCount, "MetadataElementCount", failures);
            CompareScalar(expected.TotalElementCount, actual.TotalElementCount, "TotalElementCount", failures);
            CompareCountSet("Element type", expected.ElementTypeCounts, actual.ElementTypeCounts, failures);
            CompareCountSet("Resolved package", expected.ResolvedPackageCounts, actual.ResolvedPackageCounts, failures);
            CompareCountSet("Feature kind", expected.FeatureKindCounts, actual.FeatureKindCounts, failures);
            CompareScalar(expected.SpellCount, actual.SpellCount, "SpellCount", failures);
            CompareScalar(expected.SpellAccessCount, actual.SpellAccessCount, "SpellAccessCount", failures);
            CompareScalar(expected.ClassCount, actual.ClassCount, "ClassCount", failures);
            CompareScalar(expected.ArchetypeCount, actual.ArchetypeCount, "ArchetypeCount", failures);
            CompareScalar(expected.MulticlassCount, actual.MulticlassCount, "MulticlassCount", failures);
            CompareScalar(expected.SubraceCount, actual.SubraceCount, "SubraceCount", failures);
            CompareScalar(expected.RaceVariantCount, actual.RaceVariantCount, "RaceVariantCount", failures);
            CompareScalar(expected.CompanionCount, actual.CompanionCount, "CompanionCount", failures);
            CompareCountSet("Companion CR/type", expected.CompanionCrTypeCounts, actual.CompanionCrTypeCounts, failures);
            CompareScalar(expected.SpellcastingProfileCount, actual.SpellcastingProfileCount, "SpellcastingProfileCount", failures);
            CompareScalar(expected.ExtendedSpellcastingProfileCount, actual.ExtendedSpellcastingProfileCount, "ExtendedSpellcastingProfileCount", failures);
            CompareScalar(expected.PreparedSpellcastingProfileCount, actual.PreparedSpellcastingProfileCount, "PreparedSpellcastingProfileCount", failures);
            CompareScalar(expected.AllowReplaceSpellcastingProfileCount, actual.AllowReplaceSpellcastingProfileCount, "AllowReplaceSpellcastingProfileCount", failures);
            CompareScalar(expected.SpellcastingProfilesWithExtendTextCount, actual.SpellcastingProfilesWithExtendTextCount, "SpellcastingProfilesWithExtendTextCount", failures);
            CompareScalar(expected.RuleScopeCount, actual.RuleScopeCount, "RuleScopeCount", failures);
            CompareScalar(expected.GrantCount, actual.GrantCount, "GrantCount", failures);
            CompareScalar(expected.SelectCount, actual.SelectCount, "SelectCount", failures);
            CompareScalar(expected.StatCount, actual.StatCount, "StatCount", failures);
            CompareScalar(expected.SetterScopeCount, actual.SetterScopeCount, "SetterScopeCount", failures);
            CompareScalar(expected.SetterEntryCount, actual.SetterEntryCount, "SetterEntryCount", failures);
            CompareScalar(expected.SetterAttributeCount, actual.SetterAttributeCount, "SetterAttributeCount", failures);
            CompareScalar(expected.RawGrantXmlCount, actual.RawGrantXmlCount, "RawGrantXmlCount", failures);
            CompareScalar(expected.RawSelectXmlCount, actual.RawSelectXmlCount, "RawSelectXmlCount", failures);
            CompareScalar(expected.RawStatXmlCount, actual.RawStatXmlCount, "RawStatXmlCount", failures);
            CompareStringList(expected.CuratedArchetypeLinks, actual.CuratedArchetypeLinks, "CuratedArchetypeLinks", failures);
            CompareStringList(expected.CuratedMulticlassRows, actual.CuratedMulticlassRows, "CuratedMulticlassRows", failures);
            CompareStringList(expected.CuratedSpellcastingProfiles, actual.CuratedSpellcastingProfiles, "CuratedSpellcastingProfiles", failures);
            CompareStringList(expected.CuratedCompanionRows, actual.CuratedCompanionRows, "CuratedCompanionRows", failures);
            CompareStringList(expected.CuratedSpellAccessRows, actual.CuratedSpellAccessRows, "CuratedSpellAccessRows", failures);

            return failures;
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

        private static SqliteConnection OpenSqliteConnection(string sqlitePath)
        {
            if (string.IsNullOrWhiteSpace(sqlitePath))
                throw new ArgumentException("SQLite path is required.", nameof(sqlitePath));
            if (!File.Exists(sqlitePath))
                throw new FileNotFoundException($"SQLite database was not found: {sqlitePath}");

            var connection = new SqliteConnection($"Data Source={sqlitePath}");
            connection.Open();
            return connection;
        }

        private static SqliteCommand CreateSqliteCommand(
            SqliteConnection connection,
            string sql,
            params (string Name, object Value)[] parameters)
        {
            var command = connection.CreateCommand();
            command.CommandText = sql;

            if (parameters != null)
            {
                foreach ((string name, object value) in parameters)
                {
                    command.Parameters.AddWithValue(name, value ?? DBNull.Value);
                }
            }

            return command;
        }

        private static long QueryInt64(
            SqliteConnection connection,
            string sql,
            params (string Name, object Value)[] parameters)
        {
            using SqliteCommand command = CreateSqliteCommand(connection, sql, parameters);
            object value = command.ExecuteScalar();
            return value == null || value == DBNull.Value
                ? 0L
                : Convert.ToInt64(value);
        }

        private static DiagnosticsRegressionCount[] QueryCountList(
            SqliteConnection connection,
            string sql,
            params (string Name, object Value)[] parameters)
        {
            var counts = new List<DiagnosticsRegressionCount>();

            using SqliteCommand command = CreateSqliteCommand(connection, sql, parameters);
            using SqliteDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                counts.Add(new DiagnosticsRegressionCount(
                    reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    reader.IsDBNull(1) ? 0L : reader.GetInt64(1)));
            }

            return counts
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .ToArray();
        }

        private static string[] QueryStringList(
            SqliteConnection connection,
            string sql,
            params (string Name, object Value)[] parameters)
        {
            var values = new List<string>();

            using SqliteCommand command = CreateSqliteCommand(connection, sql, parameters);
            using SqliteDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                values.Add(reader.IsDBNull(0) ? string.Empty : reader.GetString(0));
            }

            return values
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static DatabaseMetadataRow ReadDatabaseMetadata(SqliteConnection connection)
        {
            using SqliteCommand command = CreateSqliteCommand(
                connection,
                @"SELECT
                      schema_version,
                      data_version,
                      source_file_count,
                      element_count
                  FROM database_metadata
                  WHERE singleton_id = 1;");
            using SqliteDataReader reader = command.ExecuteReader();

            if (!reader.Read())
                return new DatabaseMetadataRow(0, 0, 0, 0);

            return new DatabaseMetadataRow(
                SchemaVersion: reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                DataVersion: reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                SourceFileCount: reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                ElementCount: reader.IsDBNull(3) ? 0 : reader.GetInt32(3));
        }

        private static string[] BuildCuratedArchetypeLinks(SqliteConnection connection)
        {
            return QueryStringList(
                connection,
                @"SELECT
                      e.aurora_id || '|' ||
                      e.name || '|parent=' ||
                      COALESCE(parent.aurora_id, '') || '|' ||
                      COALESCE(parent.name, '') || '|support=' ||
                      COALESCE(a.parent_support_text, '')
                  FROM archetypes AS a
                  JOIN elements AS e
                    ON e.element_id = a.element_id
                  LEFT JOIN elements AS parent
                    ON parent.element_id = a.parent_class_element_id
                  WHERE e.aurora_id IN
                  (
                      'ID_WOTC_PHB_ARCHETYPE_LIFEDOMAIN',
                      'ID_WOTC_PHB_ARCHETYPE_OTHERWORLDLY_PATRON_FIEND'
                  );");
        }

        private static string[] BuildCuratedMulticlassRows(SqliteConnection connection)
        {
            return QueryStringList(
                connection,
                @"SELECT
                      e.aurora_id || '|' ||
                      e.name || '|multiclass=' ||
                      COALESCE(cm.multiclass_aurora_id, '') || '|prerequisite=' ||
                      COALESCE(cm.prerequisite_text, '') || '|requirements=' ||
                      COALESCE(cm.requirements_text, '') || '|proficiencies=' ||
                      COALESCE(cm.proficiencies_text, '')
                  FROM class_multiclass AS cm
                  JOIN elements AS e
                    ON e.element_id = cm.class_element_id
                  WHERE e.aurora_id IN
                  (
                      'ID_WOTC_PHB_CLASS_CLERIC',
                      'ID_WOTC_PHB_CLASS_WARLOCK',
                      'ID_WOTC_PHB_CLASS_WIZARD'
                  );");
        }

        private static string[] BuildCuratedSpellcastingProfiles(SqliteConnection connection)
        {
            var results = new List<string>();

            using SqliteCommand command = CreateSqliteCommand(
                connection,
                @"SELECT
                      e.aurora_id,
                      e.name,
                      sp.owner_kind,
                      sp.profile_name,
                      sp.ability_name,
                      sp.is_extended,
                      sp.prepare_spells,
                      sp.allow_replace,
                      sp.list_text,
                      sp.extend_text
                  FROM spellcasting_profiles AS sp
                  JOIN elements AS e
                    ON e.element_id = sp.owner_element_id
                  WHERE e.aurora_id IN
                  (
                      'ID_WOTC_PHB_CLASS_CLERIC',
                      'ID_WOTC_PHB_CLASS_WARLOCK',
                      'ID_WOTC_PHB_ARCHETYPE_OTHERWORLDLY_PATRON_FIEND'
                  );");
            using SqliteDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                string ownerAuroraId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                string ownerName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                string ownerKind = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                string profileName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
                string abilityName = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
                bool isExtended = !reader.IsDBNull(5) && reader.GetInt64(5) != 0;
                bool? prepareSpells = reader.IsDBNull(6) ? (bool?)null : reader.GetInt64(6) != 0;
                bool? allowReplace = reader.IsDBNull(7) ? (bool?)null : reader.GetInt64(7) != 0;
                string listText = reader.IsDBNull(8) ? null : reader.GetString(8);
                string extendText = reader.IsDBNull(9) ? null : reader.GetString(9);

                results.Add(
                    ownerAuroraId + "|" +
                    ownerName + "|kind=" + ownerKind +
                    "|profile=" + profileName +
                    "|ability=" + abilityName +
                    "|extended=" + isExtended +
                    "|prepare=" + (prepareSpells?.ToString() ?? string.Empty) +
                    "|replace=" + (allowReplace?.ToString() ?? string.Empty) +
                    "|listCount=" + CountTextCollectionItems(listText) +
                    "|extendCount=" + CountTextCollectionItems(extendText) +
                    "|list=" + NormalizeTextCollection(listText) +
                    "|extend=" + NormalizeTextCollection(extendText));
            }

            return results
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string[] BuildCuratedCompanionRows(SqliteConnection connection)
        {
            return QueryStringList(
                connection,
                @"SELECT
                      COALESCE(cp.package_key, '') || '|' ||
                      e.aurora_id || '|' ||
                      e.name || '|type=' ||
                      COALESCE(c.creature_type, '') || '|size=' ||
                      COALESCE(c.size_text, '') || '|cr=' ||
                      COALESCE(c.challenge_text, '') || '|speed=' ||
                      COALESCE(c.speed_text, '')
                  FROM companions AS c
                  JOIN elements AS e
                    ON e.element_id = c.element_id
                  LEFT JOIN source_files AS sf
                    ON sf.source_file_id = e.source_file_id
                  LEFT JOIN content_packages AS cp
                    ON cp.content_package_id = sf.content_package_id
                  WHERE e.name IN ('Badger', 'Giant Badger');");
        }

        private static string[] BuildCuratedSpellAccessRows(SqliteConnection connection)
        {
            var accessBySpell = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            using SqliteCommand command = CreateSqliteCommand(
                connection,
                @"SELECT
                      e.aurora_id,
                      e.name,
                      sa.access_text
                  FROM spell_access AS sa
                  JOIN elements AS e
                    ON e.element_id = sa.spell_element_id
                  WHERE e.aurora_id IN
                  (
                      'ID_PHB_SPELL_AID',
                      'ID_PHB_SPELL_BURNING_HANDS',
                      'ID_PHB_SPELL_ELDRITCH_BLAST',
                      'ID_PHB_SPELL_FIND_FAMILIAR'
                  );");
            using SqliteDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                string auroraId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                string spellName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                string accessText = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                string key = auroraId + "|" + spellName;

                if (!accessBySpell.TryGetValue(key, out List<string> accessList))
                {
                    accessList = new List<string>();
                    accessBySpell[key] = accessList;
                }

                accessList.Add(accessText);
            }

            return accessBySpell
                .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.Key + "|access=" + string.Join(
                    ",",
                    x.Value
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)))
                .ToArray();
        }

        private static int CountTextCollectionItems(string rawText)
        {
            AuroraTextCollection collection = ParseAuroraTextCollection(rawText);
            return collection?.Count ?? 0;
        }

        private static string NormalizeTextCollection(string rawText)
        {
            AuroraTextCollection collection = ParseAuroraTextCollection(rawText);
            if (collection == null || collection.Count == 0)
                return string.Empty;

            return string.Join(
                ",",
                collection
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
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

        private static DiagnosticsRegressionCount[] BuildDiagnosticsCountList<T>(IEnumerable<IGrouping<string, T>> groups)
        {
            return groups
                .Select(group => new DiagnosticsRegressionCount(group.Key, group.LongCount()))
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .ToArray();
        }

        private static void CompareScalar<T>(T expected, T actual, string label, List<string> failures)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                failures.Add($"{label} changed: expected '{expected}', got '{actual}'.");
        }

        private static void CompareStringList(
            IReadOnlyList<string> expected,
            IReadOnlyList<string> actual,
            string label,
            List<string> failures)
        {
            string[] expectedValues = expected?.ToArray() ?? Array.Empty<string>();
            string[] actualValues = actual?.ToArray() ?? Array.Empty<string>();

            if (expectedValues.SequenceEqual(actualValues, StringComparer.OrdinalIgnoreCase))
                return;

            failures.Add($"{label} changed: expected {expectedValues.Length} item(s), got {actualValues.Length}.");

            foreach (string missing in expectedValues.Except(actualValues, StringComparer.OrdinalIgnoreCase).Take(5))
                failures.Add($"{label} missing: {missing}");
            foreach (string added in actualValues.Except(expectedValues, StringComparer.OrdinalIgnoreCase).Take(5))
                failures.Add($"{label} added: {added}");
        }

        private static void CompareOptionalStringList(
            IReadOnlyList<string> expected,
            IReadOnlyList<string> actual,
            string label,
            List<string> failures)
        {
            if (expected == null)
                return;

            CompareStringList(expected, actual, label, failures);
        }

        private static void CompareAbilityScores(
            IReadOnlyList<CharacterStateAbilityScoreBaseline> expected,
            IReadOnlyList<CharacterStateAbilityScoreBaseline> actual,
            List<string> failures)
        {
            Dictionary<string, decimal> expectedMap = (expected ?? Array.Empty<CharacterStateAbilityScoreBaseline>())
                .ToDictionary(x => x.AbilityKey, x => x.FinalValue, StringComparer.OrdinalIgnoreCase);
            Dictionary<string, decimal> actualMap = (actual ?? Array.Empty<CharacterStateAbilityScoreBaseline>())
                .ToDictionary(x => x.AbilityKey, x => x.FinalValue, StringComparer.OrdinalIgnoreCase);

            foreach (string abilityKey in expectedMap.Keys.Union(actualMap.Keys, StringComparer.OrdinalIgnoreCase)
                         .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                bool hasExpected = expectedMap.TryGetValue(abilityKey, out decimal expectedValue);
                bool hasActual = actualMap.TryGetValue(abilityKey, out decimal actualValue);

                if (!hasExpected)
                {
                    failures.Add($"AbilityScores added: {abilityKey} => {actualValue}");
                    continue;
                }

                if (!hasActual)
                {
                    failures.Add($"AbilityScores missing: {abilityKey}");
                    continue;
                }

                if (expectedValue != actualValue)
                    failures.Add($"AbilityScores changed: {abilityKey} expected {expectedValue}, got {actualValue}.");
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

        private sealed record CharacterStateAbilityScoreBaseline(string AbilityKey, decimal FinalValue);

        private sealed record CharacterStateRegressionBaseline(
            DateTime CapturedAtUtc,
            string StateLabel,
            int DirectSelectionCount,
            IReadOnlyList<string> DirectSelections,
            int ActiveFeatureCount,
            IReadOnlyList<string> ActiveFeatures,
            int ActiveGrantCount,
            int AvailableSelectCount,
            int PendingChoiceCount,
            int BlockingPendingChoiceCount,
            int WarningCount,
            IReadOnlyList<DiagnosticsRegressionCount> WarningKindCounts,
            IReadOnlyList<CharacterStateAbilityScoreBaseline> AbilityScores,
            IReadOnlyList<string> ProficiencyKeys,
            IReadOnlyList<string> LanguageKeys,
            IReadOnlyList<string> GrantedSpellKeys,
            IReadOnlyList<string> SpellcastingProfileKeys,
            IReadOnlyList<string> FeatKeys,
            IReadOnlyList<string> FeatureKeys,
            IReadOnlyList<string> ChoiceSelectionKeys,
            IReadOnlyList<string> TraitKeys,
            IReadOnlyList<string> EffectRowKeys,
            IReadOnlyList<string> PendingChoiceKeys,
            IReadOnlyList<DiagnosticsRegressionCount> ProvenanceKindCounts,
            IReadOnlyList<string> AppliedChoiceStates,
            IReadOnlyList<string> SpellSelectOptionCounts = null,
            IReadOnlyList<string> SpellSelectOptionKeys = null);

        private sealed record WpfParityRegressionBaseline(
            DateTime CapturedAtUtc,
            string CorpusLabel,
            int SchemaVersion,
            int DataVersion,
            int SourceFileCount,
            int MetadataElementCount,
            long TotalElementCount,
            IReadOnlyList<DiagnosticsRegressionCount> ElementTypeCounts,
            IReadOnlyList<DiagnosticsRegressionCount> ResolvedPackageCounts,
            IReadOnlyList<DiagnosticsRegressionCount> FeatureKindCounts,
            long SpellCount,
            long SpellAccessCount,
            long ClassCount,
            long ArchetypeCount,
            long MulticlassCount,
            long SubraceCount,
            long RaceVariantCount,
            long CompanionCount,
            IReadOnlyList<DiagnosticsRegressionCount> CompanionCrTypeCounts,
            long SpellcastingProfileCount,
            long ExtendedSpellcastingProfileCount,
            long PreparedSpellcastingProfileCount,
            long AllowReplaceSpellcastingProfileCount,
            long SpellcastingProfilesWithExtendTextCount,
            long RuleScopeCount,
            long GrantCount,
            long SelectCount,
            long StatCount,
            long SetterScopeCount,
            long SetterEntryCount,
            long SetterAttributeCount,
            long RawGrantXmlCount,
            long RawSelectXmlCount,
            long RawStatXmlCount,
            IReadOnlyList<string> CuratedArchetypeLinks,
            IReadOnlyList<string> CuratedMulticlassRows,
            IReadOnlyList<string> CuratedSpellcastingProfiles,
            IReadOnlyList<string> CuratedCompanionRows,
            IReadOnlyList<string> CuratedSpellAccessRows);

        private sealed record DiagnosticsRegressionBaseline(
            DateTime CapturedAtUtc,
            string CorpusLabel,
            long TotalUnresolvedCount,
            long ActionableUnresolvedCount,
            IReadOnlyList<DiagnosticsRegressionCount> DeferredCounts,
            IReadOnlyList<DiagnosticsRegressionCount> UnresolvedKindCounts,
            int TotalSourceIntegrityCount,
            IReadOnlyList<DiagnosticsRegressionCount> SourceIntegrityKindCounts);

        private sealed record DatabaseMetadataRow(
            int SchemaVersion,
            int DataVersion,
            int SourceFileCount,
            int ElementCount);

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
