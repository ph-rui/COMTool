using System.Diagnostics;
using System.Threading;
using XTerm;
using XTerm.Buffer;

namespace Iciclecreek.Terminal;

internal sealed class FrameCapturePool
{
	private static readonly long ChunkPublishIntervalTicks = Stopwatch.Frequency / 30;

	private readonly CapturedFrame[] _slots = new CapturedFrame[3]
	{
		new CapturedFrame(),
		new CapturedFrame(),
		new CapturedFrame()
	};

	private CapturedFrame? _published;

	private CapturedFrame? _pinned;

	private long _lastChunkPublish;

	public void Publish(XTerm.Terminal terminal, long generation)
	{
		CapturedFrame capturedFrame = FreeSlot();
		TerminalBuffer buffer = terminal.Buffer;
		int rows = terminal.Rows;
		int cols = terminal.Cols;
		int viewportY = buffer.ViewportY;
		if (capturedFrame.Lines.Length != rows)
		{
			capturedFrame.Lines = new BufferLine[rows];
		}
		for (int i = 0; i < rows; i++)
		{
			int num = viewportY + i;
			BufferLine bufferLine = ((num >= 0 && num < buffer.Length) ? buffer.GetLine(num) : null);
			if (bufferLine == null || bufferLine.HasSizedRuns || bufferLine.LineAttribute != LineAttribute.Normal)
			{
				capturedFrame.Lines[i] = null;
				continue;
			}
			BufferLine bufferLine2 = capturedFrame.Lines[i];
			if (bufferLine2 == null)
			{
				bufferLine2 = (capturedFrame.Lines[i] = new BufferLine(cols));
			}
			bufferLine2.CopyFrom(bufferLine);
			if (bufferLine.HasImages && !bufferLine2.HasImages)
			{
				capturedFrame.Lines[i] = null;
			}
		}
		capturedFrame.StartLine = viewportY;
		capturedFrame.Cols = cols;
		capturedFrame.Generation = generation;
		Volatile.Write(ref _published, capturedFrame);
	}

	public void PublishThrottled(XTerm.Terminal terminal, long generation)
	{
		long timestamp = Stopwatch.GetTimestamp();
		if (timestamp - _lastChunkPublish >= ChunkPublishIntervalTicks)
		{
			_lastChunkPublish = timestamp;
			Publish(terminal, generation);
		}
	}

	public CapturedFrame? PinForRender(int startLine, int cols, int rows, long liveGeneration, bool atomicUpdate, bool writeInProgress = false)
	{
		CapturedFrame capturedFrame;
		do
		{
			capturedFrame = Volatile.Read(in _published);
			Volatile.Write(ref _pinned, capturedFrame);
		}
		while (Volatile.Read(in _published) != capturedFrame);
		if (capturedFrame == null)
		{
			return null;
		}
		if (capturedFrame.Cols != cols || capturedFrame.StartLine != startLine || capturedFrame.Lines.Length < rows)
		{
			return null;
		}
		if (capturedFrame.Generation != liveGeneration && !atomicUpdate && !writeInProgress)
		{
			return null;
		}
		return capturedFrame;
	}

	public void InvalidateAll()
	{
		Volatile.Write(ref _published, null);
	}

	private CapturedFrame FreeSlot()
	{
		CapturedFrame capturedFrame = Volatile.Read(in _published);
		CapturedFrame capturedFrame2 = Volatile.Read(in _pinned);
		CapturedFrame[] slots = _slots;
		foreach (CapturedFrame capturedFrame3 in slots)
		{
			if (capturedFrame3 != capturedFrame && capturedFrame3 != capturedFrame2)
			{
				return capturedFrame3;
			}
		}
		return _slots[0];
	}
}
