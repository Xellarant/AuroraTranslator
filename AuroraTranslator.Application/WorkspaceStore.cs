using System.Text.Json;
using System.Text.Json.Serialization;

namespace AuroraTranslator.Application;

/// <summary>Atomic, local-only settings/history. An unreadable file is never silently overwritten.</summary>
public sealed class WorkspaceStore(string directory)
{
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    public string FilePath => Path.Combine(DirectoryPath, "workspace.json");
    public string? Warning { get; private set; }
    private bool writable = true;

    public WorkspaceDocument Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return Empty();
            var document = JsonSerializer.Deserialize<WorkspaceDocument>(File.ReadAllText(FilePath), JsonOptions);
            if (document is not { Version: 1, Selection: not null, Presets: not null, Runs: not null }
                || !ValidParameters(document.Selection)
                || document.Presets.Any(p => p is null || !ValidParameters(p.Parameters) || string.IsNullOrWhiteSpace(p.Name))
                || document.Runs.Any(r => r is null || !ValidParameters(r.Parameters)
                    || r.Result is { } result && (result.Issues is null || result.Issues.Any(i =>
                        i is null || i.Area is null || i.Kind is null || i.Message is null))))
                throw new InvalidDataException("Unsupported or incomplete workspace settings.");
            return document with { Runs = document.Runs.Take(30).Select(r =>
                r.Status is RunStatus.Running or RunStatus.Cancelling
                    ? r with { Status = RunStatus.Interrupted, Error = "The app closed before a final result was recorded. Inspect the database before retrying." }
                    : r).ToArray() };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or InvalidDataException)
        {
            writable = false;
            Warning = "Settings could not be loaded; this session will not overwrite them. " + ex.Message;
            return Empty();
        }
    }

    public void Save(WorkspaceDocument document)
    {
        if (!writable) return;
        string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(temporary, JsonSerializer.Serialize(document, JsonOptions));
            File.Move(temporary, FilePath, overwrite: true);
            Warning = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Warning = "The current session works, but settings/history could not be saved. " + ex.Message; }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    public static string Export(RunRecord run) => JsonSerializer.Serialize(run, JsonOptions);
    private static bool ValidParameters(RunParameters? parameters) =>
        parameters is { ContentRoot: not null, DatabasePath: not null };
    private static WorkspaceDocument Empty() => new(1, new(), [], []);
}
