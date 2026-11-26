using Avalonia.Interactivity;
using XTerm.Events;

namespace Iciclecreek.Terminal;

public class TerminalNotificationEventArgs : RoutedEventArgs
{
	public TerminalEvents.NotificationEventArgs Notification { get; }

	public TerminalNotificationEventArgs(RoutedEvent routedEvent, TerminalEvents.NotificationEventArgs notification)
		: base(routedEvent)
	{
		Notification = notification;
	}
}
