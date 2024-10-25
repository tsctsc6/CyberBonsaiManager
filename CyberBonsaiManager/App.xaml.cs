﻿using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Windows;
using CyberBonsaiManager.Models;
using CyberBonsaiManager.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace CyberBonsaiManager;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public new static App Current => (App)Application.Current;
    public IServiceProvider Services { get; }

    public App()
    {
        Services = new ServiceCollection()
            .AddSingleton<MainWindow>(sp => new MainWindow{DataContext = sp.GetRequiredService<MainWindowViewModel>()})
            .AddSingleton<MainWindowViewModel>()
            .AddSingleton<JsonSerializerOptions>(_ => new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                WriteIndented = true,
                Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
            })
            .AddSingleton<AppConfig>(sp => JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(@".\config.json"),
                sp.GetRequiredService<JsonSerializerOptions>()) ?? throw new InvalidOperationException("Failed to load config"))
            .BuildServiceProvider();
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Async(a => a.Console())
            .WriteTo.Async(a => a.File(Path.Combine(".\\logs", ".log"), rollingInterval: RollingInterval.Day))
            .WriteTo.Sink(new ObservableCollectionSink(Current.Services.GetRequiredService<MainWindowViewModel>().ScriptOutput,
                Current.Services.GetRequiredService<MainWindow>().ScrollViewer1_ScrollToBottom))
            .CreateLogger();
    }

    private void App_OnStartup(object sender, StartupEventArgs e)
    {
        Services.GetRequiredService<MainWindow>().Show();
        Services.GetRequiredService<MainWindowViewModel>().RunCommand.Execute(null);
    }

    private void App_OnExit(object sender, ExitEventArgs e)
    {
        Services.GetRequiredService<MainWindowViewModel>().Dispose();
        Log.CloseAndFlush();
    }
}