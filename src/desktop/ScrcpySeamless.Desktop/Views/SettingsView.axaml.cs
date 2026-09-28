using Avalonia.Controls;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Input;
using ScrcpySeamless.Desktop.Presentation;

namespace ScrcpySeamless.Desktop.Views;

/// <summary>Shows a searchable, categorized in-memory draft of generated settings.</summary>
public partial class SettingsView : UserControl
{
    private const double CompactCategoryWidth = 720;
    private const double StackedCategoryWidth = 500;
    private SettingsViewModel? observedViewModel;

    /// <summary>Loads the typed Settings preview.</summary>
    public SettingsView()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) => BindResults();
        AttachedToVisualTree += (_, _) => BindResults();
        DetachedFromVisualTree += (_, _) => UnbindResults();
        SizeChanged += (_, _) => UpdateLayoutMode();
    }

    /// <summary>Subscribes only while this view is mounted.</summary>
    private void BindResults()
    {
        UnbindResults();
        observedViewModel = DataContext as SettingsViewModel;

        if (observedViewModel is not null)
        {
            observedViewModel.ResultsChanged += OnResultsChanged;
            observedViewModel.PropertyChanged += OnSettingsPropertyChanged;

            // Preview retains the accepted local gesture; normal mode activates only saved bindings.
            KeyBindings.Clear();

            if (observedViewModel.IsPreview)
            {
                KeyBindings.Add(new KeyBinding
                {
                    Gesture = SettingsShortcuts.FocusSearch,
                    Command = new ActionCommand(() => this.FindControl<TextBox>("OptionSearch")!.Focus()),
                });
            }

            UpdateLayoutMode();
        }
    }

    /// <summary>Releases the view-local result subscription.</summary>
    private void UnbindResults()
    {
        if (observedViewModel is null)
        {
            return;
        }

        observedViewModel.ResultsChanged -= OnResultsChanged;
        observedViewModel.PropertyChanged -= OnSettingsPropertyChanged;
        observedViewModel = null;
    }

    /// <summary>Reclaims the configuration toolbar space when the reference tab opens.</summary>
    private void OnSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(SettingsViewModel.IsShortcutsSection))
        {
            UpdateLayoutMode();
        }
    }

    /// <summary>Reveals the first changed result after the item layout catches up.</summary>
    private void OnResultsChanged(SettingsResultsChange change)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (observedViewModel is null)
            {
                return;
            }

            ScrollViewer scroll = this.FindControl<ScrollViewer>("OptionScroll")!;
            scroll.Offset = new Vector(scroll.Offset.X, 0);
        }, DispatcherPriority.Loaded);
    }

    /// <summary>Gives compact windows one bounded category selector above results.</summary>
    private void UpdateLayoutMode()
    {
        Grid columns = this.FindControl<Grid>("SettingsColumns")!;
        Grid navigation = this.FindControl<Grid>("CategoryNavigation")!;
        ComboBox picker = this.FindControl<ComboBox>("CompactCategoryPicker")!;
        Grid results = this.FindControl<Grid>("SettingsResults")!;
        bool compact = Bounds.Width < CompactCategoryWidth;
        // Once categories use the compact picker, results span the full content width below it.
        bool stacked = compact;
        bool shortWindow = Bounds.Height < 500;
        bool compactPersistent = shortWindow && observedViewModel?.IsPersistent == true;
        // Reflow the preview title only where the adjacent Reset action would clip it.
        this.FindControl<TextBlock>("SettingsTitle")!.TextWrapping = Bounds.Width < StackedCategoryWidth
            ? Avalonia.Media.TextWrapping.Wrap
            : Avalonia.Media.TextWrapping.NoWrap;
        this.FindControl<TextBlock>("SettingsEyebrow")!.IsVisible = !shortWindow;
        this.FindControl<TextBlock>("SettingsSubtitle")!.IsVisible = !shortWindow;
        this.FindControl<TextBlock>("SettingsGlobalLabel")!.IsVisible = !shortWindow;
        this.FindControl<TextBlock>("SettingsUnsavedLabel")!.IsVisible = !shortWindow;
        this.FindControl<TextBlock>("SettingsShortcutHelp")!.IsVisible = !shortWindow;
        // Preview keeps its section navigation in place of persistence actions.
        bool showConfigurationToolbar = observedViewModel?.IsPersistent == true &&
            observedViewModel.IsShortcutsSection != true;
        this.FindControl<Border>("SettingsStatusBlock")!.IsVisible = showConfigurationToolbar && !compactPersistent;
        this.FindControl<Border>("CompactSettingsToolbar")!.IsVisible = showConfigurationToolbar && compactPersistent;
        navigation.IsVisible = !compact;
        picker.IsVisible = compact;
        columns.ColumnDefinitions = stacked ? new ColumnDefinitions("*") : new ColumnDefinitions("160,*");
        columns.RowDefinitions = stacked ? new RowDefinitions("Auto,*") : new RowDefinitions("*");
        columns.ColumnSpacing = stacked ? 0 : 12;
        columns.RowSpacing = stacked ? 8 : 0;
        Grid.SetColumn(results, stacked ? 0 : 1);
        Grid.SetRow(results, stacked ? 1 : 0);
        results.RowSpacing = shortWindow ? 6 : 12;
    }
}
