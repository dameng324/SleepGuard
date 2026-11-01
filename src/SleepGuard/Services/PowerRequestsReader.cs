using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SleepGuard.Models;

namespace SleepGuard.Services;

/// <summary>执行 <c>powercfg /requests</c> 并返回解析后的结果。</summary>
public static class PowerRequestsReader
{
    public static async Task<PowerRequestsResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (AppOptions.DemoMode)
        {
            return DemoData.CreateResult();
        }

        if (!OperatingSystem.IsWindows())
        {
            return PowerRequestsResult.Failure("powercfg 仅在 Windows 上可用。", needsElevation: false);
        }

        try
        {
            var startInfo = new ProcessStartInfo("powercfg.exe", "/requests")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                return PowerRequestsResult.Failure("无法启动 powercfg.exe。", !ElevationHelper.IsElevated);
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            var raw = string.Join(Environment.NewLine, new[] { stdout, stderr }.Where(t => !string.IsNullOrWhiteSpace(t)))
                .Trim();

            if (process.ExitCode != 0)
            {
                var message = FirstMeaningfulLine(raw) ?? $"powercfg 退出码 {process.ExitCode}";
                return PowerRequestsResult.Failure(message, !ElevationHelper.IsElevated, raw);
            }

            return new PowerRequestsResult(true, false, PowerRequestsParser.Parse(stdout), raw, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return PowerRequestsResult.Failure(ex.Message, !ElevationHelper.IsElevated);
        }
    }

    private static string? FirstMeaningfulLine(string text)
        => text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
               .FirstOrDefault(line => line.Length > 0);
}
