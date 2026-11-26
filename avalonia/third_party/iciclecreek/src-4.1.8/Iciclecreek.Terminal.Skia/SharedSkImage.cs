using System.Collections.Concurrent;
using System.Threading;
using SkiaSharp;

namespace Iciclecreek.Terminal.Skia;

internal sealed class SharedSkImage
{
	private int _refs = 1;

	private SKImage? _texture;

	private GRContext? _textureContext;

	internal static readonly ConcurrentQueue<SKImage> PendingTextureDisposal = new ConcurrentQueue<SKImage>();

	public SKImage Image { get; }

	public SharedSkImage(SKImage image)
	{
		Image = image;
	}

	public SKImage ForDrawing(GRContext? gpu)
	{
		if (gpu == null || Volatile.Read(in _refs) <= 0)
		{
			return Image;
		}
		if (_texture != null && _textureContext == gpu)
		{
			return _texture;
		}
		if (_texture != null)
		{
			PendingTextureDisposal.Enqueue(_texture);
		}
		_texture = Image.ToTextureImage(gpu);
		_textureContext = gpu;
		return _texture ?? Image;
	}

	internal static void DisposePendingTextures()
	{
		SKImage result;
		while (PendingTextureDisposal.TryDequeue(out result))
		{
			((SKNativeObject)result).Dispose();
		}
	}

	public SharedSkImage AddRef()
	{
		Interlocked.Increment(ref _refs);
		return this;
	}

	public void Release()
	{
		if (Interlocked.Decrement(ref _refs) == 0)
		{
			((SKNativeObject)Image).Dispose();
			if (_texture != null)
			{
				PendingTextureDisposal.Enqueue(_texture);
			}
			_texture = null;
		}
	}
}
