using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Threading;
using SleepGuard.Infrastructure;
using SleepGuard.Models;
using SleepGuard.Services;

namespace SleepGuard.ViewModels;

public enum NoticeKind
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>给界面层的一条提示（最终显示为 Arc 的 Toast）。</summary>
public sealed record Notice(string Title, NoticeKind Kind, string? Description = null);

/// <summary>主窗口的状态与业务逻辑。</summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    /// <summary>自动刷新的最小间隔（秒）。</summary>
    private const int RefreshIntervalSeconds = 5;

    private readonly SleepPreventer _preventer = new();
    private readonly DispatcherTimer _clock;

    private bool _refreshing;
    private bool _syncingPrevent;
    private int _tick;
    private string _signature = string.Empty;

    private bool _isRefreshing;
    private bool _autoRefresh = true;
    private string _statusMessage = "正在读取 powercfg /requests …";
    private string _lastUpdatedText = "尚未刷新";
    private string _rawOutput = string.Empty;
    private bool _showRawOutput;
    private bool _hasError;
    private int _blockerCount;
    private int _kindCount;
    private int _processBlockerCount;
    private string _kindSummaryText = "—";
    private bool _isPreventing;
    private bool _keepDisplayOn;
    private string _durationKey = "forever";
    private string _preventElapsedText = "开启后开始计时";
    private string _preventRemainingText = string.Empty;

    public MainViewModel()
    {
        IsElevated = ElevationHelper.IsElevated || AppOptions.DemoMode;
        Blockers = [];
        SelectedBlockers = [];
        SelectedBlockers.CollectionChanged += (_, _) => NotifySelectionChanged();

        _preventer.StateChanged += (_, _) => Dispatcher.UIThread.Post(SyncPreventState);

        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += OnClockTick;
        _clock.Start();
    }

    public ObservableCollection<BlockerRow> Blockers { get; }

    public AvaloniaList<BlockerRow> SelectedBlockers { get; }

    public bool IsElevated { get; }

    public bool NeedsElevation => !IsElevated;

    public bool IsDemo => AppOptions.DemoMode;

    public string ElevationText => IsElevated ? "管理员权限" : "普通权限";

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set => SetProperty(ref _isRefreshing, value);
    }

    public bool AutoRefresh
    {
        get => _autoRefresh;
        set
        {
            if (SetProperty(ref _autoRefresh, value))
            {
                OnPropertyChanged(nameof(AutoRefreshText));
                _tick = 0;
            }
        }
    }

    public string AutoRefreshText => AutoRefresh ? $"每 {RefreshIntervalSeconds} 秒自动刷新" : "自动刷新已关闭";

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }

    public string LastUpdatedText
    {
        get => _lastUpdatedText;
        private set
        {
            if (SetProperty(ref _lastUpdatedText, value))
            {
                OnPropertyChanged(nameof(LastUpdatedLabel));
            }
        }
    }

    public string LastUpdatedLabel => $"上次刷新 {LastUpdatedText}";

    public string RawOutput
    {
        get => _rawOutput;
        private set => SetProperty(ref _rawOutput, value);
    }

    public bool ShowRawOutput
    {
        get => _showRawOutput;
        set => SetProperty(ref _showRawOutput, value);
    }

    public int BlockerCount
    {
        get => _blockerCount;
        private set
        {
            if (SetProperty(ref _blockerCount, value))
            {
                OnPropertyChanged(nameof(HasBlockers), nameof(EmptyHint));
            }
        }
    }

    public bool HasBlockers => BlockerCount > 0;

    public string EmptyHint => IsElevated
        ? "当前没有程序阻止系统休眠。"
        : "需要管理员权限才能读取阻止列表。";

    public int KindCount
    {
        get => _kindCount;
        private set => SetProperty(ref _kindCount, value);
    }

    public int ProcessBlockerCount
    {
        get => _processBlockerCount;
        private set => SetProperty(ref _processBlockerCount, value);
    }

    public string KindSummaryText
    {
        get => _kindSummaryText;
        private set => SetProperty(ref _kindSummaryText, value);
    }

    // ── 我的休眠阻止 ──────────────────────────────────────────────

    public bool IsPreventing
    {
        get => _isPreventing;
        set
        {
            if (!SetProperty(ref _isPreventing, value))
            {
                return;
            }

            OnPropertyChanged(nameof(PreventStateText), nameof(CanChangeDuration), nameof(HasLimit));
            if (_syncingPrevent)
            {
                return;
            }

            try
            {
                if (value)
                {
                    StartPrevent();
                }
                else
                {
                    StopPrevent();
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Write("切换休眠阻止失败", ex);
                _syncingPrevent = true;
                try
                {
                    IsPreventing = _preventer.IsActive;
                }
                finally
                {
                    _syncingPrevent = false;
                }
            }
        }
    }

    public string PreventStateText => IsPreventing ? "阻止中" : "未启用";

    public bool KeepDisplayOn
    {
        get => _keepDisplayOn;
        set
        {
            if (SetProperty(ref _keepDisplayOn, value))
            {
                _preventer.SetKeepDisplay(value);
                OnPropertyChanged(nameof(PreventDescription));
            }
        }
    }

    public string PreventDescription => KeepDisplayOn
        ? "已同时保持显示器常亮（ES_DISPLAY_REQUIRED）。"
        : "只阻止系统休眠，显示器仍按计划关闭（ES_SYSTEM_REQUIRED）。";

    /// <summary>时长选项：forever / 30 / 60 / 120 / 240（分钟）。</summary>
    public string DurationKey
    {
        get => _durationKey;
        set => SetProperty(ref _durationKey, value ?? "forever");
    }

    public bool CanChangeDuration => !IsPreventing;

    public bool HasLimit => IsPreventing && _preventer.Remaining is not null;

    public string PreventElapsedText
    {
        get => _preventElapsedText;
        private set => SetProperty(ref _preventElapsedText, value);
    }

    public string PreventRemainingText
    {
        get => _preventRemainingText;
        private set => SetProperty(ref _preventRemainingText, value);
    }

    public string SelectedText => SelectedBlockers.Count == 0
        ? "选中行后可批量结束"
        : $"已选 {SelectedBlockers.Count} 项";

    public bool HasSelection => SelectedBlockers.Count > 0;

    /// <summary>表格的选中项由 Arc 的 Table 直接写进 <see cref="SelectedBlockers"/>，这里只负责刷新派生文本。</summary>
    public void NotifySelectionChanged()
        => OnPropertyChanged(nameof(SelectedText), nameof(HasSelection));

    public event EventHandler<Notice>? Notice;

    // ── 刷新 ─────────────────────────────────────────────────────

    public async Task RefreshAsync()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        IsRefreshing = true;

        try
        {
            // 读取 powercfg 与枚举进程都不便宜，放到后台线程，避免每 5 秒卡一下界面。
            var result = await PowerRequestsReader.ReadAsync().ConfigureAwait(true);
            var rows = result.Succeeded ? await Task.Run(() => BuildRows(result)).ConfigureAwait(true) : [];
            Apply(result, rows);
        }
        catch (Exception ex)
        {
            StatusMessage = $"读取失败：{ex.Message}";
            HasError = true;
        }
        finally
        {
            _refreshing = false;
            IsRefreshing = false;
        }
    }

    private void Apply(PowerRequestsResult result, IReadOnlyList<BlockerRow> rows)
    {
        RawOutput = result.RawOutput;
        LastUpdatedText = DateTime.Now.ToString("HH:mm:ss");

        if (!result.Succeeded)
        {
            HasError = true;
            StatusMessage = result.NeedsElevation
                ? "powercfg /requests 需要管理员权限。"
                : result.Error ?? "读取失败。";
            ReplaceRows([]);
            return;
        }

        HasError = false;
        StatusMessage = result.Entries.Count == 0
            ? "没有程序阻止休眠。"
            : $"读到 {result.Entries.Count} 条休眠阻止请求。";

        var signature = string.Join(
            "\n",
            rows.Select(r => $"{r.Kind}|{r.Origin}|{r.Target}|{r.Reason}|{string.Join(',', r.Pids)}"));

        if (signature == _signature)
        {
            return;
        }

        _signature = signature;
        ReplaceRows(rows);
    }

    private static List<BlockerRow> BuildRows(PowerRequestsResult result)
    {
        var rows = new List<BlockerRow>(result.Entries.Count);
        if (result.Entries.Count == 0)
        {
            return rows;
        }

        var snapshot = ProcessLookup.Snapshot();

        foreach (var entry in result.Entries)
        {
            var executable = ProcessLookup.GetExecutableName(entry.Target);
            var matches = ProcessLookup.Match(snapshot, executable);
            var pids = matches.Select(m => m.Pid).ToList();
            var displayPath = DevicePathMapper.ToWin32Path(entry.Target);

            TerminateCheck check;
            if (entry.Origin != RequestOrigin.Process)
            {
                check = new TerminateCheck(false, entry.Origin == RequestOrigin.Service
                    ? "由系统服务发起，请在“服务”管理器中停止对应服务。"
                    : "由驱动程序发起，无法通过结束进程解除。");
            }
            else if (pids.Count == 0)
            {
                check = new TerminateCheck(false, "进程已经不在了，稍后刷新即可。");
            }
            else
            {
                check = ProcessGuard.Evaluate(executable, pids);
            }

            rows.Add(new BlockerRow(entry, displayPath, executable, pids, check.CanTerminate, check.Reason));
        }

        return rows;
    }

    private void ReplaceRows(IReadOnlyList<BlockerRow> rows)
    {
        Blockers.Clear();
        SelectedBlockers.Clear();
        foreach (var row in rows)
        {
            Blockers.Add(row);
        }

        BlockerCount = Blockers.Count;
        ProcessBlockerCount = Blockers.Count(r => r.Origin == RequestOrigin.Process);
        OnPropertyChanged(nameof(SelectedText));

        var kinds = Blockers.Select(r => r.Kind).Distinct().OrderBy(k => k).ToList();
        KindCount = kinds.Count;
        KindSummaryText = kinds.Count == 0
            ? "—"
            : string.Join(" · ", kinds.Select(k => Blockers.First(b => b.Kind == k).KindLabel));
    }

    // ── 结束进程 ─────────────────────────────────────────────────

    public Task<(int Killed, IReadOnlyList<string> Failures)> TerminateAsync(IReadOnlyList<BlockerRow> rows)
        => Task.Run(() =>
        {
            var killed = 0;
            var failures = new List<string>();

            foreach (var row in rows.Where(r => r.CanTerminate))
            {
                var (count, errors) = ProcessGuard.Terminate(row.Pids);
                killed += count;
                failures.AddRange(errors.Select(e => $"{row.ExecutableName} {e}"));
            }

            return (killed, (IReadOnlyList<string>)failures);
        });

    // ── 主动阻止休眠 ──────────────────────────────────────────────

    private void StartPrevent()
    {
        var duration = DurationKey switch
        {
            "30" => TimeSpan.FromMinutes(30),
            "60" => TimeSpan.FromHours(1),
            "120" => TimeSpan.FromHours(2),
            "240" => TimeSpan.FromHours(4),
            _ => (TimeSpan?)null
        };

        _preventer.Start(KeepDisplayOn, duration);
        SyncPreventState();
        Notice?.Invoke(this, new Notice(
            "已开始阻止系统休眠",
            NoticeKind.Success,
            KeepDisplayOn ? "系统不会休眠，显示器也保持常亮。" : "系统不会休眠，但显示器仍会按计划关闭。"));
    }

    private void StopPrevent()
    {
        _preventer.Stop();
        SyncPreventState();
        Notice?.Invoke(this, new Notice("已释放休眠阻止", NoticeKind.Info, "系统可以按电源计划重新休眠。"));
    }

    private void SyncPreventState()
    {
        var active = _preventer.IsActive;

        _syncingPrevent = true;
        try
        {
            IsPreventing = active;
        }
        finally
        {
            _syncingPrevent = false;
        }

        if (!active)
        {
            PreventElapsedText = "开启后开始计时";
            PreventRemainingText = string.Empty;
        }

        OnPropertyChanged(nameof(CanChangeDuration), nameof(HasLimit), nameof(PreventStateText));
    }

    private void OnClockTick(object? sender, EventArgs e)
    {
        _tick++;

        if (IsPreventing)
        {
            var elapsed = _preventer.Elapsed;
            PreventElapsedText = $"已保持 {Format(elapsed)}";

            var remaining = _preventer.Remaining;
            PreventRemainingText = remaining is { } value ? $"剩余 {Format(value)}" : "无时限";

            if (!_preventer.IsActive)
            {
                // 到达设定的时长，自动释放。
                SyncPreventState();
                Notice?.Invoke(this, new Notice("保持时长已到", NoticeKind.Info, "系统可以重新休眠了。"));
            }
        }

        if (AutoRefresh && _tick % RefreshIntervalSeconds == 0 && !_refreshing)
        {
            _ = RefreshAsync();
        }
    }

    private static string Format(TimeSpan value)
        => value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{value.Minutes:00}:{value.Seconds:00}";

    public void Dispose()
    {
        _clock.Stop();
        _preventer.Dispose();
    }
}
