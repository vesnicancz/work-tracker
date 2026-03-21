using Avalonia.Controls;
using Avalonia.Input;

namespace WorkTracker.Avalonia.Views;

/// <summary>
/// Simple modal message-box dialog.
/// Pass <c>isConfirmation: true</c> to show Yes/No buttons; otherwise an OK button is shown.
/// </summary>
public partial class MessageBoxWindow : Window
{
	public bool Result { get; private set; }

	// Required by Avalonia XAML loader (AVLN3001)
	public MessageBoxWindow() : this(string.Empty, string.Empty) { }

	public MessageBoxWindow(string title, string message, bool isConfirmation = false)
	{
		InitializeComponent();

		Title = title;
		DialogTitleText.Text = title;
		MessageText.Text = message;

		CloseButton.Click += (_, _) => Close(false);
		DialogTitleBar.PointerPressed += OnDragPointerPressed;
		DialogBorder.PointerPressed += (_, e) =>
		{
			if (!DialogTitleBar.IsVisible)
			{
				OnDragPointerPressed(null, e);
			}
		};

		if (isConfirmation)
		{
			OkPanel.IsVisible = false;
			YesNoPanel.IsVisible = true;
			YesButton.Click += (_, _) => { Result = true; Close(true); };
			NoButton.Click += (_, _) => { Result = false; Close(false); };
		}
		else
		{
			OkPanel.IsVisible = true;
			YesNoPanel.IsVisible = false;
			OkButton.Click += (_, _) => { Result = true; Close(true); };
		}
	}

	private void OnDragPointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
		{
			BeginMoveDrag(e);
		}
	}
}
