using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WorkTracker.WPF.ViewModels;

/// <summary>
/// Base class for all ViewModels providing INotifyPropertyChanged implementation
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
	public event PropertyChangedEventHandler? PropertyChanged;

	/// <summary>
	/// Raises PropertyChanged event for a property
	/// </summary>
	/// <param name="propertyName">Name of the property that changed (auto-filled by compiler)</param>
	protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}

	/// <summary>
	/// Sets field value and raises PropertyChanged if value changed
	/// </summary>
	/// <typeparam name="T">Type of the property</typeparam>
	/// <param name="field">Reference to the backing field</param>
	/// <param name="value">New value</param>
	/// <param name="propertyName">Name of the property (auto-filled by compiler)</param>
	/// <returns>True if value changed, false otherwise</returns>
	protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
	{
		if (EqualityComparer<T>.Default.Equals(field, value))
		{
			return false;
		}

		field = value;
		OnPropertyChanged(propertyName);
		return true;
	}
}