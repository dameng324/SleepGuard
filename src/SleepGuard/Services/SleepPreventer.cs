using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace SleepGuard.Services;

/// <summary>
/// 由本程序主动阻止系统休眠。
/// <para>
/// 使用 <c>SetThreadExecutionState</c>：这个状态属于调用它的线程，所以真正的调用发生在一条常驻后台线程上，
/// 并每 4 秒重新声明一次（某些驱动/程序会重置全局状态），关闭时由同一条线程用 <c>ES_CONTINUOUS</c> 释放。
/// </para>
/// </summary>
public sealed class SleepPreventer : IDisposable
{
    [Flags]
    private enum ExecutionState : uint
    {
        SystemRequired = 0x00000001,
        DisplayRequired = 0x00000002,
        Continuous = 0x80000000
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern ExecutionState SetThreadExecutionState(ExecutionState flags);

    private readonly object _gate = new();
    private readonly AutoResetEvent _signal = new(false);

    private Thread? _thread;
    private int _generation;
    private bool _active;
    private bool _keepDisplay;
    private DateTimeOffset _startedAt;
    private DateTimeOffset? _deadline;
    private bool _disposed;

    /// <summary>状态变化（开始、停止、超时自动停止）时触发，可能在后台线程上触发。</summary>
    public event EventHandler? StateChanged;

    public bool IsActive
    {
        get
        {
            lock (_gate)
            {
                return _active;
            }
        }
    }

    public bool KeepDisplayOn
    {
        get
        {
            lock (_gate)
            {
                return _keepDisplay;
            }
        }
    }

    public DateTimeOffset StartedAt
    {
        get
        {
            lock (_gate)
            {
                return _startedAt;
            }
        }
    }

    public TimeSpan Elapsed
    {
        get
        {
            lock (_gate)
            {
                return _active ? DateTimeOffset.Now - _startedAt : TimeSpan.Zero;
            }
        }
    }

    /// <summary>设定的保持时长；null 表示一直保持到手动关闭。</summary>
    public TimeSpan? Limit
    {
        get
        {
            lock (_gate)
            {
                return _deadline is null ? null : _deadline.Value - _startedAt;
            }
        }
    }

    /// <summary>剩余时间；没有设定时长或未启用时为 null。</summary>
    public TimeSpan? Remaining
    {
        get
        {
            lock (_gate)
            {
                if (!_active || _deadline is not { } deadline)
                {
                    return null;
                }

                var remaining = deadline - DateTimeOffset.Now;
                return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
            }
        }
    }

    public void Start(bool keepDisplayOn, TimeSpan? duration)
    {
        StopCore(wait: true);

        Thread thread;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _generation++;
            _startedAt = DateTimeOffset.Now;
            _deadline = duration is { } limit && limit > TimeSpan.Zero ? _startedAt + limit : null;
            _keepDisplay = keepDisplayOn;
            _active = true;

            thread = new Thread(Worker)
            {
                IsBackground = true,
                Name = "SleepGuard.SleepPreventer"
            };
            _thread = thread;
        }

        thread.Start();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>运行中切换“是否同时保持显示器常亮”，不需要重新计时。</summary>
    public void SetKeepDisplay(bool keepDisplayOn)
    {
        lock (_gate)
        {
            if (_keepDisplay == keepDisplayOn)
            {
                return;
            }

            _keepDisplay = keepDisplayOn;
        }

        _signal.Set();
    }

    public void Stop() => StopCore(wait: true);

    private void StopCore(bool wait)
    {
        Thread? thread;
        lock (_gate)
        {
            if (!_active && _thread is null)
            {
                return;
            }

            _active = false;
            _generation++;
            thread = _thread;
        }

        _signal.Set();

        if (wait && thread is not null && thread != Thread.CurrentThread)
        {
            thread.Join(2000);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Worker()
    {
        int generation;
        lock (_gate)
        {
            generation = _generation;
        }

        while (true)
        {
            bool running;
            bool keepDisplay;
            lock (_gate)
            {
                running = _active && generation == _generation;
                keepDisplay = _keepDisplay;

                if (running && _deadline is { } deadline && DateTimeOffset.Now >= deadline)
                {
                    // 到达设定时长，自动释放。
                    _active = false;
                    running = false;
                }
            }

            if (!running)
            {
                break;
            }

            Apply(keepDisplay);
            _signal.WaitOne(TimeSpan.FromSeconds(4));
        }

        // 必须由同一条线程释放，否则其它线程的状态不受影响。
        try
        {
            SetThreadExecutionState(ExecutionState.Continuous);
        }
        catch (Exception)
        {
            // 非 Windows 平台或 API 不可用时忽略。
        }

        lock (_gate)
        {
            if (generation == _generation)
            {
                _thread = null;
                _active = false;
            }
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void Apply(bool keepDisplay)
    {
        try
        {
            var flags = ExecutionState.Continuous | ExecutionState.SystemRequired;
            if (keepDisplay)
            {
                flags |= ExecutionState.DisplayRequired;
            }

            SetThreadExecutionState(flags);
        }
        catch (Exception)
        {
            // 非 Windows 平台或 API 不可用时忽略。
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
        }

        StopCore(wait: true);

        lock (_gate)
        {
            _disposed = true;
        }

        _signal.Dispose();
    }
}
