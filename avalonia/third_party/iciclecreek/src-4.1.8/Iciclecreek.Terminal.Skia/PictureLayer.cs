using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace Iciclecreek.Terminal.Skia;

internal sealed class PictureLayer : ICustomDrawOperation, IEquatable<ICustomDrawOperation>, IDisposable
{
	private readonly List<PictureDraw> _draws;

	private int _released;

	private int _unsupported;

	public Rect Bounds { get; }

	public bool Unsupported => Volatile.Read(in _unsupported) != 0;

	public PictureLayer(List<PictureDraw> draws, Rect bounds)
	{
		_draws = draws;
		Bounds = bounds;
	}

	public bool HitTest(Point p)
	{
		return false;
	}

	public bool Equals(ICustomDrawOperation? other)
	{
		return false;
	}

	public void Render(ImmediateDrawingContext context)
	{
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_0083: Unknown result type (might be due to invalid IL or missing references)
		//IL_0088: Unknown result type (might be due to invalid IL or missing references)
		//IL_0092: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ac: Unknown result type (might be due to invalid IL or missing references)
		if (Volatile.Read(in _released) != 0)
		{
			return;
		}
		ISkiaSharpApiLeaseFeature skiaSharpApiLeaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
		if (skiaSharpApiLeaseFeature == null)
		{
			Volatile.Write(ref _unsupported, 1);
			return;
		}
		using ISkiaSharpApiLease skiaSharpApiLease = skiaSharpApiLeaseFeature.Lease();
		SKCanvas skCanvas = skiaSharpApiLease.SkCanvas;
		GRContext grContext = skiaSharpApiLease.GrContext;
		SharedSkImage.DisposePendingTextures();
		SKSamplingOptions val = new SKSamplingOptions((SKFilterMode)1, (SKMipmapMode)0);
		foreach (PictureDraw draw in _draws)
		{
			SKImage val2 = draw.Image.ForDrawing(grContext);
			SKRect? clip = draw.Clip;
			if (clip.HasValue)
			{
				SKRect valueOrDefault = clip.GetValueOrDefault();
				skCanvas.Save();
				skCanvas.ClipRect(valueOrDefault, (SKClipOperation)1, false);
				skCanvas.DrawImage(val2, draw.Source, draw.Destination, val, (SKPaint)null);
				skCanvas.Restore();
			}
			else
			{
				skCanvas.DrawImage(val2, draw.Source, draw.Destination, val, (SKPaint)null);
			}
		}
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _released, 1) != 0)
		{
			return;
		}
		foreach (PictureDraw draw in _draws)
		{
			draw.Image.Release();
		}
	}
}
