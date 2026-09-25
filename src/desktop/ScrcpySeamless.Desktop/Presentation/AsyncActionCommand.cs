using System.Windows.Input;

namespace ScrcpySeamless.Desktop.Presentation;

/// <summary>Runs one asynchronous UI action and reports failures through an owned presentation callback.</summary>
public sealed class AsyncActionCommand(
    Func<Task> execute,
    Func<bool> canExecute,
    Action<Exception> onError) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute();

    /// <summary>Invokes an awaited command without letting a void UI event lose its error.</summary>
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        try
        {
            await execute();
        }
        catch (Exception exception)
        {
            onError(exception);
        }
    }

    /// <summary>Re-evaluates command availability after workspace state changes.</summary>
    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
