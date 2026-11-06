using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace AuroraTranslator.Desktop;

public sealed class DesktopDialogs
{
    public string? ContentFolder() => System.Windows.Application.Current.Dispatcher.Invoke(() =>
    {
        var picker = new OpenFolderDialog { Title = "Select Aurora content folder" };
        return picker.ShowDialog(System.Windows.Application.Current.MainWindow) == true ? picker.FolderName : null;
    });
    public string? Database(bool existing) => System.Windows.Application.Current.Dispatcher.Invoke(() =>
    {
        const string filter = "SQLite database (*.sqlite;*.sqlite3;*.db)|*.sqlite;*.sqlite3;*.db";
        if (existing)
        {
            var picker = new OpenFileDialog { Title = "Open content database", Filter = filter };
            return picker.ShowDialog(System.Windows.Application.Current.MainWindow) == true ? picker.FileName : null;
        }
        var save = new SaveFileDialog { Title = "Choose database to create or refresh", Filter = filter,
            DefaultExt = ".sqlite", FileName = "aurora-content.sqlite", OverwritePrompt = false };
        return save.ShowDialog(System.Windows.Application.Current.MainWindow) == true ? save.FileName : null;
    });
    public bool Export(string json, string suggestedName) => System.Windows.Application.Current.Dispatcher.Invoke(() =>
    {
        var picker = new SaveFileDialog { Title = "Export run report", Filter = "JSON report (*.json)|*.json",
            DefaultExt = ".json", FileName = suggestedName };
        if (picker.ShowDialog(System.Windows.Application.Current.MainWindow) != true) return false;
        File.WriteAllText(picker.FileName, json);
        return true;
    });
}
