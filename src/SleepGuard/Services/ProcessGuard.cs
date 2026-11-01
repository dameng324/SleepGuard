using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace SleepGuard.Services;

/// <summary>结束进程前的安全检查结果。</summary>
/// <param name="CanTerminate">是否允许从界面结束。</param>
/// <param name="Reason">不允许时的说明，会显示在界面的提示里。</param>
public readonly record struct TerminateCheck(bool CanTerminate, string? Reason);

/// <summary>
/// 保护性判断：系统关键进程、本程序自身、以及 PID 0/4 一律不允许结束，
/// 否则轻则界面错乱，重则系统立即蓝屏。
/// </summary>
public static class ProcessGuard
{
    private static readonly HashSet<string> s_protectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "system",
        "system idle process",
        "idle",
        "registry",
        "memory compression",
        "smss.exe",
        "csrss.exe",
        "wininit.exe",
        "winlogon.exe",
        "services.exe",
        "lsass.exe",
        "svchost.exe",
        "dwm.exe",
        "fontdrvhost.exe",
        "audiodg.exe",
        "sihost.exe",
        "msmpeng.exe",
        "securityhealthservice.exe",
        "spoolsv.exe",
        "trustedinstaller.exe"
    };

    public static string OwnExecutableName
        => Path.GetFileName(Environment.ProcessPath) ?? "SleepGuard.exe";

    public static TerminateCheck Evaluate(string? executableName, IReadOnlyList<int> pids)
    {
        if (pids.Count == 0)
        {
            return new TerminateCheck(false, "没有找到对应的进程，可能已经退出。");
        }

        var name = Path.GetFileName(executableName ?? string.Empty);
        if (name.Length == 0)
        {
            return new TerminateCheck(false, "无法确定进程名。");
        }

        if (string.Equals(name, OwnExecutableName, StringComparison.OrdinalIgnoreCase))
        {
            return new TerminateCheck(false, "这是 SleepGuard 自身，不能结束。");
        }

        if (s_protectedNames.Contains(name))
        {
            return new TerminateCheck(false, $"{name} 是系统关键进程，结束它会导致系统不稳定。");
        }

        if (pids.Any(pid => pid <= 4))
        {
            return new TerminateCheck(false, "系统进程（PID ≤ 4）不能结束。");
        }

        return new TerminateCheck(true, null);
    }

    /// <summary>强制结束给定的 PID，返回成功数与失败说明。</summary>
    public static (int Killed, IReadOnlyList<string> Failures) Terminate(IEnumerable<int> pids)
    {
        var killed = 0;
        var failures = new List<string>();

        foreach (var pid in pids)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                process.Kill();
                killed++;
            }
            catch (ArgumentException)
            {
                // 进程已经退出。
                killed++;
            }
            catch (Exception ex)
            {
                failures.Add($"PID {pid}: {ex.Message}");
            }
        }

        return (killed, failures);
    }
}
