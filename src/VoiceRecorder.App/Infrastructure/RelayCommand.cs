using System.Windows.Input;

namespace VoiceRecorder.App.Infrastructure;

public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);
}

public class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Func<object?, bool>? _canExecute;
    private readonly Action<Exception>? _onError;
    private int _running;

    public AsyncRelayCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null, Action<Exception>? onError = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
        _onError = onError;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool IsRunning => Volatile.Read(ref _running) == 1;

    public bool CanExecute(object? parameter) => IsRunning == false && (_canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            return;
        }

        CommandManager.InvalidateRequerySuggested();
        try
        {
            await _execute(parameter);
        }
        catch (Exception ex)
        {
            _onError?.Invoke(ex);
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
