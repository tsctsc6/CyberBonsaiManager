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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualBasic;
using Serilog;
using Tomlyn;

namespace CyberBonsaiManager;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private static readonly HttpClient client = new();
    private Process? emulatorProcess;
    private Process? scriptProcess;
    public ObservableCollection<StringWithColor> ScriptOutput { get; } = [];

    [ObservableProperty]
    private bool autoScrollToBottom = true;
    
    [RelayCommand]
    private async Task RunAsync()
    {
        try
        {
            var appTasks = Toml.ToModel<AppTasks>(await File.ReadAllTextAsync(App.Current.Services.GetRequiredService<AppConfig>().Tasks.ConfigPath));
            var appTaskPures = appTasks.Tasks.Select(AppTaskBase.Parse);
            foreach (var task in appTaskPures)
            {
                switch (task.Type)
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

    private void CloseAppHandler(AppTaskBase task)
    {
        App.Current.Shutdown();
    }

    private static async Task SleepHandlerAsync(AppTaskBase task)
    {
        if (int.TryParse(task.Args, out int t))
            await Task.Delay(t);
    }

    private async Task TestConnectionStatusHandlerAsync(AppTaskBase task)
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

    private async Task ChangeResolutionHandlerAsync(AppTaskBase task)
    {
        switch (App.Current.Services.GetRequiredService<AppConfig>().Emulator.Type)
        {
            case "BlueStack": await ChangeBlueStackResolutionHandlerAsync(task); break;
        }
    }

    private async Task ChangeBlueStackResolutionHandlerAsync(AppTaskBase task)
    {
        if (task.Args is null) return;
        emulatorProcess?.Kill();
        emulatorProcess = null;
        var r = task.Args.Split('x');
        if (r.Length != 2) return;
        int width = int.Parse(r[0]);
        int height = int.Parse(r[1]);
        Log.Information("修改分辨率为 {w}x{h}", width, height);
        var BSconf = await File.ReadAllLinesAsync(App.Current.Services.GetRequiredService<AppConfig>().Emulator.ConfigPath);
        BSconf = BSconf.Select(s =>
        {
            if (s.StartsWith("bst.instance.Nougat64.fb_height"))
            {
                return ValueRegex().Replace(s, m => $"\"{height}\"");
            }
            else if (s.StartsWith("bst.instance.Nougat64.fb_width"))
            {
                return ValueRegex().Replace(s, m => $"\"{width}\"");
            }
            else
            {
                return s;
            }
        }).ToArray();
        await File.WriteAllLinesAsync(App.Current.Services.GetRequiredService<AppConfig>().Emulator.ConfigPath, BSconf);
    }

    private async Task EmulatorStartupHandlerAsync(AppTaskBase task)
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

    private async Task ScriptHandlerAsync(AppTaskBase task)
    {
        if (task.FilePath is null) return;
        var encoding = Encoding.Default;
        if (!string.IsNullOrEmpty(task.Encoding))
        {
            try
            {
                encoding = Encoding.GetEncoding(task.Encoding);
            }
            catch (ArgumentException ex)
            {
                Log.Error(ex.Message);
                return;
            }
        }
        scriptProcess = new Process()
        {
            StartInfo = new()
            {
                FileName = Path.GetFullPath(task.FilePath),
                Arguments = task.Args,
                WorkingDirectory = Path.GetFullPath(task.WorkPath ?? ".\\"),
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = encoding,
                StandardErrorEncoding = encoding
            },
        };
        scriptProcess.Start();
        await Task.WhenAny(scriptProcess.WaitForExitAsync(),
            ListenProcessStandardOutputAsync(scriptProcess),
            ListenProcessStandardErrorAsync(scriptProcess));
    }

    [GeneratedRegex("\"(.*?)\"")]
    private static partial Regex ValueRegex();

    private async Task ListenProcessStandardOutputAsync(Process process)
    {
        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync();
            if (!string.IsNullOrWhiteSpace(line)) Log.Information(line);
        }
    }
    
    private async Task ListenProcessStandardErrorAsync(Process process)
    {
        while (true)
        {
            var line = await process.StandardError.ReadLineAsync();
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