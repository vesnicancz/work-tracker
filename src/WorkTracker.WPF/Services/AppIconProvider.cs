using System.Windows.Media;
using System.Windows.Media.Imaging;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.WPF.Services;

public static class AppIconProvider
{
	private static readonly string IconDirectory =
		System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources");

	public static ImageSource? GetIcon(bool isActive)
	{
		var path = AppIconResolver.GetIconPath(isActive, IconDirectory);
		return path != null ? new BitmapImage(new Uri(path)) : null;
	}
}
