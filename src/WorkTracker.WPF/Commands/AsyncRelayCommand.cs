using System.Windows.Input;

namespace WorkTracker.WPF.Commands;

/// <summary>
/// Async implementation of ICommand for parameterless async commands
/// Properly handles async/await and prevents multiple concurrent executions
/// </summary>
public class AsyncRelayCommand : ICommand
{
	private readonly Func<Task> _execute;
	private readonly Func<bool>? _canExecute;
	private bool _isExecuting;

	public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
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
		return !_isExecuting && (_canExecute == null || _canExecute());
	}

	public async void Execute(object? parameter)
	{
		if (!CanExecute(parameter))
		{
			return;
		}

		_isExecuting = true;
		RaiseCanExecuteChanged();

		try
		{
			await _execute();
		}
		finally
		{
			_isExecuting = false;
			RaiseCanExecuteChanged();
		}
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
/// Async implementation of ICommand for async commands with parameters
/// Properly handles async/await and prevents multiple concurrent executions
/// </summary>
public class AsyncRelayCommand<T> : ICommand
{
	private readonly Func<T?, Task> _execute;
	private readonly Func<T?, bool>? _canExecute;
	private bool _isExecuting;

	public AsyncRelayCommand(Func<T?, Task> execute, Func<T?, bool>? canExecute = null)
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
		return !_isExecuting && (_canExecute == null || _canExecute((T?)parameter));
	}

	public async void Execute(object? parameter)
	{
		if (!CanExecute(parameter))
		{
			return;
		}

		_isExecuting = true;
		RaiseCanExecuteChanged();

		try
		{
			await _execute((T?)parameter);
		}
		finally
		{
			_isExecuting = false;
			RaiseCanExecuteChanged();
		}
	}

	/// <summary>
	/// Raises CanExecuteChanged to re-evaluate command availability
	/// </summary>
	public void RaiseCanExecuteChanged()
	{
		CommandManager.InvalidateRequerySuggested();
	}
}