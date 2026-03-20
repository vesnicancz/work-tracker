using Avalonia.Controls;

namespace WorkTracker.Avalonia.Views;

/// <summary>
/// Simple modal message-box dialog.
/// Pass <c>isConfirmation: true</c> to show Yes/No buttons; otherwise an OK button is shown.
/// </summary>
public partial class MessageBoxWindow : Window
{
    public bool Result { get; private set; }

    public MessageBoxWindow(string title, string message, bool isConfirmation = false)
    {
        InitializeComponent();

        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;

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
}
