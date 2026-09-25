using System.Text.Json;
using ScrcpySeamless.Core.Configuration;
using ScrcpySeamless.Core.Options;
using Xunit;

namespace ScrcpySeamless.Core.Tests;

/// <summary>Protects pure option semantics without launching native scrcpy.</summary>
public sealed class OptionSelectionValidatorTests
{
    /// <summary>Unknown migrated data stays in v2 but never enters an invocation.</summary>
    [Fact]
    public void UnknownStoredOptionIsPreservedAndNotExecutable()
    {
        MirroringPreferences preferences = CreatePreferences(("future-option", "value"));

        OptionSelectionResult result = OptionSelectionValidator.Evaluate(preferences);

        Assert.Contains(result.Diagnostics, issue => issue.Code == OptionDiagnosticCode.UnknownOption);
        Assert.Empty(result.Arguments);
        Assert.Equal("value", preferences.Options["future-option"].GetString());
        Assert.Empty(preferences.Validate("Mirroring"));
    }

    /// <summary>Managed connection controls and one-shot actions cannot become saved settings.</summary>
    [Theory]
    [InlineData("help")]
    [InlineData("version")]
    [InlineData("list-encoders")]
    [InlineData("select-usb")]
    [InlineData("tcpip")]
    public void NonEditableOptionsDoNotBecomeArguments(string optionId)
    {
        MirroringPreferences preferences = CreatePreferences((optionId, true));

        OptionSelectionResult result = OptionSelectionValidator.Evaluate(preferences);

        Assert.Contains(result.Diagnostics, issue => issue.Code == OptionDiagnosticCode.NotEditable);
        Assert.Empty(result.Arguments);
    }

    /// <summary>Valid values become separate, deterministic arguments in canonical order.</summary>
    [Fact]
    public void ValidSettingsProduceDiscreteArguments()
    {
        MirroringPreferences preferences = CreatePreferences(
            ("max-size", "1920"),
            ("no-audio", false),
            ("video-source", "display"));

        OptionSelectionResult result = OptionSelectionValidator.Evaluate(preferences);

        Assert.True(result.IsValid);
        Assert.Contains("--max-size=1920", result.Arguments);
        Assert.Contains("--video-source=display", result.Arguments);
        Assert.DoesNotContain("--no-audio", result.Arguments);
    }

    /// <summary>Static enum and numeric metadata reject malformed values before native execution.</summary>
    [Theory]
    [InlineData("video-source", "unknown")]
    [InlineData("max-size", "abc")]
    [InlineData("max-size", "-1")]
    public void InvalidStaticValuesAreReported(string optionId, string value)
    {
        OptionSelectionResult result = OptionSelectionValidator.Evaluate(CreatePreferences((optionId, value)));

        Assert.Contains(result.Diagnostics, issue => issue.Code == OptionDiagnosticCode.InvalidValue);
        Assert.Empty(result.Arguments);
    }

    /// <summary>Switch and optional-argument shapes retain their native representation.</summary>
    [Fact]
    public void SwitchAndOptionalArgumentShapesAreDistinct()
    {
        OptionSelectionResult result = OptionSelectionValidator.Evaluate(CreatePreferences(
            ("no-audio", true),
            ("new-display", "")));

        Assert.True(result.IsValid);
        Assert.Contains("--no-audio", result.Arguments);
        Assert.Contains("--new-display", result.Arguments);
    }

    /// <summary>Named camera rule checks conditional source and selection conflicts.</summary>
    [Fact]
    public void CameraRuleRejectsParametersWithoutCameraSource()
    {
        OptionSelectionResult result = OptionSelectionValidator.Evaluate(CreatePreferences(("camera-id", "0")));

        Assert.Contains(result.Diagnostics, issue => issue.RuleId == "camera-source-rules");
        Assert.Empty(result.Arguments);
    }

    /// <summary>Camera exclusions use the parsed base-zero value, including hexadecimal spelling.</summary>
    [Theory]
    [InlineData("00", true)]
    [InlineData("0x10", false)]
    public void CameraSizeUsesEffectiveMaxSize(string maxSize, bool expectedValid)
    {
        OptionSelectionResult result = OptionSelectionValidator.Evaluate(CreatePreferences(
            ("video-source", "camera"),
            ("camera-size", "1920x1080"),
            ("max-size", maxSize)));

        Assert.Equal(expectedValid, result.IsValid);

        if (!expectedValid)
        {
            Assert.Contains(result.Diagnostics, issue => issue.RuleId == "camera-source-rules");
        }
    }

    /// <summary>Unconditional spec relationships are checked without a conditional rule.</summary>
    [Fact]
    public void RecordFormatRequiresRecordingTarget()
    {
        OptionSelectionResult result = OptionSelectionValidator.Evaluate(CreatePreferences(("record-format", "mkv")));

        Assert.Contains(result.Diagnostics, issue => issue.Code == OptionDiagnosticCode.MissingRequirement && issue.OptionId == "record-format");
        Assert.Empty(result.Arguments);
    }

    /// <summary>Recording semantics reject contradictory capture selections.</summary>
    [Fact]
    public void RecordingRuleRejectsDisabledVideoAndAudio()
    {
        OptionSelectionResult result = OptionSelectionValidator.Evaluate(CreatePreferences(
            ("record", "capture.mkv"),
            ("no-video", true),
            ("no-audio", true)));

        Assert.Contains(result.Diagnostics, issue => issue.RuleId == "recording-output-semantics");
    }

    /// <summary>Audio duplication cannot use a disabled audio capture path.</summary>
    [Fact]
    public void AudioDuplicationRuleRejectsDisabledAudio()
    {
        OptionSelectionResult result = OptionSelectionValidator.Evaluate(CreatePreferences(
            ("audio-dup", true),
            ("no-audio", true)));

        Assert.Contains(result.Diagnostics, issue => issue.RuleId == "audio-dup-requires-playback");
    }

    /// <summary>Flexible display sizing needs a new display, not the existing one.</summary>
    [Fact]
    public void VirtualDisplayRuleRequiresNewDisplay()
    {
        OptionSelectionResult result = OptionSelectionValidator.Evaluate(CreatePreferences(("flex-display", true)));

        Assert.Contains(result.Diagnostics, issue => issue.RuleId == "virtual-display-rules");
    }

    /// <summary>Automatic zero dimensions do not conflict with flexible virtual display sizing.</summary>
    [Theory]
    [InlineData(null, null, true)]
    [InlineData("0", null, true)]
    [InlineData(null, "0", true)]
    [InlineData("0", "0", true)]
    [InlineData("00", null, true)]
    [InlineData("010", null, false)]
    [InlineData(null, "0x10", false)]
    [InlineData("1", null, false)]
    [InlineData(null, "1", false)]
    public void FlexDisplayUsesEffectiveWindowDimensions(string? width, string? height, bool expectedValid)
    {
        List<(string OptionId, object Value)> selections = [("new-display", ""), ("flex-display", true)];

        if (width is not null)
        {
            selections.Add(("window-width", width));
        }

        if (height is not null)
        {
            selections.Add(("window-height", height));
        }

        OptionSelectionResult result = OptionSelectionValidator.Evaluate(CreatePreferences([.. selections]));

        Assert.Equal(expectedValid, result.IsValid);

        if (!expectedValid)
        {
            Assert.Contains(result.Diagnostics, issue => issue.RuleId == "virtual-display-rules");
        }
    }

    /// <summary>Native numeric boundaries are enforced before constructing CLI arguments.</summary>
    [Theory]
    [InlineData("audio-output-buffer", "0", true)]
    [InlineData("audio-output-buffer", "1000", true)]
    [InlineData("audio-output-buffer", "-1", false)]
    [InlineData("audio-output-buffer", "1001", false)]
    [InlineData("audio-output-buffer", "invalid", false)]
    [InlineData("max-size", "0", true)]
    [InlineData("max-size", "65535", true)]
    [InlineData("max-size", "-1", false)]
    [InlineData("max-size", "65536", false)]
    [InlineData("min-size-alignment", "1", true)]
    [InlineData("min-size-alignment", "2", true)]
    [InlineData("min-size-alignment", "4", true)]
    [InlineData("min-size-alignment", "8", true)]
    [InlineData("min-size-alignment", "16", true)]
    [InlineData("min-size-alignment", "0", false)]
    [InlineData("min-size-alignment", "3", false)]
    [InlineData("min-size-alignment", "17", false)]
    [InlineData("min-size-alignment", "invalid", false)]
    [InlineData("min-size-alignment", "1.5", false)]
    public void NativeNumericConstraintsAreApplied(string optionId, string value, bool expectedValid)
    {
        OptionSelectionResult result = OptionSelectionValidator.Evaluate(CreatePreferences((optionId, value)));

        Assert.Equal(expectedValid, result.IsValid);

        if (!expectedValid)
        {
            Assert.Empty(result.Arguments);
        }
    }

    /// <summary>Native base-zero spelling must be validated before forwarding the unchanged argument.</summary>
    [Theory]
    [InlineData("max-size", "00", true)]
    [InlineData("max-size", "010", true)]
    [InlineData("max-size", "08", false)]
    [InlineData("max-size", "0x10", true)]
    [InlineData("max-size", "0X10", true)]
    [InlineData("max-size", "0177777", true)]
    [InlineData("max-size", "0200000", false)]
    [InlineData("max-size", "0x", false)]
    [InlineData("max-size", "10junk", false)]
    [InlineData("max-size", "2147483648", false)]
    [InlineData("max-size", "+16", true)]
    [InlineData("max-size", "-0", true)]
    [InlineData("max-size", "+", false)]
    [InlineData("max-size", " 10", false)]
    [InlineData("min-size-alignment", "010", true)]
    [InlineData("min-size-alignment", "020", true)]
    [InlineData("min-size-alignment", "08", false)]
    [InlineData("min-size-alignment", "0x10", true)]
    [InlineData("min-size-alignment", "3", false)]
    [InlineData("video-bit-rate", "010K", true)]
    [InlineData("video-bit-rate", "08K", false)]
    [InlineData("video-bit-rate", "0x10M", true)]
    [InlineData("video-bit-rate", "2147484K", false)]
    [InlineData("video-bit-rate", "2147M", true)]
    [InlineData("video-bit-rate", "2148M", false)]
    [InlineData("audio-bit-rate", "0X10m", true)]
    [InlineData("tunnel-port", "010", true)]
    [InlineData("tunnel-port", "08", false)]
    [InlineData("window-x", "-010", true)]
    [InlineData("window-x", "-08", false)]
    [InlineData("window-y", "-0x10", true)]
    [InlineData("window-y", "-32768", false)]
    [InlineData("window-width", "0200000", false)]
    [InlineData("window-height", "0177777", true)]
    [InlineData("audio-buffer", "0x36ee80", true)]
    [InlineData("audio-buffer", "3600001", false)]
    [InlineData("video-buffer", "3600001", false)]
    [InlineData("camera-fps", "65536", false)]
    [InlineData("display-id", "2147483648", false)]
    [InlineData("screen-off-timeout", "2147484", false)]
    [InlineData("time-limit", "2147483648", false)]
    [InlineData("tunnel-port", "65536", false)]
    public void NativeScalarSyntaxMatchesEmittedArgument(string optionId, string value, bool expectedValid)
    {
        OptionSelectionResult result = OptionSelectionValidator.Evaluate(CreatePreferences((optionId, value)));

        Assert.Equal(expectedValid, result.IsValid);

        if (expectedValid)
        {
            Assert.Contains($"--{optionId}={value}", result.Arguments);
            return;
        }

        Assert.Empty(result.Arguments);
    }

    /// <summary>A session cannot disable all three meaningful channels at once.</summary>
    [Fact]
    public void SessionOutputRuleRejectsNoVideoAudioOrControl()
    {
        OptionSelectionResult result = OptionSelectionValidator.Evaluate(CreatePreferences(
            ("no-video", true),
            ("no-audio", true),
            ("no-control", true)));

        Assert.Contains(result.Diagnostics, issue => issue.RuleId == "session-output-required");
    }

    /// <summary>Converts test fixtures into the persisted bool/string shape.</summary>
    private static MirroringPreferences CreatePreferences(params (string OptionId, object Value)[] selections)
    {
        Dictionary<string, JsonElement> options = new(StringComparer.Ordinal);

        foreach ((string optionId, object value) in selections)
        {
            options.Add(optionId, JsonSerializer.SerializeToElement(value));
        }

        return new MirroringPreferences { Options = options };
    }
}
