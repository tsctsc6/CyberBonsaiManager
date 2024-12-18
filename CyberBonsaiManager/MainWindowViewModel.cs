using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CyberBonsaiManager.GameUpdater;
using CyberBonsaiManager.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Task = System.Threading.Tasks.Task;

namespace CyberBonsaiManager;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly HttpClient client;
    private Process? emulatorProcess;
    private Process? scriptProcess;
    private CancellationTokenSource cts = new();
    public ObservableCollection<StringWithColor> ScriptOutput { get; } = [];

    [ObservableProperty]
    private bool autoScrollToBottom = true;

    public string[] CliArgs { get; set; } = [];

    public MainWindowViewModel(HttpClient client)
    {
        this.client = client;
    }
    
    [RelayCommand]
    private async Task RunAsync()
    {
        string? targetPath;
        if (CliArgs.Length == 0)
        {
            VariantsAnalyzer analyzer = new();
            var targetTaskSection = analyzer.Analyze(App.Current.Services.GetRequiredService<IConfigurationRoot>()
                .GetRequiredSection("target_task"));
            targetPath = targetTaskSection["path"];
        }
        else
        {
            targetPath = CliArgs[0];
        }
        Log.Information("打开配置文件: {c}", targetPath);
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile(targetPath!,
                    optional: false, reloadOnChange: false)
                .Build();
            var appTasks = configuration.GetRequiredSection("tasks").GetChildren();
            foreach (var task in appTasks)
            {
                Log.Information("开始任务: {t}", task["name"]);
                switch (task["type"])
                {
                    case "TestConnectionStatus": await TestConnectionStatusHandlerAsync(task); break;
                    case "ChangeResolution": await ChangeResolutionHandlerAsync(task); break;
                    case "EmulatorStartup": await EmulatorStartupHandlerAsync(task); break;
                    case "Sleep": await SleepHandlerAsync(task); break;
                    case "Script": await ScriptHandlerAsync(task); break;
                    case "ArknightsUpdate": await ArknightsUpdateHandlerAsync(); break;
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

    private async Task ArknightsUpdateHandlerAsync()
    {
        await App.Current.Services.GetRequiredService<ArknightsUpdater>().UpdateAsync();
    }

    private void CloseAppHandler(IConfigurationSection task)
    {
        if (int.TryParse(task["exit_code"], out var exitCode))
            App.Current.Shutdown(exitCode);
        else App.Current.Shutdown();
    }

    private static async Task SleepHandlerAsync(IConfigurationSection task)
    {
        await Task.Delay(task.GetValue<int>("time_in_ms"));
    }

    private async Task TestConnectionStatusHandlerAsync(IConfigurationSection task)
    {
        var uris = task.GetSection("uris").GetChildren();
        foreach (var item in uris)
        {
            bool canConnectUri = false;
            for (int i = 0; i < item.GetValue<int>("retry_count"); i++)
            {
                Log.Information($"正在检测网络连接: {item["uri"]}, {i + 1}");
                var resp = await client.GetAsync(item["uri"], HttpCompletionOption.ResponseHeadersRead);
                try
                {
                    resp.EnsureSuccessStatusCode();
                    canConnectUri = true;
                    break;
                }
                catch (HttpRequestException)
                {
                    await Task.Delay(item.GetValue<int>("retry_interval"));
                }
            }
            if (!canConnectUri) throw new Exception("无网络连接!");
        }
    }

    private async Task ChangeResolutionHandlerAsync(IConfigurationSection task)
    {
        switch (App.Current.Services.GetRequiredService<IConfigurationRoot>()["emulator:type"])
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
        var conf = await File.ReadAllLinesAsync(App.Current.Services.GetRequiredService<IConfigurationRoot>()["emulator:config_path"]!);
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
        await File.WriteAllLinesAsync(App.Current.Services.GetRequiredService<IConfigurationRoot>()["emulator:config_path"]!, conf2);
    }

    private async Task EmulatorStartupHandlerAsync(IConfigurationSection task)
    {
        Log.Information("启动模拟器...");
        emulatorProcess ??= new()
        {
            StartInfo = new()
            {
                FileName = App.Current.Services.GetRequiredService<IConfigurationRoot>()["emulator:path"]!,
                Arguments = App.Current.Services.GetRequiredService<IConfigurationRoot>()["emulator:args"]!,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Maximized
            }
        };
        emulatorProcess.Start();
        await WindowsHelper.MinimzeProcessMainWindowsAsync(emulatorProcess);
        await Task.Delay(App.Current.Services.GetRequiredService<IConfigurationRoot>().GetValue<int>("emulator:wait_time_in_ms"));
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
            scriptProcess.ListenProcessStandardOutputAsync(cts.Token),
            scriptProcess.ListenProcessStandardErrorAsync(cts.Token));
        await cts.CancelAsync();
        cts.Dispose();
        cts = new();
    }

    [GeneratedRegex("\"(.*?)\"")]
    private static partial Regex ValueRegex();
    
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