using System.Windows.Input;

namespace ScrcpySeamless.Desktop.Presentation;

/// <summary>Exposes a synchronous presentation action through an Avalonia command.</summary>
public sealed class ActionCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute();
}
