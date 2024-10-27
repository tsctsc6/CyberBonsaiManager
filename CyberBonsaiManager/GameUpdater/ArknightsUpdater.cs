using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using CyberBonsaiManager.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace CyberBonsaiManager.GameUpdater;

public class ArknightsUpdater
{
    private readonly HttpClient _client;
    private const string DownloadPath = "download";
    private const string GameVersionPath = "game_version.json";

    public int BufferSize { get; set; } = 8 * 1024;
    public double MaxMegaBytesPerSecond { get; set; } = 3;
    public double RefreshDelayInSeconds { get; set; } = 0.5;
    int bytesRead;
    int bytesRead2;

    private CancellationTokenSource cts = new();

    public ArknightsUpdater(HttpClient client)
    {
        _client = client;
        if (!Directory.Exists(DownloadPath)) Directory.CreateDirectory(DownloadPath);
    }

    public async Task UpdateAsync()
    {
        var result = await PollingAsync();
        if (result.IsNewVersion)
        {
            BufferSize = App.Current.Services.GetRequiredService<IConfigurationRoot>()
                .GetValue<int>("download:buffer_size");
            MaxMegaBytesPerSecond = App.Current.Services.GetRequiredService<IConfigurationRoot>()
                .GetValue<double>("download:max_megaBytes_per_second");
            RefreshDelayInSeconds = App.Current.Services.GetRequiredService<IConfigurationRoot>()
                .GetValue<double>("download:refresh_delay_in_seconds");
            var newFilePath = Path.Combine(DownloadPath, result.OriginalFileName);
            await DownloadAsync(result.FileUri, newFilePath);
            await InstallAsync(newFilePath);
            var jsonNode = JsonNode.Parse(await File.ReadAllTextAsync(GameVersionPath));
            var oldFilePath = Path.Combine(DownloadPath, jsonNode!["Arknights"]?.GetValue<string>());
            if (File.Exists(oldFilePath)) File.Delete(oldFilePath);
            jsonNode!["Arknights"] = result.OriginalFileName;
            await File.WriteAllTextAsync(GameVersionPath,
                jsonNode.ToJsonString(App.Current.Services.GetRequiredService<JsonSerializerOptions>()));
        }
    }

    private async Task<PollingResult> PollingAsync()
    {
        var resp = await _client.GetAsync("https://ak.hypergryph.com/downloads/android_lastest");
        if (resp.StatusCode != HttpStatusCode.Found)
        {
            Log.Error("无法找到最新的安装包, {resp}", resp);
            return new();
        }

        var fileUri = resp.Headers.Location;
        if (fileUri is null)
        {
            Log.Error("无法找到最新的安装包, {resp}", resp);
            return new();
        }

        var originalFileName = fileUri.Segments[^1];
        var jsonNode = JsonNode.Parse(await File.ReadAllTextAsync("game_version.json"));
        if (originalFileName == jsonNode?["Arknights"]?.GetValue<string>())
        {
            Log.Information("当前版本已经最新");
            return new();
        }
        else
        {
            Log.Information("发现更新的版本");
            return new()
            {
                IsNewVersion = true,
                FileUri = fileUri.OriginalString,
                OriginalFileName = originalFileName
            };
        }
    }

    public async Task DownloadAsync(string uri, string filePath)
    {
        var resp = await _client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        var totalBytes = resp.Content.Headers.ContentLength;

        using Stream contentStream = await resp.Content.ReadAsStreamAsync();
        using FileStream fileStream = new(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        byte[] buffer = new byte[BufferSize];
        long totalBytesRead = 0;

        double maxBytesPerSecond = MaxMegaBytesPerSecond * 1024 * 1024;
        var delayPerBufferInSeconds_d = BufferSize / maxBytesPerSecond;
        int refreshCount = (int)(RefreshDelayInSeconds / delayPerBufferInSeconds_d);
        var delayPerBufferInSeconds = TimeSpan.FromSeconds(delayPerBufferInSeconds_d * refreshCount);
        Stopwatch stopwatch = new();
        Log.Information("开始下载");
        while (true)
        {
            stopwatch.Restart();
            await Task.WhenAll(MoveAsync(contentStream, buffer, fileStream, refreshCount),
                Task.Delay(delayPerBufferInSeconds));
            stopwatch.Stop();
            totalBytesRead += bytesRead2;
            if (totalBytes.HasValue)
            {
                // 计算并显示进度百分比
                Console.Out.WriteAsync(
                    $"\r{(double)totalBytesRead / 1024 / 1024:F2} MB, {(double)totalBytesRead / totalBytes.Value * 100:F2}%, {bytesRead2 / stopwatch.Elapsed.TotalSeconds / 1024 / 1024:F2} MB/s ");
            }
            else
            {
                // 如果无法获取文件大小，只显示已下载字节数
                Console.Out.WriteAsync(
                    $"\r{(double)totalBytesRead / 1024 / 1024:F2} MB, {bytesRead2 / stopwatch.Elapsed.TotalSeconds / 1024 / 1024:F2} MB/s ");
            }

            if (bytesRead == 0) break;
        }

        Log.Information("下载完成");
    }

    async Task MoveAsync(Stream stream1, byte[] buffer, Stream stream2, int count)
    {
        bytesRead2 = 0;
        for (int i = 0; i < count; i++)
        {
            bytesRead = await stream1.ReadAsync(buffer, 0, buffer.Length);
            if (bytesRead == 0) return;
            await stream2.WriteAsync(buffer, 0, bytesRead);
            bytesRead2 += bytesRead;
        }
    }
    
    async Task InstallAsync(string filePath)
    {
        Log.Information("安装...");
        Process emulatorProcess = new()
        {
            StartInfo = new()
            {
                FileName = App.Current.Services.GetRequiredService<IConfigurationRoot>()["emulator:path"],
                Arguments = $"{App.Current.Services.GetRequiredService<IConfigurationRoot>()["emulator:args"]}"
            }
        };
        emulatorProcess.Start();
        await WindowsHelper.MinimzeProcessMainWindowsAsync(emulatorProcess);
        await Task.Delay(App.Current.Services.GetRequiredService<IConfigurationRoot>()
            .GetValue<int>("emulator:wait_time_in_ms"));

        try
        {
            Process adbProcess = new()
            {
                StartInfo = new()
                {
                    FileName = App.Current.Services.GetRequiredService<IConfigurationRoot>()["emulator:adb_path"],
                    Arguments = $"connect {App.Current.Services.GetRequiredService<IConfigurationRoot>()["emulator:connection_address"]}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            };
            adbProcess.Start();
            await Task.WhenAny(adbProcess.WaitForExitAsync(),
                adbProcess.ListenProcessStandardOutputAsync(cts.Token),
                adbProcess.ListenProcessStandardErrorAsync(cts.Token));
            await cts.CancelAsync();
            cts.Dispose();
            cts = new();

            if (adbProcess.ExitCode != 0)
            {
                Log.Error("安装失败");
                return;
            }

            Process adbProcess2 = new()
            {
                StartInfo = new()
                {
                    FileName = App.Current.Services.GetRequiredService<IConfigurationRoot>()["emulator:adb_path"],
                    Arguments = $"-s {App.Current.Services.GetRequiredService<IConfigurationRoot>()["emulator:connection_address"]} install \"{filePath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            };
            adbProcess2.Start();
            await Task.WhenAny(adbProcess2.WaitForExitAsync(),
                adbProcess2.ListenProcessStandardOutputAsync(cts.Token),
                adbProcess2.ListenProcessStandardErrorAsync(cts.Token));
            await cts.CancelAsync();
            cts.Dispose();
            cts = new();

            if (adbProcess2.ExitCode != 0)
            {
                Log.Error("安装失败");
            }
            else
                Log.Information("安装完成");
        }
        finally
        {
            emulatorProcess.Kill();
        }
    }
}

class PollingResult
{
    public bool IsNewVersion { get; set; } = false;
    public string FileUri { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
}