using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace Iciclecreek.Terminal;

internal static class LigatureProbe
{
	private const int First = 32;

	private const int Last = 126;

	private static readonly ConcurrentDictionary<GlyphTypeface, bool[]?> _alphabets = new ConcurrentDictionary<GlyphTypeface, bool[]>();

	private static readonly ConcurrentDictionary<GlyphTypeface, List<Action<bool[]?>>> _waiters = new ConcurrentDictionary<GlyphTypeface, List<Action<bool[]>>>();

	internal static Func<GlyphTypeface, bool[]?>? ProbeOverride;

	internal static void ResetForTests()
	{
		_alphabets.Clear();
		_waiters.Clear();
	}

	public static bool TryGetAlphabet(Typeface typeface, Action<bool[]?> whenKnown, out bool[]? alphabet)
	{
		GlyphTypeface glyphTypeface;
		try
		{
			glyphTypeface = typeface.GlyphTypeface;
		}
		catch
		{
			alphabet = null;
			return true;
		}
		if (_alphabets.TryGetValue(glyphTypeface, out alphabet))
		{
			return true;
		}
		List<Action<bool[]?>> waiters = _waiters.GetOrAdd(glyphTypeface, (GlyphTypeface _) => new List<Action<bool[]>>());
		bool flag = false;
		lock (waiters)
		{
			if (_alphabets.TryGetValue(glyphTypeface, out alphabet))
			{
				return true;
			}
			flag = waiters.Count == 0;
			if (!waiters.Contains(whenKnown))
			{
				waiters.Add(whenKnown);
			}
		}
		if (flag)
		{
			GlyphTypeface face = glyphTypeface;
			Task.Run(() =>
			{
				bool[] array = ProbeOverride?.Invoke(face) ?? Probe(face);
				Action<bool[]>[] array2;
				lock (waiters)
				{
					_alphabets[face] = array;
					_waiters.TryRemove(face, out List<Action<bool[]>> _);
					array2 = waiters.ToArray();
					waiters.Clear();
				}
				if (array != null)
				{
					Action<bool[]>[] array3 = array2;
					for (int i = 0; i < array3.Length; i++)
					{
						array3[i](array);
					}
				}
			});
		}
		alphabet = null;
		return false;
	}

	private static bool[]? Probe(GlyphTypeface face)
	{
		try
		{
			TextShaperOptions options = new TextShaperOptions(face, 16.0, 0);
			TextShaper current = TextShaper.Current;
			ushort[] array = new ushort[127];
			for (int i = 32; i <= 126; i++)
			{
				ShapedBuffer shapedBuffer = current.ShapeText(((char)i).ToString().AsMemory(), options);
				if (shapedBuffer.Length != 1)
				{
					return null;
				}
				array[i] = shapedBuffer[0].GlyphIndex;
			}
			bool[] array2 = new bool[127];
			bool flag = false;
			for (int j = 32; j <= 126; j++)
			{
				for (int k = 32; k <= 126; k++)
				{
					ShapedBuffer shapedBuffer2 = current.ShapeText($"{(char)(ushort)j}{(char)(ushort)k}".AsMemory(), options);
					if (shapedBuffer2.Length != 2)
					{
						int num = j;
						bool flag2;
						array2[k] = (flag2 = (flag = true));
						array2[num] = flag2;
						continue;
					}
					if (shapedBuffer2[0].GlyphIndex != array[j])
					{
						flag = (array2[j] = true);
					}
					if (shapedBuffer2[1].GlyphIndex != array[k])
					{
						flag = (array2[k] = true);
					}
				}
			}
			for (int l = 32; l <= 126; l++)
			{
				if (!array2[l])
				{
					ShapedBuffer shapedBuffer3 = current.ShapeText(new string((char)l, 3).AsMemory(), options);
					if (shapedBuffer3.Length != 3 || shapedBuffer3[0].GlyphIndex != array[l] || shapedBuffer3[1].GlyphIndex != array[l] || shapedBuffer3[2].GlyphIndex != array[l])
					{
						flag = (array2[l] = true);
					}
				}
			}
			return flag ? array2 : null;
		}
		catch
		{
			return null;
		}
	}

	public static bool ContainsCandidate(string text, bool[] alphabet)
	{
		foreach (char c in text)
		{
			if (c < alphabet.Length && alphabet[(uint)c])
			{
				return true;
			}
		}
		return false;
	}
}
