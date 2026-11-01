using System.Collections.Generic;

namespace SleepGuard.Models;

/// <summary><c>powercfg /requests</c> 输出中的四类请求来源。</summary>
public enum BlockerKind
{
    Unknown = 0,
    Display,
    System,
    AwayMode,
    Execution,
    PerfMon
}

/// <summary>请求方的类型标签：<c>[PROCESS]</c>、<c>[SERVICE]</c>、<c>[DRIVER]</c>。</summary>
public enum RequestOrigin
{
    Unknown = 0,
    Process,
    Service,
    Driver
}

/// <summary>一条“谁在阻止休眠”的记录。</summary>
/// <param name="Kind">阻止类型（显示 / 系统 / 离开模式 / 执行 / 性能监视）。</param>
/// <param name="Origin">请求方类型。</param>
/// <param name="Target">原始目标文本，通常是 <c>\Device\HarddiskVolume3\...\app.exe</c> 这样的设备路径。</param>
/// <param name="Reason">请求方给出的原因（可能为空）。</param>
public sealed record PowerRequestEntry(
    BlockerKind Kind,
    RequestOrigin Origin,
    string Target,
    string? Reason);

/// <summary>一次探测的结果。</summary>
public sealed record PowerRequestsResult(
    bool Succeeded,
    bool NeedsElevation,
    IReadOnlyList<PowerRequestEntry> Entries,
    string RawOutput,
    string? Error)
{
    public static PowerRequestsResult Failure(string error, bool needsElevation, string raw = "")
        => new(false, needsElevation, [], raw, error);
}
