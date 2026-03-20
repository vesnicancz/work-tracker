using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using MaterialDesignThemes.Wpf;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.WPF.Services;

/// <summary>
/// Implementation of INotificationService using Material Design Snackbar
/// </summary>
public sealed class NotificationService : INotificationService
{
	private readonly ISnackbarMessageQueue _messageQueue;

	public NotificationService(ISnackbarMessageQueue messageQueue)
	{
		_messageQueue = messageQueue;
	}

	public void ShowSuccess(string message)
	{
		ShowNotification(message, PackIconKind.CheckCircle, new SolidColorBrush(Color.FromRgb(76, 175, 80))); // Green
	}

	public void ShowInformation(string message)
	{
		ShowNotification(message, PackIconKind.Information, new SolidColorBrush(Color.FromRgb(33, 150, 243))); // Blue
	}

	public void ShowWarning(string message)
	{
		ShowNotification(message, PackIconKind.AlertCircle, new SolidColorBrush(Color.FromRgb(255, 152, 0))); // Orange
	}

	public void ShowError(string message)
	{
		ShowNotification(message, PackIconKind.CloseCircle, new SolidColorBrush(Color.FromRgb(244, 67, 54))); // Red
	}

	private void ShowNotification(string message, PackIconKind icon, Brush backgroundColor)
	{
		// Execute on UI thread
		System.Windows.Application.Current?.Dispatcher.Invoke(() =>
		{
			// Create custom content with icon and message
			var border = new Border
			{
				Background = backgroundColor,
				CornerRadius = new CornerRadius(4),
				Padding = new Thickness(16, 12, 16, 12),
				Effect = new DropShadowEffect
				{
					Color = Colors.Black,
					Opacity = 0.3,
					BlurRadius = 10,
					ShadowDepth = 2
				},
				Child = new StackPanel
				{
					Orientation = Orientation.Horizontal,
					Children =
					{
						new PackIcon
						{
							Kind = icon,
							Width = 20,
							Height = 20,
							VerticalAlignment = VerticalAlignment.Center,
							Margin = new Thickness(0, 0, 12, 0),
							Foreground = Brushes.White
						},
						new TextBlock
						{
							Text = message,
							VerticalAlignment = VerticalAlignment.Center,
							Foreground = Brushes.White,
							FontSize = 14,
							FontWeight = FontWeights.Medium,
							TextWrapping = TextWrapping.Wrap,
							MaxWidth = 400
						}
					}
				}
			};

			_messageQueue.Enqueue(
				border,
				null,
				null,
				null,
				false,
				true,
				TimeSpan.FromSeconds(3));
		});
	}
}