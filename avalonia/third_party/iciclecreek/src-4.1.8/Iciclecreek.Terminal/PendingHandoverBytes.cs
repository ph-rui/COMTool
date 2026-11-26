using System;
using System.Runtime.CompilerServices;
using Porta.Pty;

namespace Iciclecreek.Terminal;

internal static class PendingHandoverBytes
{
	private sealed class Box
	{
		public byte[]? Bytes;

		public bool OwnerAttached = true;
	}

	private static readonly ConditionalWeakTable<IPtyConnection, Box> Boxes = new ConditionalWeakTable<IPtyConnection, Box>();

	public static void NoteDetached(IPtyConnection connection)
	{
		Box orCreateValue = Boxes.GetOrCreateValue(connection);
		lock (orCreateValue)
		{
			orCreateValue.OwnerAttached = false;
		}
	}

	public static bool TryPark(IPtyConnection connection, ReadOnlySpan<byte> chunk)
	{
		Box orCreateValue = Boxes.GetOrCreateValue(connection);
		lock (orCreateValue)
		{
			if (orCreateValue.OwnerAttached)
			{
				return false;
			}
			if (orCreateValue.Bytes == null)
			{
				orCreateValue.Bytes = chunk.ToArray();
			}
			else
			{
				byte[] array = new byte[orCreateValue.Bytes.Length + chunk.Length];
				orCreateValue.Bytes.CopyTo(array, 0);
				chunk.CopyTo(array.AsSpan(orCreateValue.Bytes.Length));
				orCreateValue.Bytes = array;
			}
			return true;
		}
	}

	public static byte[]? ClaimOnAttach(IPtyConnection connection)
	{
		Box orCreateValue = Boxes.GetOrCreateValue(connection);
		lock (orCreateValue)
		{
			orCreateValue.OwnerAttached = true;
			byte[] bytes = orCreateValue.Bytes;
			orCreateValue.Bytes = null;
			return bytes;
		}
	}
}
