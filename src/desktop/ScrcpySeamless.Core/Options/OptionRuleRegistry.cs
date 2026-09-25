using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace ScrcpySeamless.Core.Options;

/// <summary>Named conditional rules that cannot be represented as unconditional spec relationships.</summary>
internal static class OptionRuleRegistry
{
    private const string AudioDupRule = "audio-dup-requires-playback";
    private const string CameraRule = "camera-source-rules";
    private const string RecordingRule = "recording-output-semantics";
    private const string VirtualDisplayRule = "virtual-display-rules";
    private const string SessionOutputRule = "session-output-required";
    private const string MinSizeAlignmentRule = "min-size-alignment-power-of-two";

    private static readonly IReadOnlyDictionary<string, Func<OptionContext, OptionDiagnostic?>> Validators =
        new Dictionary<string, Func<OptionContext, OptionDiagnostic?>>(StringComparer.Ordinal)
        {
            [AudioDupRule] = ValidateAudioDuplication,
            [CameraRule] = ValidateCameraSource,
            [RecordingRule] = ValidateRecording,
            [VirtualDisplayRule] = ValidateVirtualDisplay,
            [SessionOutputRule] = ValidateSessionOutput,
            [MinSizeAlignmentRule] = ValidateMinSizeAlignment,
        };

    /// <summary>Runs each referenced rule once in stable catalogue order.</summary>
    public static IReadOnlyList<OptionDiagnostic> Validate(
        IReadOnlyDictionary<string, JsonElement> selections,
        IReadOnlySet<string> activeOptions)
    {
        OptionContext context = new(selections, activeOptions);
        HashSet<string> visited = new(StringComparer.Ordinal);
        List<OptionDiagnostic> diagnostics = [];

        foreach (OptionDescriptor descriptor in GeneratedOptionCatalog.All)
        {
            if (!activeOptions.Contains(descriptor.Id))
            {
                continue;
            }

            foreach (string ruleId in descriptor.RuleIds)
            {
                if (!visited.Add(ruleId))
                {
                    continue;
                }

                OptionDiagnostic? issue = Validators[ruleId](context);

                if (issue is not null)
                {
                    diagnostics.Add(issue);
                }
            }
        }

        return diagnostics;
    }

    /// <summary>Checks audio duplication against the effective native audio source.</summary>
    private static OptionDiagnostic? ValidateAudioDuplication(OptionContext context)
    {
        if (!context.Has("audio-dup"))
        {
            return null;
        }

        string? audioSource = context.Text("audio-source");
        bool cameraDefault = audioSource is null && context.Text("video-source") == "camera";
        bool incompatibleSource = audioSource is not null && audioSource != "playback";

        return context.Has("no-audio") || cameraDefault || incompatibleSource
            ? Violation("audio-dup", AudioDupRule)
            : null;
    }

    /// <summary>Checks camera-only parameters and mutually exclusive capture choices.</summary>
    private static OptionDiagnostic? ValidateCameraSource(OptionContext context)
    {
        bool cameraSelected = context.Text("video-source") == "camera";
        bool cameraParameters = context.Has("camera-id") || context.Has("camera-facing") ||
            context.Has("camera-size") || context.Has("camera-ar") ||
            context.NonZero("camera-fps") || context.Has("camera-high-speed");

        if (cameraParameters && !cameraSelected)
        {
            return Violation("video-source", CameraRule);
        }

        if (context.Has("camera-size") && context.NonZero("max-size"))
        {
            return Violation("camera-size", CameraRule);
        }

        if (context.Has("camera-high-speed") && !context.NonZero("camera-fps"))
        {
            return Violation("camera-high-speed", CameraRule);
        }

        if (cameraSelected && (context.NonZero("display-id") || context.Has("display-ime-policy")))
        {
            return Violation("display-id", CameraRule);
        }

        return null;
    }

    /// <summary>Checks recording-specific output and format constraints.</summary>
    private static OptionDiagnostic? ValidateRecording(OptionContext context)
    {
        if (context.Has("record") && context.Has("no-video") && context.Has("no-audio"))
        {
            return Violation("record", RecordingRule);
        }

        return null;
    }

    /// <summary>Checks new-display and flexible-display preconditions.</summary>
    private static OptionDiagnostic? ValidateVirtualDisplay(OptionContext context)
    {
        bool cameraSelected = context.Text("video-source") == "camera";

        if (context.Has("new-display") && (cameraSelected || context.Has("no-video") || context.NonZero("display-id")))
        {
            return Violation("new-display", VirtualDisplayRule);
        }

        if (context.Has("flex-display") && (!context.Has("new-display") || cameraSelected ||
            context.Has("no-control") || context.Has("crop") ||
            context.NonZero("window-width") || context.NonZero("window-height")))
        {
            return Violation("flex-display", VirtualDisplayRule);
        }

        return null;
    }

    /// <summary>Rejects a session that has no video, audio, or control capability.</summary>
    private static OptionDiagnostic? ValidateSessionOutput(OptionContext context)
    {
        bool recording = context.Has("record");
        bool videoPlaybackDisabled = context.Has("no-video-playback") || context.Has("no-playback") || context.Has("no-window");
        bool audioPlaybackDisabled = context.Has("no-audio-playback") || context.Has("no-playback");
        bool videoDisabled = context.Has("no-video") || (videoPlaybackDisabled && !recording);
        bool audioDisabled = context.Has("no-audio") || (audioPlaybackDisabled && !recording);
        bool nothingToDo = videoDisabled && audioDisabled && context.Has("no-control");

        return nothingToDo ? Violation("no-control", SessionOutputRule) : null;
    }

    /// <summary>Allows only native-supported powers of two for video size alignment.</summary>
    private static OptionDiagnostic? ValidateMinSizeAlignment(OptionContext context)
    {
        string? text = context.Text("min-size-alignment");
        bool validAlignment = uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture,
            out uint alignment) && BitOperations.IsPow2(alignment);

        return validAlignment ? null : Violation("min-size-alignment", MinSizeAlignmentRule);
    }

    /// <summary>Creates a typed diagnostic for one conditional rule.</summary>
    private static OptionDiagnostic Violation(string optionId, string ruleId)
    {
        return new OptionDiagnostic(OptionDiagnosticCode.RuleViolation, optionId, ruleId);
    }

    /// <summary>Provides safe access to already selected bool/string option values.</summary>
    private sealed class OptionContext(
        IReadOnlyDictionary<string, JsonElement> selections,
        IReadOnlySet<string> activeOptions)
    {
        public bool Has(string optionId) => activeOptions.Contains(optionId);

        public string? Text(string optionId)
        {
            return selections.TryGetValue(optionId, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        public bool NonZero(string optionId)
        {
            string? text = Text(optionId);
            return text is not null && decimal.TryParse(text, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out decimal number) && number != 0;
        }
    }
}
