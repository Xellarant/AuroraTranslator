using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using AuroraTranslator.Application;
using Microsoft.Extensions.DependencyInjection;

namespace AuroraTranslator.Desktop;

public partial class App : System.Windows.Application
{
    private ServiceProvider? services;
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            string statePath = Environment.GetEnvironmentVariable("AURORA_TRANSLATOR_STATE")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AuroraTranslator");
            statePath = Path.GetFullPath(statePath);
            string identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(statePath.ToUpperInvariant())));
            instance = new Mutex(true, "Local\\AuroraTranslator-" + identity, out bool created);
            if (!created)
            {
                MessageBox.Show("Translator is already open for this workspace.", "Aurora Translator");
                instance.Dispose(); instance = null; Shutdown(); return;
            }
            var collection = new ServiceCollection();
            collection.AddWpfBlazorWebView();
            collection.AddSingleton(new WorkspaceStore(statePath));
            collection.AddSingleton<IContentOperations, AuroraTranslator.Application.ContentOperations>();
            collection.AddSingleton<WorkspaceSession>();
            collection.AddSingleton<DesktopDialogs>();
            services = collection.BuildServiceProvider();
            MainWindow = new MainWindow(services, statePath);
            MainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Translator could not start. Verify that the .NET 10 Desktop Runtime and Microsoft Edge WebView2 Runtime are installed.\n\n" + ex.Message,
                "Aurora Translator", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e)
    { services?.Dispose(); if (instance != null) { instance.ReleaseMutex(); instance.Dispose(); } base.OnExit(e); }
}
