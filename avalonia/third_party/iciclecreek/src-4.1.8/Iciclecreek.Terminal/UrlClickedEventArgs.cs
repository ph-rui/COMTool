using System;

namespace Iciclecreek.Terminal;

public class UrlClickedEventArgs : EventArgs
{
	public string Url { get; }

	public bool FromSequence { get; }

	public UrlClickedEventArgs(string url, bool fromSequence = false)
	{
		Url = url;
		FromSequence = fromSequence;
	}
}
