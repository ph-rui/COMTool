using Avalonia.Interactivity;
using XTerm.Common;

namespace Iciclecreek.Terminal;

public class WindowInfoRequestedEventArgs : RoutedEventArgs
{
	public WindowInfoRequest Request { get; }

	public bool IsIconified { get; set; }

	public int X { get; set; }

	public int Y { get; set; }

	public int WidthPixels { get; set; }

	public int HeightPixels { get; set; }

	public int CellWidth { get; set; }

	public int CellHeight { get; set; }

	public string? Title { get; set; }

	public WindowInfoRequestedEventArgs(WindowInfoRequest request)
	{
		Request = request;
	}
}
