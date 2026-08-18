using Avalonia.Controls;
using Avalonia.Input;

namespace WorkTracker.Avalonia.Views;

/// <summary>
/// Simple modal message-box dialog. The <paramref name="buttons"/> argument picks the button
/// set; <see cref="Result"/> reports the affirmative choice (OK / Yes / Retry).
/// </summary>
public partial class MessageBoxWindow : Window
{
	public bool Result { get; private set; }

	// Required by Avalonia XAML loader (AVLN3001)
	public MessageBoxWindow() : this(string.Empty, string.Empty) { }

	public MessageBoxWindow(string title, string message, MessageBoxButtons buttons = MessageBoxButtons.Ok)
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

		OkPanel.IsVisible = buttons == MessageBoxButtons.Ok;
		YesNoPanel.IsVisible = buttons == MessageBoxButtons.YesNo;
		RetryPanel.IsVisible = buttons == MessageBoxButtons.RetryClose;

		OkButton.Click += (_, _) => { Result = true; Close(true); };
		YesButton.Click += (_, _) => { Result = true; Close(true); };
		NoButton.Click += (_, _) => { Result = false; Close(false); };
		RetryButton.Click += (_, _) => { Result = true; Close(true); };
		CloseAppButton.Click += (_, _) => { Result = false; Close(false); };
	}

	private void OnDragPointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
		{
			BeginMoveDrag(e);
		}
	}
}
