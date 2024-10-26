using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CyberBonsaiManager.Models;
using CyberBonsaiManager.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Task = System.Threading.Tasks.Task;

namespace CyberBonsaiManager;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private static readonly HttpClient client = new();
    private IConfigurationRoot configuration;
    private Process? emulatorProcess;
    private Process? scriptProcess;
    private CancellationTokenSource cts = new();
    public ObservableCollection<StringWithColor> ScriptOutput { get; } = [];

    [ObservableProperty]
    private bool autoScrollToBottom = true;
    
    [RelayCommand]
    private async Task RunAsync()
    {
        try
        {
            configuration = new ConfigurationBuilder()
                .AddJsonFile(App.Current.Services.GetRequiredService<AppConfig>().Task.Path, optional: false,
                    reloadOnChange: false)
                .Build();
            var appTasks = configuration.GetSection("tasks").GetChildren();
            foreach (var task in appTasks)
            {
                switch (task["type"])
                {
                    case "TestConnectionStatus": await TestConnectionStatusHandlerAsync(task); break;
                    case "ChangeResolution": await ChangeResolutionHandlerAsync(task); break;
                    case "EmulatorStartup": await EmulatorStartupHandlerAsync(task); break;
                    case "Sleep": await SleepHandlerAsync(task); break;
                    case "Script": await ScriptHandlerAsync(task); break;
                    case "CloseApp": CloseAppHandler(task); break;
                }
            }
        }
        catch (Exception e)
        {
            Log.Fatal(e, "Error:");
            throw;
        }
    }

    private void CloseAppHandler(IConfigurationSection task)
    {
        if (int.TryParse(task["exit_code"], out var exitCode))
            App.Current.Shutdown(exitCode);
        else App.Current.Shutdown();
    }

    private static async Task SleepHandlerAsync(IConfigurationSection task)
    {
        if (int.TryParse(task["time_in_ms"], out var t))
            await Task.Delay(t);
    }

    private async Task TestConnectionStatusHandlerAsync(IConfigurationSection task)
    {
        Log.Information("正在检测网络连接...");
        var resp = await client.GetAsync("http://www.msftconnecttest.com/redirect");
        try
        {
            resp.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException)
        {
            throw new Exception("无网络连接!");
        }
    }

    private async Task ChangeResolutionHandlerAsync(IConfigurationSection task)
    {
        switch (App.Current.Services.GetRequiredService<AppConfig>().Emulator.Type)
        {
            case "BlueStack": await ChangeBlueStackResolutionHandlerAsync(task); break;
        }
    }

    private async Task ChangeBlueStackResolutionHandlerAsync(IConfigurationSection task)
    {
        var resolution = task["resolution"];
        if (resolution is null) return;
        emulatorProcess?.Kill();
        emulatorProcess = null;
        var r = resolution.Split('x');
        if (r.Length != 2) return;
        var width = int.Parse(r[0]);
        var height = int.Parse(r[1]);
        Log.Information("修改分辨率为 {w}x{h}", width, height);
        var conf = await File.ReadAllLinesAsync(App.Current.Services.GetRequiredService<AppConfig>().Emulator.ConfigPath);
        var conf2 = conf.Select(s =>
        {
            if (s.StartsWith("bst.instance.Nougat64.fb_height"))
            {
                return ValueRegex().Replace(s, m => $"\"{height}\"");
            }
            if (s.StartsWith("bst.instance.Nougat64.fb_width"))
            {
                return ValueRegex().Replace(s, m => $"\"{width}\"");
            }
            return s;
        });
        await File.WriteAllLinesAsync(App.Current.Services.GetRequiredService<AppConfig>().Emulator.ConfigPath, conf2);
    }

    private async Task EmulatorStartupHandlerAsync(IConfigurationSection task)
    {
        Log.Information("启动模拟器...");
        emulatorProcess ??= new()
        {
            StartInfo = new()
            {
                FileName = App.Current.Services.GetRequiredService<AppConfig>().Emulator.Path,
                Arguments = App.Current.Services.GetRequiredService<AppConfig>().Emulator.Args,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Maximized
            }
        };
        emulatorProcess.Start();
        await WindowsHelper.MinimzeProcessMainWindowsAsync(emulatorProcess);
    }

    private async Task ScriptHandlerAsync(IConfigurationSection task)
    {
        var filePath = task["file_path"];
        if (string.IsNullOrEmpty(filePath)) return;
        var encoding = Encoding.Default;
        var encodingString = task["encoding"];
        if (!string.IsNullOrEmpty(encodingString))
        {
            try
            {
                encoding = Encoding.GetEncoding(encodingString);
            }
            catch (ArgumentException ex)
            {
                Log.Error(ex.Message);
                return;
            }
        }
        scriptProcess = new Process
        {
            StartInfo = new()
            {
                FileName = Path.GetFullPath(filePath),
                Arguments = task["args"],
                WorkingDirectory = Path.GetFullPath(task["work_path"] ?? ".\\"),
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = encoding,
                StandardErrorEncoding = encoding
            },
        };
        scriptProcess.Start();
        await Task.WhenAny(scriptProcess.WaitForExitAsync(),
            ListenProcessStandardOutputAsync(scriptProcess, cts.Token),
            ListenProcessStandardErrorAsync(scriptProcess, cts.Token));
        await cts.CancelAsync();
        cts = new();
    }

    [GeneratedRegex("\"(.*?)\"")]
    private static partial Regex ValueRegex();

    private async Task ListenProcessStandardOutputAsync(Process process, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested) break;
            if (!string.IsNullOrWhiteSpace(line)) Log.Information(line);
        }
    }
    
    private async Task ListenProcessStandardErrorAsync(Process process, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var line = await process.StandardError.ReadLineAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested) break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.Length > 27)
            {
                if (line[..28].Contains("INF")) Log.Information(line);
                else if (line[..28].Contains("WAR")) Log.Warning(line);
                else Log.Error(line);
            }
            else Log.Error(line);
        }
    }
    
    private void ReleaseUnmanagedResources()
    {
        emulatorProcess?.Kill();
        scriptProcess?.Kill();
    }

    private void Dispose(bool disposing)
    {
        ReleaseUnmanagedResources();
        if (disposing)
        {
            emulatorProcess?.Dispose();
            scriptProcess?.Dispose();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~MainWindowViewModel()
    {
        Dispose(false);
    }
}