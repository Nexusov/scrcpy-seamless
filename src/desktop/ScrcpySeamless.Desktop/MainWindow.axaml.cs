using System.Globalization;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ScrcpySeamless.Core.Configuration;
using ScrcpySeamless.Desktop.Presentation;

namespace ScrcpySeamless.Desktop;

/// <summary>Hosts the typed Desktop navigation shell.</summary>
public partial class MainWindow : Window
{
    private NormalDesktopComposition? normalComposition;
    private bool closeApproved;
    private bool closePromptOpen;
    private readonly PresentationText closeText = new();

    /// <summary>Provides a truthful empty design-time shell.</summary>
    public MainWindow() : this(DesktopComposition.Create(new DesktopLaunchOptions(false, AppTheme.System, null, false), _ => { }))
    {
    }

    /// <summary>Loads the same product shell for normal and preview composition.</summary>
    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Closing += OnClosing;
    }

    /// <summary>Enables the normal-only dirty-draft close guard after composition.</summary>
    public void AttachNormalComposition(NormalDesktopComposition composition)
    {
        normalComposition = composition;
    }

    /// <summary>Activates only committed Desktop command bindings in the local window.</summary>
    public void ApplyShortcuts(DesktopShortcutPreferences preferences)
    {
        KeyBindings.Clear();
        Bind(DesktopCommandIds.ShowDevices, () => ((ShellViewModel)DataContext!).ShowDevices());
        Bind(DesktopCommandIds.ShowSettings, () => ((ShellViewModel)DataContext!).ShowSettings());
        Bind(DesktopCommandIds.FocusSettingsSearch, FocusSettingsSearch);

        void Bind(string commandId, Action action)
        {
            string binding = preferences.EffectiveBinding(commandId);

            if (binding.Length == 0)
            {
                return;
            }

            string[] parts = binding.Split('+');
            string keyName = parts[^1].Length == 1 && char.IsAsciiDigit(parts[^1][0])
                ? "D" + parts[^1]
                : parts[^1] switch
                {
                    "Comma" => nameof(Key.OemComma),
                    "Period" => nameof(Key.OemPeriod),
                    _ => parts[^1],
                };

            if (!Enum.TryParse(keyName, ignoreCase: true, out Key key))
            {
                return;
            }

            KeyModifiers modifiers = parts[0] == "Alt" ? KeyModifiers.Alt : KeyModifiers.Control;

            if (parts.Length == 3)
            {
                modifiers |= KeyModifiers.Shift;
            }

            KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(key, modifiers), Command = new ActionCommand(action) });
        }
    }

    /// <summary>Focuses the real Settings search through its visible control route.</summary>
    private void FocusSettingsSearch()
    {
        ((ShellViewModel)DataContext!).ShowSettings();
        normalComposition?.Settings.ShowMirroringCommand.Execute(null);
        Dispatcher.UIThread.Post(() => this.GetVisualDescendants().OfType<TextBox>()
            .FirstOrDefault(control => control.Name == "OptionSearch")?.Focus(), DispatcherPriority.Loaded);
    }

    /// <summary>Reports an unexpected startup failure without exposing raw paths or contents.</summary>
    public void ShowStartupFailure(string errorKind)
    {
        Title = $"scrcpy Seamless — startup failed ({errorKind})";
    }

    /// <summary>Holds the window open until owned saves settle and dirty groups receive a decision.</summary>
    private async void OnClosing(object? sender, WindowClosingEventArgs eventArgs)
    {
        NormalDesktopComposition? composition = normalComposition;

        if (closeApproved || composition is null)
        {
            return;
        }

        if (composition.IsBusy || composition.IsCloseSaveActive)
        {
            eventArgs.Cancel = true;
            Title = "scrcpy Seamless — save in progress; close again when complete";
            return;
        }

        if (!composition.HasDirtyGroups)
        {
            return;
        }

        eventArgs.Cancel = true;

        if (closePromptOpen)
        {
            return;
        }

        closePromptOpen = true;

        try
        {
            CloseDecision decision = await AskCloseDecisionAsync(composition.DirtyGroupsLabel);

            if (decision == CloseDecision.Stay)
            {
                return;
            }

            if (decision == CloseDecision.Apply)
            {
                bool applied = await composition.SaveAndCloseAsync(CancellationToken.None);

                if (!applied)
                {
                    await ShowCloseFailureAsync();
                    return;
                }

                if (composition.HasDirtyGroups || composition.IsBusy || composition.IsCloseSaveActive)
                {
                    await ShowCloseFailureAsync();
                    return;
                }
            }
            // Closing discards in-memory drafts without touching a stale or invalid authority.

            closeApproved = true;
            Close();
        }
        finally
        {
            closePromptOpen = false;
        }
    }

    /// <summary>Asks for the exact owned save groups without silently writing or discarding.</summary>
    protected virtual Task<CloseDecision> AskCloseDecisionAsync(string groups)
    {
        Window dialog = new()
        {
            Title = closeText.Get("close.unsavedTitle"),
            Width = 480,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        dialog.KeyDown += (_, keyEventArgs) =>
        {
            if (keyEventArgs.Key != Key.Escape)
            {
                return;
            }

            keyEventArgs.Handled = true;
            dialog.Close(CloseDecision.Stay);
        };
        StackPanel content = new() { Margin = new Avalonia.Thickness(20), Spacing = 14 };
        content.Children.Add(new TextBlock
        {
            Text = string.Format(CultureInfo.CurrentCulture, closeText.Get("close.unsavedMessage"), groups),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };

        foreach ((string label, string automationId, CloseDecision decision) in new[]
        {
            (closeText.Get("close.saveAndClose"), "close.saveAndClose", CloseDecision.Apply),
            (closeText.Get("close.discardAndClose"), "close.discardAndClose", CloseDecision.Discard),
            (closeText.Get("close.keepEditing"), "close.keepEditing", CloseDecision.Stay),
        })
        {
            Button button = new() { Content = label };
            AutomationProperties.SetAutomationId(button, automationId);
            button.Click += (_, _) => dialog.Close(decision);
            actions.Children.Add(button);
        }

        content.Children.Add(actions);
        dialog.Content = content;
        return dialog.ShowDialog<CloseDecision>(this);
    }

    /// <summary>Explains a partial or failed close-time save while retaining all unsaved drafts.</summary>
    protected virtual async Task ShowCloseFailureAsync()
    {
        Window dialog = new()
        {
            Title = closeText.Get("close.failureTitle"),
            Width = 480,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new TextBlock
            {
                Margin = new Avalonia.Thickness(20),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Text = closeText.Get("close.failureMessage"),
            },
        };
        await dialog.ShowDialog(this);
    }

    protected enum CloseDecision { Stay, Apply, Discard }

    /// <summary>Loads the window markup.</summary>
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
