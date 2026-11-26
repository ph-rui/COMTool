using System;
using XTerm.Buffer;

namespace Iciclecreek.Terminal;

internal sealed class CapturedFrame
{
	public BufferLine?[] Lines = Array.Empty<BufferLine>();

	public int StartLine;

	public int Cols;

	public long Generation;

	public BufferLine? LineAt(int absolute)
	{
		int num = absolute - StartLine;
		if (num < 0 || num >= Lines.Length)
		{
			return null;
		}
		return Lines[num];
	}
}
