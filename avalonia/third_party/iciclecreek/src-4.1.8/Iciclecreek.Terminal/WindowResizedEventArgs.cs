using Avalonia.Interactivity;

namespace Iciclecreek.Terminal;

public class WindowResizedEventArgs : RoutedEventArgs
{
	public int Width { get; }

	public int Height { get; }

	public WindowResizedEventArgs(int width, int height)
	{
		Width = width;
		Height = height;
	}
}
