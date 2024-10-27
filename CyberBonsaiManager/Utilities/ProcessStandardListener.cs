using System.Diagnostics;
using Serilog;

namespace CyberBonsaiManager.Utilities;

public static class ProcessStandardListener
{
    public static async Task ListenProcessStandardOutputAsync(this Process process, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested) break;
            if (!string.IsNullOrWhiteSpace(line)) Log.Information(line);
        }
    }
    
    public static async Task ListenProcessStandardErrorAsync(this Process process, CancellationToken cancellationToken = default)
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
}