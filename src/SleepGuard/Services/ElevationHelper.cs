using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SleepGuard.Services;

/// <summary>管理员权限检测与提权重启。</summary>
public static class ElevationHelper
{
    private const uint TokenQuery = 0x0008;
    private const int TokenElevation = 20;

    private static bool? s_isElevated;

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle,
        int tokenInformationClass,
        out int tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    /// <summary>当前进程是否以管理员身份运行。<c>powercfg /requests</c> 与结束受保护进程都需要它。</summary>
    public static bool IsElevated => s_isElevated ??= Detect();

    private static bool Detect()
    {
        if (!OperatingSystem.IsWindows())
        {
            // 非 Windows 平台没有这个概念，按“已具备权限”处理，避免界面一直提示。
            return true;
        }

        var token = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out token) || token == IntPtr.Zero)
            {
                return false;
            }

            if (!GetTokenInformation(token, TokenElevation, out var elevated, sizeof(int), out _))
            {
                return false;
            }

            return elevated != 0;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            if (token != IntPtr.Zero)
            {
                CloseHandle(token);
            }
        }
    }

    /// <summary>以管理员身份重新启动本程序；返回 false 时 <paramref name="error"/> 给出原因（含用户取消）。</summary>
    public static bool TryRelaunchElevated(out string? error)
    {
        error = null;

        var path = Environment.ProcessPath;
        if (string.IsNullOrEmpty(path))
        {
            error = "无法确定当前可执行文件的路径。";
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo(path)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            };

            Process.Start(startInfo);
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            error = "已取消管理员权限请求。";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
