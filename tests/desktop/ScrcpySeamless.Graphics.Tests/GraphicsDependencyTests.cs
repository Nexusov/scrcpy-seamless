using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using ScrcpySeamless.Desktop;
using SkiaSharp;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(ScrcpySeamless.Graphics.Tests.GraphicsApplicationBuilder))]

namespace ScrcpySeamless.Graphics.Tests;

/// <summary>Runs the real software renderer without a visible OS window or device adapters.</summary>
public static class GraphicsApplicationBuilder
{
    /// <summary>Keeps this real native-ABI lane separate from the existing mock-drawing suite.</summary>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>Exercises the supported Skia/native pairing and the used Avalonia rendering boundary.</summary>
public sealed class GraphicsDependencyTests
{
    /// <summary>Rejects the control and both formerly accepted CVE-2026-119261 private-type forms.</summary>
    [Theory]
    [InlineData("uniform sampler2D t; half4 main(float2 p) { return half4(1); }")]
    [InlineData("struct S { sampler2D x; }; half4 main(float2 p) { return half4(1); }")]
    [InlineData("half4 f(sampler2D s) { return half4(1); } half4 main(float2 p) { return half4(1); }")]
    public void RuntimeEffectRejectsPrivateTypes(string program)
    {
        using var effect = SKRuntimeEffect.CreateShader(program, out string errors);
        Assert.Null(effect);
        Assert.Contains("invalid type", errors, StringComparison.Ordinal);
    }

    /// <summary>Calls the real Avalonia HarfBuzz adapter for LTR and RTL text using the current native pair.</summary>
    [AvaloniaTheory]
    [InlineData("Seamless office Ω", 0)]
    [InlineData("العربية", 1)]
    public void RealGlyphShapingReturnsUsableGlyphs(string text, int level)
    {
        using var shaped = TextShaper.Current.ShapeText(text.AsMemory(),
            new TextShaperOptions(new Typeface("Arial").GlyphTypeface, 24, (sbyte)level));
        Assert.Equal(text, shaped.Text.ToString());
        Assert.Equal(level == 0, shaped.IsLeftToRight);
        Assert.NotEmpty(shaped);
        Assert.All(shaped, glyph => Assert.NotEqual((ushort)0, glyph.GlyphIndex));
        Assert.True(shaped.Sum(glyph => glyph.GlyphAdvance) > 0);
    }

    /// <summary>Requires actual control and text pixels, not just a successful mock render call.</summary>
    [AvaloniaFact]
    public void RealAvaloniaRasterRendersTextAndRoundedControl()
    {
        var panel = new StackPanel { Background = Brushes.White, Width = 320, Height = 180 };
        panel.Children.Add(new TextBlock { Text = "Seamless Ω العربية 123", FontSize = 24, Foreground = Brushes.Black });
        panel.Children.Add(new Border
        {
            Width = 180, Height = 60, CornerRadius = new CornerRadius(12), Background = Brushes.Blue,
            Child = new TextBlock { Text = "Rendering", Foreground = Brushes.White },
        });
        panel.Measure(new Size(320, 180));
        panel.Arrange(new Rect(0, 0, 320, 180));
        using var rendered = new RenderTargetBitmap(new PixelSize(320, 180), new Vector(96, 96));
        rendered.Render(panel);
        using var encoded = new MemoryStream();
        rendered.Save(encoded, PngBitmapEncoderOptions.Default);
        using var decoded = SKBitmap.Decode(encoded.ToArray());
        Assert.Equal(320, decoded.Width);
        Assert.Equal(180, decoded.Height);
        Assert.True(decoded.Pixels.Count(pixel => pixel.Blue > 200 && pixel.Red < 30 && pixel.Green < 30) > 500);
        Assert.True(decoded.Pixels.Count(pixel => pixel.Red < 80 && pixel.Blue < 80 && pixel.Green < 80) > 20);
    }

    /// <summary>Checks the native raster/clip/path/image codec boundary without initializing a GPU.</summary>
    [Fact]
    public void RasterClipPathAndPngRoundTripRetainPixels()
    {
        using var surface = SKSurface.Create(new SKImageInfo(64, 64));
        Assert.NotNull(surface);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        using var rounded = new SKRoundRect(new SKRect(8, 8, 56, 56), 8);
        canvas.ClipRoundRect(rounded, SKClipOperation.Intersect, true);
        using var builder = new SKPathBuilder();
        builder.AddRect(new SKRect(0, 0, 64, 64));
        using var path = builder.Detach();
        using var paint = new SKPaint { Color = SKColors.Blue, IsAntialias = true };
        canvas.DrawPath(path, paint);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var decoded = SKBitmap.Decode(data);
        Assert.Equal(SKColors.White, decoded.GetPixel(0, 0));
        Assert.Equal(SKColors.Blue, decoded.GetPixel(32, 32));
        Assert.Equal(155, SkiaSharpVersion.Native.Major);
    }

    /// <summary>Renders the real preview shell through Skia while its device adapters remain isolated.</summary>
    [AvaloniaFact]
    public void PreviewShellRendersThroughTheRealSoftwareBackend()
    {
        var shell = DesktopComposition.Create(new DesktopLaunchOptions(true, AppTheme.System, null, true), _ => { });
        var window = new MainWindow(shell) { Width = 900, Height = 620 };

        try
        {
            window.Show();
            window.UpdateLayout();
            using var rendered = new RenderTargetBitmap(new PixelSize(900, 620), new Vector(96, 96));
            rendered.Render(window);
            using var encoded = new MemoryStream();
            rendered.Save(encoded, PngBitmapEncoderOptions.Default);
            using var decoded = SKBitmap.Decode(encoded.ToArray());
            Assert.True(decoded.Pixels.Select(pixel => (uint)pixel).Distinct().Take(16).Count() >= 16);
            Assert.Equal("scrcpy Seamless", shell.ProductName);
        }
        finally
        {
            window.Close();
        }
    }
}
