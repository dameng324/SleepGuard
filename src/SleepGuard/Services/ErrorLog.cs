using System;
using System.IO;

namespace SleepGuard.Services;

/// <summary>
/// 极简错误日志：写到 <c>%LOCALAPPDATA%\SleepGuard\error.log</c>。
/// 桌面程序没有控制台，界面里出现异常时唯一的线索就是这个文件。
/// </summary>
public static class ErrorLog
{
    private static readonly object s_gate = new();

    public static string DirectoryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SleepGuard");

    public static string FilePath => Path.Combine(DirectoryPath, "error.log");

    public static void Write(string message, Exception? exception = null)
    {
        try
        {
            lock (s_gate)
            {
                System.IO.Directory.CreateDirectory(DirectoryPath);
                var text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
                if (exception is not null)
                {
                    text += Environment.NewLine + exception;
                }

                File.AppendAllText(FilePath, text + Environment.NewLine);
            }
        }
        catch (Exception)
        {
            // 日志失败不能再引发新的异常。
        }
    }
}
