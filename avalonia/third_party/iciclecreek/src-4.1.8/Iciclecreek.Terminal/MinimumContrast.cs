using System;
using System.Collections.Generic;
using Avalonia.Media;

namespace Iciclecreek.Terminal;

internal sealed class MinimumContrast
{
	private readonly Dictionary<ulong, uint> _cache = new Dictionary<ulong, uint>();

	private double _ratio = 1.0;

	private const int MaxEntries = 1024;

	public bool Active => _ratio > 1.0;

	public void SnapshotRatio(double ratio)
	{
		ratio = (double.IsNaN(ratio) ? 1.0 : Math.Clamp(ratio, 1.0, 21.0));
		if (Math.Abs(ratio - _ratio) > double.Epsilon)
		{
			_ratio = ratio;
			_cache.Clear();
		}
	}

	public Color Apply(Color fg, Color bg)
	{
		if (!Active)
		{
			return fg;
		}
		ulong key = ((ulong)PackArgb(fg) << 24) | Pack(bg);
		if (_cache.TryGetValue(key, out var value))
		{
			return UnpackArgb(value);
		}
		Color color = Adjust(fg, bg, _ratio);
		color = Color.FromArgb(fg.A, color.R, color.G, color.B);
		if (_cache.Count >= 1024)
		{
			_cache.Clear();
		}
		_cache[key] = PackArgb(color);
		return color;
	}

	private static Color Adjust(Color fg, Color bg, double minRatio)
	{
		double l = Luminance(bg);
		if (ContrastRatio(Luminance(fg), l) >= minRatio)
		{
			return fg;
		}
		double num = ContrastRatio(1.0, l);
		double num2 = ContrastRatio(0.0, l);
		Color color = ((num >= num2) ? Colors.White : Colors.Black);
		if (((num >= num2) ? num : num2) < minRatio)
		{
			return color;
		}
		double num3 = 0.0;
		double num4 = 1.0;
		for (int i = 0; i < 8; i++)
		{
			double num5 = (num3 + num4) / 2.0;
			if (ContrastRatio(Luminance(Blend(fg, color, num5)), l) >= minRatio)
			{
				num4 = num5;
			}
			else
			{
				num3 = num5;
			}
		}
		return Blend(fg, color, num4);
	}

	internal static double Luminance(Color c)
	{
		return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
		static double Lin(byte v)
		{
			double num = (double)(int)v / 255.0;
			if (!(num <= 0.03928))
			{
				return Math.Pow((num + 0.055) / 1.055, 2.4);
			}
			return num / 12.92;
		}
	}

	internal static double ContrastRatio(double l1, double l2)
	{
		double num2;
		double num3;
		if (!(l1 >= l2))
		{
			double num = l1;
			num2 = num;
			num3 = l2;
		}
		else
		{
			double num = l2;
			num2 = num;
			num3 = l1;
		}
		return (num3 + 0.05) / (num2 + 0.05);
	}

	private static Color Blend(Color from, Color to, double t)
	{
		return Color.FromRgb((byte)((double)(int)from.R + (double)(to.R - from.R) * t), (byte)((double)(int)from.G + (double)(to.G - from.G) * t), (byte)((double)(int)from.B + (double)(to.B - from.B) * t));
	}

	private static uint Pack(Color c)
	{
		return (uint)((c.R << 16) | (c.G << 8) | c.B);
	}

	private static uint PackArgb(Color c)
	{
		return (uint)(c.A << 24) | Pack(c);
	}

	private static Color UnpackArgb(uint v)
	{
		return Color.FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
	}

	internal static bool IsExemptCodepoint(int cp)
	{
		if ((cp < 9472 || cp > 9631) && (cp < 9632 || cp > 9727))
		{
			if (cp >= 57520)
			{
				return cp <= 57535;
			}
			return false;
		}
		return true;
	}

	internal static bool IsExemptRun(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return false;
		}
		for (int i = 0; i < text.Length; i++)
		{
			int num = text[i];
			if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
			{
				num = char.ConvertToUtf32(text[i], text[i + 1]);
			}
			if (!IsExemptCodepoint(num) && !char.IsWhiteSpace((char)Math.Min(num, 65535)))
			{
				return false;
			}
		}
		return true;
	}
}
