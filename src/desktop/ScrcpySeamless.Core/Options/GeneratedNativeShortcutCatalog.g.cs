// DO NOT EDIT - generated from spec/options/native-shortcuts.json.
namespace ScrcpySeamless.Core.Options;

public static class GeneratedNativeShortcutCatalog
{
    public static IReadOnlyList<NativeShortcutDefinition> Actions { get; } = Array.AsReadOnly(new NativeShortcutDefinition[]
    {
        new("home", "navigation", NativeShortcutContext.Display, 0, "device", Array.AsReadOnly(new[] { "MOD\u002BH" })),
        new("back", "navigation", NativeShortcutContext.Display, 0, "device", Array.AsReadOnly(new[] { "MOD\u002BB", "MOD\u002BBackspace" })),
        new("recent", "navigation", NativeShortcutContext.Display, 0, "device", Array.AsReadOnly(new[] { "MOD\u002BS" })),
        new("menu", "navigation", NativeShortcutContext.Display, 0, "device", Array.AsReadOnly(new[] { "MOD\u002BM" })),
        new("notifications", "navigation", NativeShortcutContext.Display, 1, "device", Array.AsReadOnly(new[] { "MOD\u002BN" })),
        new("quickSettings", "navigation", NativeShortcutContext.Display, 2, "device", Array.AsReadOnly(new[] { "MOD\u002BN" })),
        new("collapsePanels", "navigation", NativeShortcutContext.Display, 0, "device", Array.AsReadOnly(new[] { "MOD\u002BShift\u002BN" })),
        new("quitMirror", "window", NativeShortcutContext.Any, 0, "", Array.AsReadOnly(new[] { "MOD\u002BQ", "MOD\u002BShift\u002BQ" })),
        new("fullscreen", "window", NativeShortcutContext.Any, 0, "", Array.AsReadOnly(new[] { "MOD\u002BF", "F11" })),
        new("rotateLeft", "window", NativeShortcutContext.Any, 0, "video", Array.AsReadOnly(new[] { "MOD\u002BLeft" })),
        new("rotateRight", "window", NativeShortcutContext.Any, 0, "video", Array.AsReadOnly(new[] { "MOD\u002BRight" })),
        new("flipHorizontal", "window", NativeShortcutContext.Any, 0, "video", Array.AsReadOnly(new[] { "MOD\u002BShift\u002BLeft", "MOD\u002BShift\u002BRight" })),
        new("flipVertical", "window", NativeShortcutContext.Any, 0, "video", Array.AsReadOnly(new[] { "MOD\u002BShift\u002BUp", "MOD\u002BShift\u002BDown" })),
        new("pause", "window", NativeShortcutContext.Any, 0, "video", Array.AsReadOnly(new[] { "MOD\u002BZ" })),
        new("unpause", "window", NativeShortcutContext.Any, 0, "video", Array.AsReadOnly(new[] { "MOD\u002BShift\u002BZ" })),
        new("resetVideo", "window", NativeShortcutContext.Any, 0, "device", Array.AsReadOnly(new[] { "MOD\u002BShift\u002BR" })),
        new("pixelPerfect", "window", NativeShortcutContext.Any, 0, "video", Array.AsReadOnly(new[] { "MOD\u002BG" })),
        new("removeBorders", "window", NativeShortcutContext.Any, 0, "video", Array.AsReadOnly(new[] { "MOD\u002BW" })),
        new("fps", "window", NativeShortcutContext.Any, 0, "stdout", Array.AsReadOnly(new[] { "MOD\u002BI" })),
        new("rotateDevice", "deviceScreen", NativeShortcutContext.Display, 0, "device", Array.AsReadOnly(new[] { "MOD\u002BR" })),
        new("powerKey", "deviceScreen", NativeShortcutContext.Display, 0, "device", Array.AsReadOnly(new[] { "MOD\u002BP" })),
        new("screenOff", "deviceScreen", NativeShortcutContext.Display, 0, "device", Array.AsReadOnly(new[] { "MOD\u002BO" })),
        new("screenOn", "deviceScreen", NativeShortcutContext.Display, 0, "device", Array.AsReadOnly(new[] { "MOD\u002BShift\u002BO" })),
        new("volumeUp", "deviceScreen", NativeShortcutContext.Display, 0, "deviceNonCamera", Array.AsReadOnly(new[] { "MOD\u002BUp" })),
        new("volumeDown", "deviceScreen", NativeShortcutContext.Display, 0, "deviceNonCamera", Array.AsReadOnly(new[] { "MOD\u002BDown" })),
        new("copy", "clipboard", NativeShortcutContext.Display, 0, "android7", Array.AsReadOnly(new[] { "MOD\u002BC" })),
        new("cut", "clipboard", NativeShortcutContext.Display, 0, "android7", Array.AsReadOnly(new[] { "MOD\u002BX" })),
        new("paste", "clipboard", NativeShortcutContext.Display, 0, "paste", Array.AsReadOnly(new[] { "MOD\u002BV" })),
        new("pasteText", "clipboard", NativeShortcutContext.Display, 0, "device", Array.AsReadOnly(new[] { "MOD\u002BShift\u002BV" })),
        new("keyboardSettings", "special", NativeShortcutContext.Display, 0, "hid", Array.AsReadOnly(new[] { "MOD\u002BK" })),
        new("torchOn", "special", NativeShortcutContext.Camera, 0, "camera", Array.AsReadOnly(new[] { "MOD\u002BT" })),
        new("torchOff", "special", NativeShortcutContext.Camera, 0, "camera", Array.AsReadOnly(new[] { "MOD\u002BShift\u002BT" })),
        new("cameraZoomIn", "special", NativeShortcutContext.Camera, 0, "camera", Array.AsReadOnly(new[] { "MOD\u002BUp" })),
        new("cameraZoomOut", "special", NativeShortcutContext.Camera, 0, "camera", Array.AsReadOnly(new[] { "MOD\u002BDown" })),
    });
}
