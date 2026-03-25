using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Material.Icons;
using Material.Icons.Avalonia;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.Avalonia.Services;

/// <summary>
/// Implementation of INotificationService using a custom overlay panel.
/// The overlay must be a Panel named "NotificationHost" in MainWindow.
/// Notifications auto-dismiss after 3 seconds.
/// Uses theme-aware brushes from the current resource dictionary.
/// </summary>
public sealed class NotificationService : INotificationService
{
	public void ShowSuccess(string message) =>
		ShowNotification(message, MaterialIconKind.CheckCircle, GetThemeBrush("SuccessBrush"));

	public void ShowInformation(string message) =>
		ShowNotification(message, MaterialIconKind.Information, GetThemeBrush("InfoBrush"));

	public void ShowWarning(string message) =>
		ShowNotification(message, MaterialIconKind.AlertCircle, GetThemeBrush("WarningBrush"));

	public void ShowError(string message) =>
		ShowNotification(message, MaterialIconKind.CloseCircle, GetThemeBrush("ErrorBrush"));

	private static IBrush GetThemeBrush(string key) =>
		global::Avalonia.Application.Current?.FindResource(key) is IBrush brush
			? brush
			: new SolidColorBrush(Colors.Gray);

	private void ShowNotification(string message, MaterialIconKind icon, IBrush backgroundBrush)
	{
		Dispatcher.UIThread.Post(() =>
		{
			var host = FindNotificationHost();
			if (host == null)
			{
				return;
			}

			var border = new Border
			{
				Background = backgroundBrush,
				CornerRadius = new CornerRadius(4),
				Padding = new Thickness(16, 12),
				Margin = new Thickness(0, 4),
				HorizontalAlignment = HorizontalAlignment.Center,
				BoxShadow = new BoxShadows(new BoxShadow
				{
					OffsetX = 0,
					OffsetY = 2,
					Blur = 10,
					Color = Color.Parse("#40000000")
				}),
				Child = new StackPanel
				{
					Orientation = global::Avalonia.Layout.Orientation.Horizontal,
					Spacing = 12,
					Children =
					{
						new MaterialIcon
						{
							Kind = icon,
							Width = 20,
							Height = 20,
							Foreground = Brushes.White,
							VerticalAlignment = VerticalAlignment.Center
						},
						new TextBlock
						{
							Text = message,
							Foreground = Brushes.White,
							FontSize = 14,
							FontWeight = FontWeight.Medium,
							TextWrapping = TextWrapping.Wrap,
							MaxWidth = 400,
							VerticalAlignment = VerticalAlignment.Center
						}
					}
				}
			};

			host.Children.Add(border);

			// Auto-dismiss after 3 seconds
			var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
			timer.Tick += (_, _) =>
			{
				timer.Stop();
				host.Children.Remove(border);
			};
			timer.Start();
		});
	}

	private static Panel? FindNotificationHost()
	{
		if (global::Avalonia.Application.Current?.ApplicationLifetime
			is IClassicDesktopStyleApplicationLifetime desktop)
		{
			return desktop.MainWindow?.FindControl<Panel>("NotificationHost");
		}
		return null;
	}
}
