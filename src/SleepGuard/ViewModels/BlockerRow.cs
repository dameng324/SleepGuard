using System;
using System.Collections.Generic;
using SleepGuard.Infrastructure;
using SleepGuard.Models;

namespace SleepGuard.ViewModels;

/// <summary>表格中的一行：一条休眠阻止请求 + 解析出的进程信息 + 是否允许结束。</summary>
public sealed class BlockerRow : ObservableObject
{
    public BlockerRow(
        PowerRequestEntry entry,
        string displayPath,
        string? executableName,
        IReadOnlyList<int> pids,
        bool canTerminate,
        string? blockReason)
    {
        Kind = entry.Kind;
        Origin = entry.Origin;
        Target = entry.Target;
        Reason = string.IsNullOrWhiteSpace(entry.Reason) ? "—" : entry.Reason;
        DisplayPath = displayPath;
        ExecutableName = executableName ?? "—";
        Pids = pids;
        CanTerminate = canTerminate;
        BlockReason = blockReason;
    }

    public BlockerKind Kind { get; }

    public RequestOrigin Origin { get; }

    public string Target { get; }

    public string Reason { get; }

    /// <summary>可读路径（已尽量从 NT 设备路径翻译成盘符路径）。</summary>
    public string DisplayPath { get; }

    public string ExecutableName { get; }

    public IReadOnlyList<int> Pids { get; }

    public bool CanTerminate { get; }

    public string? BlockReason { get; }

    public string KindLabel => Kind switch
    {
        BlockerKind.Display => "显示",
        BlockerKind.System => "系统",
        BlockerKind.AwayMode => "离开模式",
        BlockerKind.Execution => "执行",
        BlockerKind.PerfMon => "性能监视",
        _ => "未知"
    };

    /// <summary>用于 <c>arc:Badge</c> 的 Tone（字符串会被绑定转换为枚举）。</summary>
    public string KindTone => Kind switch
    {
        BlockerKind.Display => "Warning",
        BlockerKind.System => "Danger",
        BlockerKind.AwayMode => "Info",
        BlockerKind.Execution => "Info",
        _ => "Default"
    };

    public string OriginLabel => Origin switch
    {
        RequestOrigin.Process => "进程",
        RequestOrigin.Service => "服务",
        RequestOrigin.Driver => "驱动",
        _ => "未知"
    };

    public string PidText => Pids.Count switch
    {
        0 => "—",
        1 => Pids[0].ToString(),
        _ => $"{Pids[0]} +{Pids.Count - 1}"
    };

    public string ActionText => CanTerminate ? "结束" : "受保护";

    public string DetailText => Reason != "—" ? Reason : DisplayPath;

    public string Tooltip => $"{DisplayPath}\n{Pids.Count} 个进程" +
                             (BlockReason is null ? string.Empty : $"\n不能结束：{BlockReason}");
}
