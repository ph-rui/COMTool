using System;
using System.Buffers;
using System.Collections.Generic;
using SkiaSharp;
using XTerm.Graphics;

namespace Iciclecreek.Terminal.Skia;

internal sealed class SkiaPictureCache
{
	private sealed class Entry
	{
		public required SharedSkImage Image;

		public int FrameSerial;

		public long LastFrame;
	}

	private sealed class StripEntry
	{
		public required SharedSkImage Image;

		public long Version;

		public long LastFrame;
	}

	private readonly Dictionary<TerminalImage, Entry> _entries = new Dictionary<TerminalImage, Entry>(ReferenceEqualityComparer.Instance);

	private readonly List<TerminalImage> _unused = new List<TerminalImage>();

	private readonly Dictionary<SixelStrip, StripEntry> _strips = new Dictionary<SixelStrip, StripEntry>(ReferenceEqualityComparer.Instance);

	private readonly List<SixelStrip> _unusedStrips = new List<SixelStrip>();

	public SharedSkImage? GetStrip(SixelStrip strip, long frame)
	{
		//IL_0095: Unknown result type (might be due to invalid IL or missing references)
		long version = strip.Version;
		if (_strips.TryGetValue(strip, out StripEntry value))
		{
			if (value.Version == version)
			{
				value.LastFrame = frame;
				return value.Image;
			}
			value.Image.Release();
			_strips.Remove(strip);
		}
		int num = Math.Max(1, strip.PaintedColumns);
		int num2 = num * strip.CellWidth * 4;
		int num3 = num2 * strip.PixelHeight;
		byte[] array = ArrayPool<byte>.Shared.Rent(num3);
		SKImage val;
		try
		{
			strip.CopyPixelsTo(array, num2, num);
			val = SKImage.FromPixelCopy(new SKImageInfo(num * strip.CellWidth, strip.PixelHeight, (SKColorType)6, (SKAlphaType)3), (ReadOnlySpan<byte>)array.AsSpan(0, num3), num2);
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(array);
		}
		if (val == null)
		{
			return null;
		}
		SharedSkImage sharedSkImage = new SharedSkImage(val);
		_strips[strip] = new StripEntry
		{
			Image = sharedSkImage,
			Version = ((strip.Version == version) ? version : (-1)),
			LastFrame = frame
		};
		return sharedSkImage;
	}

	public SharedSkImage? Get(TerminalImage image, long frame)
	{
		if (_entries.TryGetValue(image, out Entry value))
		{
			if (value.FrameSerial == image.FrameSerial)
			{
				value.LastFrame = frame;
				return value.Image;
			}
			value.Image.Release();
			_entries.Remove(image);
		}
		SharedSkImage sharedSkImage = Upload(image);
		if (sharedSkImage == null)
		{
			return null;
		}
		_entries[image] = new Entry
		{
			Image = sharedSkImage,
			FrameSerial = image.FrameSerial,
			LastFrame = frame
		};
		return sharedSkImage;
	}

	public void Clear()
	{
		foreach (Entry value in _entries.Values)
		{
			value.Image.Release();
		}
		_entries.Clear();
		foreach (StripEntry value2 in _strips.Values)
		{
			value2.Image.Release();
		}
		_strips.Clear();
	}

	public void Sweep(long frame)
	{
		foreach (var (item, entry2) in _entries)
		{
			if (entry2.LastFrame != frame)
			{
				_unused.Add(item);
			}
		}
		foreach (TerminalImage item3 in _unused)
		{
			_entries[item3].Image.Release();
			_entries.Remove(item3);
		}
		_unused.Clear();
		foreach (var (item2, stripEntry2) in _strips)
		{
			if (stripEntry2.LastFrame != frame)
			{
				_unusedStrips.Add(item2);
			}
		}
		foreach (SixelStrip unusedStrip in _unusedStrips)
		{
			_strips[unusedStrip].Image.Release();
			_strips.Remove(unusedStrip);
		}
		_unusedStrips.Clear();
	}

	private static SharedSkImage? Upload(TerminalImage image)
	{
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		if (image.PixelWidth <= 0 || image.PixelHeight <= 0)
		{
			return null;
		}
		SKImage val = SKImage.FromPixelCopy(new SKImageInfo(image.PixelWidth, image.PixelHeight, (SKColorType)6, (SKAlphaType)3), image.CurrentPixels.Span, image.Stride);
		if (val != null)
		{
			return new SharedSkImage(val);
		}
		return null;
	}
}
