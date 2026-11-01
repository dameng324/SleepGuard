using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SleepGuard.Models;

namespace SleepGuard.Services;

/// <summary>
/// 演示数据（<c>--demo</c>）：按本机当前真实运行的进程拼出一份与 <c>powercfg /requests</c> 格式相同的输出，
/// 这样在没有管理员权限时也能查看并操作完整界面。
/// </summary>
public static class DemoData
{
    private static readonly (string Name, BlockerKind Kind, string Reason)[] s_candidates =
    [
        ("notepad.exe", BlockerKind.Display, "记事本示例：模拟正在播放视频"),
        ("chrome.exe", BlockerKind.Display, "Chrome 正在播放视频"),
        ("msedge.exe", BlockerKind.Display, "Edge 标签页正在播放媒体"),
        ("Code.exe", BlockerKind.System, "VS Code 正在运行构建任务"),
        ("devenv.exe", BlockerKind.System, "Visual Studio 正在调试"),
        ("Spotify.exe", BlockerKind.System, "Spotify 正在播放音乐"),
        ("zoom.exe", BlockerKind.AwayMode, "Zoom 会议进行中"),
        ("Discord.exe", BlockerKind.Display, "Discord 语音通话中"),
        ("obs64.exe", BlockerKind.Display, "OBS 正在录制"),
        ("WeChat.exe", BlockerKind.System, "微信正在接收文件"),
        ("PotPlayerMini64.exe", BlockerKind.Display, "PotPlayer 正在播放")
    ];

    private static readonly Dictionary<BlockerKind, string> s_sectionNames = new()
    {
        [BlockerKind.Display] = "DISPLAY",
        [BlockerKind.System] = "SYSTEM",
        [BlockerKind.AwayMode] = "AWAYMODE",
        [BlockerKind.Execution] = "EXECUTION",
        [BlockerKind.PerfMon] = "PERFMON"
    };

    public static PowerRequestsResult CreateResult()
    {
        var snapshot = ProcessLookup.Snapshot();
        var buckets = new Dictionary<BlockerKind, List<(string Target, string Reason)>>();

        void Add(BlockerKind kind, string target, string reason)
        {
            if (!buckets.TryGetValue(kind, out var list))
            {
                list = [];
                buckets[kind] = list;
            }

            list.Add((target, reason));
        }

        foreach (var (name, kind, reason) in s_candidates)
        {
            var match = snapshot.FirstOrDefault(entry =>
                string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match.Pid <= 0)
            {
                continue;
            }

            var path = ProcessLookup.TryGetPath(match.Pid)
                       ?? $@"C:\Program Files\Sample\{name}";
            Add(kind, path, reason);
        }

        // 系统关键进程：展示“受保护、不可结束”的状态。
        var svchost = snapshot.FirstOrDefault(entry =>
            string.Equals(entry.Name, "svchost.exe", StringComparison.OrdinalIgnoreCase));
        if (svchost.Pid > 0)
        {
            Add(BlockerKind.System, @"C:\Windows\System32\svchost.exe", "Legacy Kernel Caller");
        }

        if (buckets.Sum(pair => pair.Value.Count) < 2)
        {
            Add(BlockerKind.Display, @"C:\Program Files\SamplePlayer\SamplePlayer.exe", "示例播放器正在播放视频");
            Add(BlockerKind.System, @"C:\Program Files\SampleBackup\SampleBackup.exe", "示例备份工具正在写入归档");
        }

        var builder = new StringBuilder();
        foreach (var kind in new[] { BlockerKind.Display, BlockerKind.System, BlockerKind.AwayMode, BlockerKind.Execution, BlockerKind.PerfMon })
        {
            builder.Append(s_sectionNames[kind]).Append(':').Append('\n');

            if (buckets.TryGetValue(kind, out var items) && items.Count > 0)
            {
                foreach (var (target, reason) in items)
                {
                    builder.Append("[PROCESS] ").Append(target).Append('\n');
                    builder.Append(reason).Append('\n');
                }
            }
            else
            {
                builder.Append("None.\n");
            }

            builder.Append('\n');
        }

        var raw = builder.ToString().TrimEnd();
        return new PowerRequestsResult(true, false, PowerRequestsParser.Parse(raw), raw, null);
    }
}
