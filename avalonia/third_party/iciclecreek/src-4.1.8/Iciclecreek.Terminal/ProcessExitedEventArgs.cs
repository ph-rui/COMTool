using System;

namespace Iciclecreek.Terminal;

public class ProcessExitedEventArgs : EventArgs
{
	public int ExitCode { get; }

	public bool ExitCodeKnown { get; }

	public long SessionId { get; init; }

	public ProcessExitedEventArgs(int exitCode)
	{
		ExitCode = exitCode;
		ExitCodeKnown = true;
	}

	private ProcessExitedEventArgs()
	{
		ExitCode = 0;
		ExitCodeKnown = false;
	}

	public static ProcessExitedEventArgs UnknownCode()
	{
		return new ProcessExitedEventArgs();
	}

	public static ProcessExitedEventArgs UnknownCode(long sessionId)
	{
		return new ProcessExitedEventArgs
		{
			SessionId = sessionId
		};
	}
}
