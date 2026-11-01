using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using SleepGuard.Services;
using SleepGuard.ViewModels;
using TKCode.Arc;

namespace SleepGuard;

public partial class MainWindow : Window
{
    private readonly MainViewModel _model = new();

    public MainWindow()
    {
        InitializeComponent();

        DataContext = _model;
        ConfigureTable();

        _model.Notice += OnNotice;
        Opened += async (_, _) => await _model.RefreshAsync();
        Closed += (_, _) => _model.Dispose();
    }

    /// <summary>Arc 的 Table 定位是“结构化记录”，列在代码里声明（值选择器保证排序可用）。</summary>
    private void ConfigureTable()
    {
        var table = BlockerTable;

        table.Columns.Add(new DataTableColumn("类型", row => ((BlockerRow)row).KindLabel)
        {
            Width = new GridLength(104),
            CellTemplate = Resources["KindCell"] as IDataTemplate
        });

        table.Columns.Add(new DataTableColumn("进程", row => ((BlockerRow)row).ExecutableName)
        {
            Width = new GridLength(190)
        });

        table.Columns.Add(new DataTableColumn("详情", row => ((BlockerRow)row).DetailText)
        {
            Width = new GridLength(1, GridUnitType.Star)
        });

        table.Columns.Add(new DataTableColumn("PID", row => ((BlockerRow)row).PidText)
        {
            Width = new GridLength(112),
            IsSortable = false,
            IsNumeric = true
        });

        table.Columns.Add(new DataTableColumn("操作", row => ((BlockerRow)row).ActionText)
        {
            Width = new GridLength(96),
            IsSortable = false,
            CellTemplate = Resources["ActionCell"] as IDataTemplate
        });
    }

    private async void OnRefreshClick(object? sender, RoutedEventArgs e)
        => await GuardedAsync("刷新", () => _model.RefreshAsync());

    private void OnTableSelectionChanged(object? sender, DataTableSelectionChangedEventArgs e)
        => _model.NotifySelectionChanged();

    private async void OnKillRowClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: BlockerRow row })
        {
            await GuardedAsync("结束进程", () => KillAsync([row]));
        }
    }

    private async void OnKillSelectedClick(object? sender, RoutedEventArgs e)
    {
        var rows = _model.SelectedBlockers.Where(row => row.CanTerminate).ToList();
        if (rows.Count == 0)
        {
            Toast.Show(this, "没有可结束的行", ToastKind.Info, "受保护的系统进程无法结束。");
            return;
        }

        await GuardedAsync("批量结束进程", () => KillAsync(rows));
    }

    /// <summary>事件处理器是 async void，异常必须自己接住，否则只会静默消失。</summary>
    private async Task GuardedAsync(string what, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            ErrorLog.Write($"{what} 失败", ex);
            Toast.Show(this, $"{what} 失败", ToastKind.Error, ex.Message);
        }
    }

    private async Task KillAsync(IReadOnlyList<BlockerRow> rows)
    {
        var names = string.Join("、", rows.Select(row => row.ExecutableName).Distinct());
        var pids = rows.SelectMany(row => row.Pids).Distinct().ToList();

        var preview = string.Join(", ", pids.Take(8)) + (pids.Count > 8 ? " …" : string.Empty);
        var confirmed = await Dialog.ConfirmAsync(
            this,
            $"结束 {names}？",
            $"将强制结束 {pids.Count} 个进程（PID {preview}）。未保存的数据可能丢失，无法撤销。",
            confirmText: "结束进程",
            cancelText: "取消",
            isDanger: true);

        if (!confirmed)
        {
            return;
        }

        var (killed, failures) = await _model.TerminateAsync(rows);

        if (failures.Count == 0)
        {
            Toast.Show(this, $"已结束 {killed} 个进程", ToastKind.Success, names);
        }
        else
        {
            Toast.Show(this, "部分进程未能结束", ToastKind.Error, failures[0]);
        }

        await _model.RefreshAsync();
    }

    private void OnReleaseClick(object? sender, RoutedEventArgs e)
    {
        // 走 IsPreventing 的 setter，由视图模型负责真正释放并给出提示。
        _model.IsPreventing = false;
    }

    private void OnElevateClick(object? sender, RoutedEventArgs e)
    {
        if (ElevationHelper.TryRelaunchElevated(out var error))
        {
            Close();
            return;
        }

        Toast.Show(this, "无法提升权限", ToastKind.Error, error);
    }

    private void OnNotice(object? sender, Notice notice)
        => Toast.Show(this, notice.Title, MapKind(notice.Kind), notice.Description);

    private static ToastKind MapKind(NoticeKind kind) => kind switch
    {
        NoticeKind.Success => ToastKind.Success,
        NoticeKind.Error => ToastKind.Error,
        _ => ToastKind.Info
    };
}
