using System;

namespace Iciclecreek.Terminal;

public sealed class TerminalMessageEventArgs : EventArgs
{
	private string _text;

	public TerminalMessageKind Kind { get; }

	public Exception? Exception { get; }

	public int? ExitCode { get; }

	public long SessionId { get; }

	public string DefaultText { get; }

	public string Text
	{
		get
		{
			return _text;
		}
		set
		{
			_text = value ?? string.Empty;
		}
	}

	public bool Handled { get; set; }

	private TerminalMessageEventArgs(TerminalMessageKind kind, string defaultText, Exception? exception, int? exitCode, long sessionId)
	{
		Kind = kind;
		DefaultText = defaultText;
		_text = defaultText;
		Exception = exception;
		ExitCode = exitCode;
		SessionId = sessionId;
	}

	public static TerminalMessageEventArgs ReadError(Exception exception, long sessionId = 0L)
	{
		return new TerminalMessageEventArgs(TerminalMessageKind.ReadError, "\nError reading from process: " + exception.Message + "\n", exception, null, sessionId);
	}

	public static TerminalMessageEventArgs ProcessExited(int? exitCode, long sessionId = 0L)
	{
		string defaultText;
		if (exitCode.HasValue)
		{
			int valueOrDefault = exitCode.GetValueOrDefault();
			defaultText = $"\nProcess exited with code: {valueOrDefault}\n";
		}
		else
		{
			defaultText = "\nProcess exited\n";
		}
		return new TerminalMessageEventArgs(TerminalMessageKind.ProcessExited, defaultText, null, exitCode, sessionId);
	}
}
