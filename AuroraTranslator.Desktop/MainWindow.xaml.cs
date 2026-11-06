using System.ComponentModel;
using System.IO;
using System.Windows;
using AuroraTranslator.Application;
using Microsoft.AspNetCore.Components.WebView.Wpf;
using Microsoft.Extensions.DependencyInjection;

namespace AuroraTranslator.Desktop;

public partial class MainWindow : Window
{
    private readonly WorkspaceSession session;
    public MainWindow(IServiceProvider services, string statePath)
    {
        InitializeComponent();
        session = services.GetRequiredService<WorkspaceSession>();
        WebView.Services = services;
        WebView.BlazorWebViewInitializing += (_, args) => args.UserDataFolder = Path.Combine(statePath, "WebView2");
        WebView.RootComponents.Add(new RootComponent { Selector = "#app", ComponentType = typeof(Components.Workspace) });
        Closing += OnClosing;
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!session.IsBusy) return;
        e.Cancel = true;
        if (MessageBox.Show("A run is still active. Cancel it? Keep this window open until it finishes safely.",
            "Run in progress", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            session.Cancel();
    }
}
