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
