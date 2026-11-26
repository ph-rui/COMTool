using System;
using System.Text;

namespace Iciclecreek.Terminal;

public class OutputReceivedEventArgs : EventArgs
{
	private readonly ReadOnlyMemory<byte> _bytes;

	private string? _output;

	public ReadOnlyMemory<byte> Bytes => _bytes;

	public string Output => _output ?? (_output = Encoding.UTF8.GetString(_bytes.Span));

	public long SessionId { get; init; }

	public OutputReceivedEventArgs(ReadOnlyMemory<byte> bytes)
	{
		_bytes = bytes;
	}

	public OutputReceivedEventArgs(string output)
	{
		_output = output;
		_bytes = Encoding.UTF8.GetBytes(output);
	}
}
