using ScrcpySeamless.Core.Options;
using Xunit;

namespace ScrcpySeamless.Core.Tests;

public sealed class NativeShortcutBindingsTests
{
    [Fact]
    public void InheritsOneGeneratedCatalogWithoutDefaultConflicts()
    {
        Assert.Equal(34, GeneratedNativeShortcutCatalog.Actions.Count);
        Assert.True(NativeShortcutBindings.TryParse("{}", out NativeShortcutBindings? bindings));
        Assert.Empty(bindings!.Overrides);
        Assert.Equal(2, bindings.Effective(Action("back")).Count);
        Assert.Equal(2, bindings.Effective(Action("fullscreen")).Count);
        Assert.Equal(1, Assert.Single(bindings.Effective(Action("notifications"))).Tap);
        Assert.Equal(2, Assert.Single(bindings.Effective(Action("quickSettings"))).Tap);
    }

    [Theory]
    [InlineData("MOD+A")]
    [InlineData("MOD+Shift+Z")]
    [InlineData("MOD+Backspace")]
    [InlineData("MOD+Left")]
    [InlineData("MOD+F24")]
    [InlineData("F12")]
    public void AcceptsBoundedFocusedModifierOrFunctionGestures(string gesture)
    {
        Assert.True(NativeShortcutBindings.TryParseGesture(gesture, out NativeShortcutGesture? value));
        Assert.NotNull(value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("Ctrl+A")]
    [InlineData("Shift+F12")]
    [InlineData("MOD+")]
    [InlineData("MOD+shift+Z")]
    [InlineData("MOD+a")]
    [InlineData("MOD+Shift+Shift+Z")]
    [InlineData("F0")]
    [InlineData("F01")]
    [InlineData("F25")]
    public void RejectsAmbiguousOrUnsupportedGestures(string gesture) =>
        Assert.False(NativeShortcutBindings.TryParseGesture(gesture, out _));

    [Theory]
    [InlineData("{\"home\":\"MOD+Q\"}")]
    [InlineData("{\"future\":\"MOD+J\"}")]
    [InlineData("{\"home\":null}")]
    [InlineData("{\"home\":true}")]
    [InlineData("{\"home\":\"MOD+J\",\"home\":\"MOD+L\"}")]
    [InlineData("[]")]
    [InlineData("{\"home\":\"\\uD800\"}")]
    [InlineData("{\"\\uD800\":\"F12\"}")]
    public void InvalidMapNeverPartiallyApplies(string json)
    {
        Assert.False(NativeShortcutBindings.TryParse(json, out NativeShortcutBindings? bindings,
            out IReadOnlyList<NativeShortcutBindingIssue> issues));
        Assert.Null(bindings);
        Assert.NotEmpty(issues);
    }

    [Fact]
    public void OverrideReplacesAliasesAndDisableRemovesAction()
    {
        Assert.True(NativeShortcutBindings.TryParse("{\"back\":\"F12\",\"fullscreen\":\"\"}",
            out NativeShortcutBindings? bindings));
        Assert.Equal("F12", Assert.Single(bindings!.Effective(Action("back"))).Key);
        Assert.Empty(bindings.Effective(Action("fullscreen")));
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<string, string>)bindings.Overrides).Add("home", "MOD+J"));
    }

    [Fact]
    public void ContextsAllowCameraAndDisplayButNotAllScopeCollision()
    {
        Assert.True(NativeShortcutBindings.TryParse("{\"volumeUp\":\"MOD+T\"}", out _));
        Assert.False(NativeShortcutBindings.TryParse("{\"pause\":\"MOD+T\"}", out _, out var issues));
        Assert.Contains(issues, issue => issue.Error == NativeShortcutBindingError.Conflict);
        Assert.True(NativeShortcutBindings.TryParse("{\"home\":\"MOD+Q\",\"quitMirror\":\"\"}", out _));
    }

    [Fact]
    public void DirectQuickSettingsOverrideKeepsInheritedFirstPressDistinct()
    {
        Assert.True(NativeShortcutBindings.TryParse("{\"quickSettings\":\"MOD+J\"}", out NativeShortcutBindings? direct));
        Assert.Equal(0, Assert.Single(direct!.Effective(Action("quickSettings"))).Tap);
        Assert.Equal(1, Assert.Single(direct.Effective(Action("notifications"))).Tap);
        Assert.True(NativeShortcutBindings.TryParse("{\"quickSettings\":\"MOD+N\"}", out NativeShortcutBindings? same));
        Assert.Equal(2, Assert.Single(same!.Effective(Action("quickSettings"))).Tap);
    }

    [Fact]
    public void BoundsSerializedMapBeforeParsing()
    {
        Assert.False(NativeShortcutBindings.TryParse("{}" + new string(' ', NativeShortcutBindings.MaximumBytes),
            out _, out var issues));
        Assert.Equal(NativeShortcutBindingError.TooLarge, Assert.Single(issues).Error);
        Assert.True(NativeShortcutBindings.TryParse("{}" + new string(' ', NativeShortcutBindings.MaximumBytes - 2), out _));
    }

    private static NativeShortcutDefinition Action(string id) => GeneratedNativeShortcutCatalog.Actions.Single(action => action.Id == id);
}
