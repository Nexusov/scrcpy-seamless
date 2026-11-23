using ScrcpySeamless.Desktop;
using Xunit;

namespace ScrcpySeamless.Desktop.Tests;

/// <summary>Normal portable startup needs no launcher or development arguments.</summary>
public sealed class PortableStartupTests
{
    [Fact]
    public void EmptyArgumentsSelectPortableStorageWithoutPreview()
    {
        DesktopLaunchOptions options = DesktopLaunchOptions.Parse([]);
        Assert.False(options.Preview);
        Assert.Equal(DesktopStorageMode.Portable, options.StorageMode);
        Assert.Null(options.DevelopmentDataDirectory);
        Assert.Null(options.DeviceRuntimeDirectory);
    }

    [Theory]
    [InlineData("--portable")]
    [InlineData("--installed")]
    public void ProductModesResolveOnlyAdjacentRuntime(string mode)
    {
        string applicationDirectory = Path.GetFullPath(Path.Combine("synthetic", "package"));
        DesktopLaunchOptions options = DesktopLaunchOptions.Parse([mode]);
        Assert.Equal(Path.Combine(applicationDirectory, "runtime"),
            NormalDesktopFactory.ResolveDeviceRuntimeDirectory(options, applicationDirectory));
    }

    [Fact]
    public void PreviewAndSettingsOnlyDevelopmentNeverSelectAnImplicitRuntime()
    {
        string applicationDirectory = Path.GetFullPath(Path.Combine("synthetic", "package"));
        string developmentDirectory = Path.GetFullPath(Path.Combine("synthetic", "data"));
        Assert.Null(NormalDesktopFactory.ResolveDeviceRuntimeDirectory(
            DesktopLaunchOptions.Parse(["--preview"]), applicationDirectory));
        Assert.Null(NormalDesktopFactory.ResolveDeviceRuntimeDirectory(
            DesktopLaunchOptions.Parse(["--dev-data-dir=" + developmentDirectory]), applicationDirectory));
    }

    [Fact]
    public void ProductRuntimeCannotBeRedirectedByADevelopmentOptionRecord()
    {
        string applicationDirectory = Path.GetFullPath(Path.Combine("synthetic", "package"));
        DesktopLaunchOptions options = DesktopLaunchOptions.Parse([]) with
        {
            DeviceRuntimeDirectory = Path.GetFullPath(Path.Combine("synthetic", "unexpected")),
        };
        Assert.Equal(Path.Combine(applicationDirectory, "runtime"),
            NormalDesktopFactory.ResolveDeviceRuntimeDirectory(options, applicationDirectory));
        Assert.Throws<ArgumentException>(() => DesktopLaunchOptions.Parse(
            ["--portable", "--device-runtime=" + options.DeviceRuntimeDirectory]));
    }

    [Fact]
    public void LegacyImportRequiresOneExplicitAbsoluteSourceOutsidePreviewAndDevModes()
    {
        string source = Path.GetFullPath(Path.Combine("synthetic", "legacy app"));
        string argument = "--import-legacy=" + source;

        DesktopLaunchOptions portable = DesktopLaunchOptions.Parse([argument, "--page=settings"]);
        Assert.Equal(source, portable.LegacyImportDirectory);
        Assert.Equal(DesktopStorageMode.Portable, portable.StorageMode);
        Assert.Equal(source, DesktopLaunchOptions.Parse(["--installed", argument]).LegacyImportDirectory);
        Assert.Throws<ArgumentException>(() => DesktopLaunchOptions.Parse(["--preview", argument]));
        Assert.Throws<ArgumentException>(() => DesktopLaunchOptions.Parse(["--dev-data-dir=" + source, argument]));
        Assert.Throws<ArgumentException>(() => DesktopLaunchOptions.Parse(["--import-legacy=relative"]));
        Assert.Throws<ArgumentException>(() => DesktopLaunchOptions.Parse([argument, argument]));
    }
}
