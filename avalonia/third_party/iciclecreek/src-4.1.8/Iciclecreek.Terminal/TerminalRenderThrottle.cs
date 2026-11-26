using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Iciclecreek.Terminal;

public static class TerminalRenderThrottle
{
	private const int DefaultFrameRate = 30;

	private const int MaxFrameRate = 1000;

	private static int _targetFrameRate = 30;

	private static readonly HashSet<Control> Pending = new HashSet<Control>();

	private static bool _frameScheduled;

	private static DateTime _lastFrame = DateTime.MinValue;

	public static int TargetFrameRate
	{
		get
		{
			return Volatile.Read(in _targetFrameRate);
		}
		set
		{
			if (value < 1 || value > 1000)
			{
				throw new ArgumentOutOfRangeException("value", value, $"Frame rate must be between 1 and {1000} frames per second.");
			}
			Volatile.Write(ref _targetFrameRate, value);
		}
	}

	private static TimeSpan FrameInterval => TimeSpan.FromSeconds(1.0 / (double)Volatile.Read(in _targetFrameRate));

	public static void RequestInvalidate(this Control control)
	{
		if (control == null)
		{
			return;
		}
		bool flag;
		lock (Pending)
		{
			Pending.Add(control);
			flag = !_frameScheduled;
			if (flag)
			{
				_frameScheduled = true;
			}
		}
		if (flag)
		{
			ScheduleFrame();
		}
	}

	private static void ScheduleFrame()
	{
		TimeSpan frameInterval = FrameInterval;
		TimeSpan timeSpan = DateTime.UtcNow - _lastFrame;
		if (timeSpan >= frameInterval)
		{
			Dispatcher.UIThread.Post(Flush);
			return;
		}
		TimeSpan delay = frameInterval - timeSpan;
		Dispatcher.UIThread.Post(async () =>
		{
			await Task.Delay(delay);
			Flush();
		});
	}

	private static void Flush()
	{
		Control[] array;
		lock (Pending)
		{
			_frameScheduled = false;
			_lastFrame = DateTime.UtcNow;
			if (Pending.Count == 0)
			{
				return;
			}
			array = new Control[Pending.Count];
			Pending.CopyTo(array);
			Pending.Clear();
		}
		Control[] array2 = array;
		for (int i = 0; i < array2.Length; i++)
		{
			array2[i].InvalidateVisual();
		}
	}
}
