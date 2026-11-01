using System;

namespace SleepGuard;

/// <summary>命令行开关。目前只有一个：<c>--demo</c>。</summary>
public static class AppOptions
{
    /// <summary>
    /// 演示模式：用内置样例数据代替 <c>powercfg /requests</c> 的真实输出，
    /// 这样在没有管理员权限（或非 Windows）时也能检查界面、结束进程确认框与通知的效果。
    /// </summary>
    public static bool DemoMode { get; private set; }

    public static void Parse(string[] args)
        => DemoMode = args.Any(a => a.Equals("--demo", StringComparison.OrdinalIgnoreCase));
}
