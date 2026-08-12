using System.Windows.Input;

namespace QuickSearch.Core;

public sealed class RelayCommand(
    Action execute,
    Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => execute();

    public void NotifyCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class RelayCommand<T>(
    Action<T> execute,
    Func<T, bool>? canExecute = null) : ICommand
    where T : notnull
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) =>
        parameter is T value && (canExecute?.Invoke(value) ?? true);

    public void Execute(object? parameter)
    {
        if (parameter is T value && CanExecute(value))
        {
            execute(value);
        }
    }

    public void NotifyCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private readonly Action<Exception> _onException;
    private bool _isExecuting;

    public AsyncRelayCommand(
        Func<Task> execute,
        Func<bool>? canExecute = null,
        Action<Exception>? onException = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = execute;
        _canExecute = canExecute;
        _onException = onException ?? (_ => { });
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) =>
        !_isExecuting && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _isExecuting = true;
        NotifyCanExecuteChanged();
        try
        {
            await _execute();
        }
        catch (Exception exception)
        {
            _onException(exception);
        }
        finally
        {
            _isExecuting = false;
            NotifyCanExecuteChanged();
        }
    }

    public void NotifyCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class AsyncRelayCommand<T> : ICommand
    where T : notnull
{
    private readonly Func<T, Task> _execute;
    private readonly Func<T, bool>? _canExecute;
    private readonly Action<Exception> _onException;
    private bool _isExecuting;

    public AsyncRelayCommand(
        Func<T, Task> execute,
        Func<T, bool>? canExecute = null,
        Action<Exception>? onException = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = execute;
        _canExecute = canExecute;
        _onException = onException ?? (_ => { });
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) =>
        !_isExecuting
        && parameter is T value
        && (_canExecute?.Invoke(value) ?? true);

    public async void Execute(object? parameter)
    {
        if (parameter is not T value || !CanExecute(value))
        {
            return;
        }

        _isExecuting = true;
        NotifyCanExecuteChanged();
        try
        {
            await _execute(value);
        }
        catch (Exception exception)
        {
            _onException(exception);
        }
        finally
        {
            _isExecuting = false;
            NotifyCanExecuteChanged();
        }
    }

    public void NotifyCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
