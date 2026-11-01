using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using SleepGuard.Models;

namespace SleepGuard.Services;

/// <summary>
/// 解析 <c>powercfg /requests</c> 的文本输出。
/// <para>
/// 输出形如：
/// <code>
/// DISPLAY:
/// [PROCESS] \Device\HarddiskVolume3\Program Files\Google\Chrome\Application\chrome.exe
/// Chrome is playing video
///
/// SYSTEM:
/// None.
/// </code>
/// 各段标题（DISPLAY / SYSTEM / AWAYMODE / EXECUTION / PERFMON）保持英文，而 "None." 会随系统语言本地化，
/// 所以这里的解析对本地化文本保持宽容：只有 <c>[过程/服务/驱动]</c> 标签才会产生记录，其余行只作为“原因”附加。
/// </para>
/// </summary>
public static partial class PowerRequestsParser
{
    private static readonly Dictionary<string, BlockerKind> s_sections = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DISPLAY"] = BlockerKind.Display,
        ["SYSTEM"] = BlockerKind.System,
        ["AWAYMODE"] = BlockerKind.AwayMode,
        ["EXECUTION"] = BlockerKind.Execution,
        ["PERFMON"] = BlockerKind.PerfMon
    };

    [GeneratedRegex(@"^(?<name>[A-Za-z]+)\s*:\s*$")]
    private static partial Regex SectionRegex();

    [GeneratedRegex(@"^\[(?<origin>[A-Za-z]+)\]\s*(?<target>.*)$")]
    private static partial Regex EntryRegex();

    /// <summary>各语言下表示“没有请求”的占位文本。</summary>
    [GeneratedRegex(@"^\s*(none|n\/a|无|沒有|没有|keine|aucun|aucune|ninguno|ninguna|nessuno|nessuna|なし|없음)\b[^\n]*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NoneRegex();

    public static IReadOnlyList<PowerRequestEntry> Parse(string? output)
    {
        var entries = new List<PowerRequestEntry>();
        if (string.IsNullOrWhiteSpace(output))
        {
            return entries;
        }

        var kind = BlockerKind.Unknown;
        RequestOrigin? origin = null;
        string? target = null;
        var reason = new StringBuilder();

        void Flush()
        {
            if (origin is null || target is null)
            {
                reason.Clear();
                return;
            }

            var text = reason.ToString().Trim();
            entries.Add(new PowerRequestEntry(kind, origin.Value, target, text.Length == 0 ? null : text));
            origin = null;
            target = null;
            reason.Clear();
        }

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim('\r', '\t', ' ');
            if (line.Length == 0)
            {
                Flush();
                continue;
            }

            var section = SectionRegex().Match(line);
            if (section.Success && s_sections.TryGetValue(section.Groups["name"].Value, out var sectionKind))
            {
                Flush();
                kind = sectionKind;
                continue;
            }

            var entry = EntryRegex().Match(line);
            if (entry.Success)
            {
                Flush();
                origin = ParseOrigin(entry.Groups["origin"].Value);
                target = entry.Groups["target"].Value.Trim();
                if (target.Length == 0)
                {
                    origin = null;
                }

                continue;
            }

            if (NoneRegex().IsMatch(line))
            {
                continue;
            }

            if (origin is not null)
            {
                // 请求方给出的原因，可能是多行。
                if (reason.Length > 0)
                {
                    reason.Append(' ');
                }

                reason.Append(line);
            }
        }

        Flush();
        return entries;
    }

    private static RequestOrigin ParseOrigin(string value)
        => value.ToUpperInvariant() switch
        {
            "PROCESS" => RequestOrigin.Process,
            "SERVICE" => RequestOrigin.Service,
            "DRIVER" => RequestOrigin.Driver,
            _ => RequestOrigin.Unknown
        };
}
