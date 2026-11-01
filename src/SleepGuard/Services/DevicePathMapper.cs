using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace SleepGuard.Services;

/// <summary>
/// 把 <c>powercfg</c> 报告的 NT 设备路径（<c>\Device\HarddiskVolume3\...</c>）翻译成盘符路径。
/// 翻译失败时原样返回，界面仍然可以显示。
/// </summary>
public static class DevicePathMapper
{
    private const int MaxPath = 1024;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int QueryDosDevice(string deviceName, StringBuilder targetPath, int maxLength);

    private static Dictionary<string, string>? s_map;
    private static readonly object s_gate = new();

    public static string ToWin32Path(string? devicePath)
    {
        if (string.IsNullOrWhiteSpace(devicePath))
        {
            return string.Empty;
        }

        if (!devicePath.StartsWith(@"\Device\", StringComparison.OrdinalIgnoreCase))
        {
            return devicePath;
        }

        var map = GetMap();
        foreach (var (device, drive) in map)
        {
            if (devicePath.StartsWith(device, StringComparison.OrdinalIgnoreCase))
            {
                return drive + devicePath[device.Length..];
            }
        }

        return devicePath;
    }

    private static Dictionary<string, string> GetMap()
    {
        lock (s_gate)
        {
            if (s_map is not null)
            {
                return s_map;
            }

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (OperatingSystem.IsWindows())
            {
                for (var letter = 'A'; letter <= 'Z'; letter++)
                {
                    var drive = $"{letter}:";
                    var buffer = new StringBuilder(MaxPath);
                    if (QueryDosDevice(drive, buffer, buffer.Capacity) != 0)
                    {
                        var device = buffer.ToString();
                        if (device.Length > 0 && device.StartsWith(@"\Device\", StringComparison.OrdinalIgnoreCase))
                        {
                            // 同一设备可能映射到多个盘符，保留第一个。
                            map.TryAdd(device, drive);
                        }
                    }
                }
            }

            s_map = map;
            return map;
        }
    }
}
