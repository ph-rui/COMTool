using Avalonia.Interactivity;

namespace Iciclecreek.Terminal;

public class TitleChangedEventArgs : RoutedEventArgs
{
	public string Title { get; }

	public TitleChangedEventArgs(string title)
	{
		Title = title;
	}
}
