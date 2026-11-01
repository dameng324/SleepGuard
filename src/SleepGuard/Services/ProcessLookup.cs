using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace SleepGuard.Services;

/// <summary>进程快照中的一项。</summary>
public readonly record struct ProcessEntry(int Pid, string Name, string? Path);

/// <summary>进程枚举：先用便宜的“名称快照”做匹配，只在需要显示路径时再打开进程主模块。</summary>
public static class ProcessLookup
{
    /// <summary>
    /// 枚举当前所有进程的 PID 与可执行文件名。只用 <see cref="Process.ProcessName"/>（不打开主模块），
    /// 所以即使面对受保护进程也很快、不会因为访问被拒而失败。
    /// </summary>
    public static IReadOnlyList<ProcessEntry> Snapshot()
    {
        var list = new List<ProcessEntry>(512);
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (Exception)
        {
            return list;
        }

        foreach (var process in processes)
        {
            try
            {
                var name = process.ProcessName;
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    name = name[..^4];
                }

                list.Add(new ProcessEntry(process.Id, name + ".exe", null));
            }
            catch (Exception)
            {
                // 进程可能在枚举过程中退出，或不允许读取属性。
            }
            finally
            {
                process.Dispose();
            }
        }

        return list;
    }

    /// <summary>尝试读取某个进程的可执行文件完整路径；受保护进程返回 null。</summary>
    public static string? TryGetPath(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.MainModule?.FileName;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>从 <c>powercfg</c> 的目标文本中取出可执行文件名。</summary>
    public static string? GetExecutableName(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return null;
        }

        var index = target.LastIndexOfAny(['\\', '/']);
        var name = index >= 0 ? target[(index + 1)..] : target;
        name = name.Trim().Trim('"');
        if (name.Length == 0 || name.IndexOf(' ') >= 0 || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return name;
    }

    /// <summary>按可执行文件名（不区分大小写）找出所有匹配的进程。</summary>
    public static IReadOnlyList<ProcessEntry> Match(IReadOnlyList<ProcessEntry> snapshot, string? executableName)
    {
        if (string.IsNullOrWhiteSpace(executableName))
        {
            return [];
        }

        var matches = new List<ProcessEntry>();
        foreach (var entry in snapshot)
        {
            if (string.Equals(Path.GetFileName(entry.Name), executableName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.Name, executableName, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(entry);
            }
        }

        return matches;
    }
}
