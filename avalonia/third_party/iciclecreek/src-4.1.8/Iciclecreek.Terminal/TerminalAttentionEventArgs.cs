using Avalonia.Interactivity;

namespace Iciclecreek.Terminal;

public class TerminalAttentionEventArgs : RoutedEventArgs
{
	public string Action { get; }

	public TerminalAttentionEventArgs(RoutedEvent routedEvent, string action)
		: base(routedEvent)
	{
		Action = action;
	}
}
