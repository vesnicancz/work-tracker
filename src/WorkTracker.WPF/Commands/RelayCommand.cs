using System.Windows.Input;

namespace WorkTracker.WPF.Commands;

/// <summary>
/// Generic implementation of ICommand for parameterless commands
/// </summary>
public class RelayCommand : ICommand
{
	private readonly Action _execute;
	private readonly Func<bool>? _canExecute;

	public RelayCommand(Action execute, Func<bool>? canExecute = null)
	{
		_execute = execute ?? throw new ArgumentNullException(nameof(execute));
		_canExecute = canExecute;
	}

	public event EventHandler? CanExecuteChanged
	{
		add => CommandManager.RequerySuggested += value;
		remove => CommandManager.RequerySuggested -= value;
	}

	public bool CanExecute(object? parameter)
	{
		return _canExecute == null || _canExecute();
	}

	public void Execute(object? parameter)
	{
		_execute();
	}

	/// <summary>
	/// Raises CanExecuteChanged to re-evaluate command availability
	/// </summary>
	public void RaiseCanExecuteChanged()
	{
		CommandManager.InvalidateRequerySuggested();
	}
}

/// <summary>
/// Generic implementation of ICommand for commands with parameters
/// </summary>
public class RelayCommand<T> : ICommand
{
	private readonly Action<T?> _execute;
	private readonly Func<T?, bool>? _canExecute;

	public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
	{
		_execute = execute ?? throw new ArgumentNullException(nameof(execute));
		_canExecute = canExecute;
	}

	public event EventHandler? CanExecuteChanged
	{
		add => CommandManager.RequerySuggested += value;
		remove => CommandManager.RequerySuggested -= value;
	}

	public bool CanExecute(object? parameter)
	{
		return _canExecute == null || _canExecute((T?)parameter);
	}

	public void Execute(object? parameter)
	{
		_execute((T?)parameter);
	}

	/// <summary>
	/// Raises CanExecuteChanged to re-evaluate command availability
	/// </summary>
	public void RaiseCanExecuteChanged()
	{
		CommandManager.InvalidateRequerySuggested();
	}
}