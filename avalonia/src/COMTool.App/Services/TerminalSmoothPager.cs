using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Iciclecreek.Terminal;

namespace COMTool.App.Services;

/// <summary>
/// r67.8：终端滚动条「按住轨道空白区」的平滑翻页。
/// </summary>
/// <remarks>
/// <para>r67.7 让库模板里的翻页按钮按 MobaXterm 的 300/250 节奏重复，但每次重复是
/// <c>LargeChange = 一整屏</c>（22 行）——实机是 4 次/秒的整屏跳动，用户反馈“一顿一顿”。
/// 这里接管这两个翻页按钮的按下手势：</para>
/// <list type="bullet">
/// <item>按下立即翻一整屏（保留 MobaXterm 单击语义，也保留 <c>LargeChange</c> 供自动化/无障碍使用）；</item>
/// <item>按住超过 <see cref="HoldDelayMs"/> 后进入连续滚动：每 <see cref="StepIntervalMs"/>
/// 滚 <see cref="LinesPerStep"/> 行（约 60 行/秒），肉眼看到的是逐行推进而不是整屏跳；</item>
/// <item>松开、指针捕获丢失、终端离开可视树都会立即停住。</item>
/// </list>
/// <para>只接管 <c>PART_PageUpButton</c>/<c>PART_PageDownButton</c>（轨道空白区）；滑块拖动、
/// 上下箭头按钮、单击翻页的取值全部保持原样。滚动直接写
/// <see cref="TerminalControl.ViewportY"/>，库会在属性变化时把 <c>ScrollBar.Value</c>
/// 同步回来，滑块因此仍然跟手。</para>
/// </remarks>
internal sealed class TerminalSmoothPager : IDisposable
{
    /// <summary>按住多久后从「翻一屏」切到「连续逐行」（毫秒）。</summary>
    internal const int HoldDelayMs = 350;

    /// <summary>连续滚动的时间步长（毫秒）：16ms ≈ 60fps。</summary>
    internal const int StepIntervalMs = 16;

    /// <summary>每个时间步滚动的行数。1 行/16ms ≈ 60 行/秒。</summary>
    internal const int LinesPerStep = 1;

    private static readonly ConditionalWeakTable<ScrollBar, TerminalSmoothPager> Instances = new();

    private readonly TerminalControl _terminal;
    private readonly ScrollBar _bar;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _watch = new();
    private readonly EventHandler<PointerPressedEventArgs> _pressedHandler;
    private readonly EventHandler<PointerReleasedEventArgs> _releasedHandler;
    private readonly EventHandler<PointerCaptureLostEventArgs> _captureLostHandler;
    private TimeSpan _lastTick;
    private double _holdMs;
    private double _stepMs;
    private int _direction;
    private bool _disposed;

    private TerminalSmoothPager(TerminalControl terminal, ScrollBar bar)
    {
        _terminal = terminal;
        _bar = bar;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(StepIntervalMs) };
        _timer.Tick += OnTimerTick;
        _pressedHandler = OnPointerPressed;
        _releasedHandler = OnPointerReleased;
        _captureLostHandler = OnPointerCaptureLost;
    }

    /// <summary>给一根滚动条装配平滑翻页；重复调用返回同一实例。</summary>
    internal static TerminalSmoothPager Attach(TerminalControl terminal, ScrollBar bar)
    {
        if (Instances.TryGetValue(bar, out var existing))
        {
            return existing;
        }

        var pager = new TerminalSmoothPager(terminal, bar);
        Instances.Add(bar, pager);
        pager.Subscribe();
        return pager;
    }

    internal static TerminalSmoothPager? ForTest(ScrollBar bar) =>
        Instances.TryGetValue(bar, out var pager) ? pager : null;

    /// <summary>当前是否处于按住状态（-1 向上 / +1 向下）。</summary>
    internal int Direction => _direction;

    internal void Press(int direction)
    {
        if (_disposed || direction == 0)
        {
            return;
        }

        _direction = direction;
        _holdMs = 0;
        _stepMs = 0;
        _lastTick = TimeSpan.Zero;
        _watch.Restart();
        ScrollLines(direction * PageLines());
        _timer.Start();
    }

    /// <summary>
    /// 推进时间轴。真实运行时由 <see cref="DispatcherTimer"/> 按帧调用；headless 测试里
    /// 时器不触发，测试直接调用本方法验证「350ms 后每 16ms 一行」的节奏。
    /// </summary>
    internal void Advance(TimeSpan delta)
    {
        if (_direction == 0)
        {
            return;
        }

        var remainingMs = delta.TotalMilliseconds;
        if (remainingMs <= 0)
        {
            return;
        }

        if (_holdMs < HoldDelayMs)
        {
            var consumed = Math.Min(remainingMs, HoldDelayMs - _holdMs);
            _holdMs += consumed;
            remainingMs -= consumed;
            if (remainingMs <= 0)
            {
                return;
            }
        }

        _stepMs += remainingMs;
        var steps = (int)(_stepMs / StepIntervalMs);
        if (steps <= 0)
        {
            return;
        }

        _stepMs -= steps * StepIntervalMs;
        ScrollLines(_direction * steps * LinesPerStep);
    }

    internal void Release()
    {
        _direction = 0;
        _timer.Stop();
        _watch.Reset();
        _holdMs = 0;
        _stepMs = 0;
        _lastTick = TimeSpan.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Release();
        _timer.Tick -= OnTimerTick;
        _bar.RemoveHandler(InputElement.PointerPressedEvent, _pressedHandler);
        _bar.RemoveHandler(InputElement.PointerReleasedEvent, _releasedHandler);
        _bar.RemoveHandler(InputElement.PointerCaptureLostEvent, _captureLostHandler);
    }

    private void Subscribe()
    {
        // 隧道阶段先于 RepeatButton 自己的按下逻辑，命中翻页区就整段接管。
        _bar.AddHandler(InputElement.PointerPressedEvent, _pressedHandler, RoutingStrategies.Tunnel);
        _bar.AddHandler(InputElement.PointerReleasedEvent, _releasedHandler, RoutingStrategies.Tunnel);
        _bar.AddHandler(InputElement.PointerCaptureLostEvent, _captureLostHandler, RoutingStrategies.Tunnel);
        // 标签切换/关窗时终端会离开可视树：按住状态必须停，否则定时器会继续改 ViewportY。
        _bar.DetachedFromVisualTree += (_, _) => Release();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_bar).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var direction = DirectionFrom(e.Source as Visual);
        if (direction == 0)
        {
            return;
        }

        // 拦下 RepeatButton：它自己每 250ms 跳一整屏，正是“一顿一顿”的来源。
        e.Handled = true;
        e.Pointer.Capture(_bar);
        Press(direction);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_direction == 0)
        {
            return;
        }

        if (e.Pointer.Captured == _bar)
        {
            e.Pointer.Capture(null);
        }

        Release();
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => Release();

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_disposed || _direction == 0)
        {
            _timer.Stop();
            return;
        }

        var now = _watch.Elapsed;
        var delta = now - _lastTick;
        _lastTick = now;
        Advance(delta);
    }

    private static int DirectionFrom(Visual? source)
    {
        if (source is null)
        {
            return 0;
        }

        var button = source.FindAncestorOfType<RepeatButton>(includeSelf: true);
        return button?.Name switch
        {
            "PART_PageUpButton" => -1,
            "PART_PageDownButton" => 1,
            _ => 0,
        };
    }

    private int PageLines()
    {
        var lines = _bar.ViewportSize > 0 ? _bar.ViewportSize : _terminal.ViewportLines;
        return Math.Max(1, (int)Math.Round(lines));
    }

    private void ScrollLines(int lines)
    {
        if (lines == 0)
        {
            return;
        }

        var maximum = Math.Max(0, _terminal.MaxScrollback);
        var target = Math.Clamp(_terminal.ViewportY + lines, 0, maximum);
        if (target != _terminal.ViewportY)
        {
            _terminal.ViewportY = target;
        }
    }
}
