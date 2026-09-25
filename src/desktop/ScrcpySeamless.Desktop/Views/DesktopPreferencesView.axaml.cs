using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ScrcpySeamless.Desktop.Presentation;

namespace ScrcpySeamless.Desktop.Views;

/// <summary>Displays a separate appearance and command-shortcut edit group.</summary>
public partial class DesktopPreferencesView : UserControl
{
    /// <summary>Loads the typed, scrollable Desktop preferences editor.</summary>
    public DesktopPreferencesView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>Awaits the owned preferences save without blocking Avalonia's UI thread.</summary>
    private async void OnApplyClicked(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is DesktopPreferencesViewModel viewModel)
        {
            await viewModel.ApplyAsync(CancellationToken.None);
        }
    }

    /// <summary>Reloads saved state only after an explicit user action.</summary>
    private async void OnReloadClicked(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is DesktopPreferencesViewModel viewModel)
        {
            await viewModel.ReloadAsync(CancellationToken.None);
        }
    }
}
