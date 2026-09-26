namespace ScrcpySeamless.Infrastructure.Runtime;

/** Selects compatibility settings only for the reviewed Windows ADB 34.0.5 binary. */
public static class BundledAdbCompatibility
{
    private const string WindowsAdb3405Sha256 =
        "58765259A349CCE392FBB2F15DAB75FED3B7C0B40CC68A7653278B9850602A2F";

    /** Enables Openscreen only for the exact ADB hash from a validated runtime manifest. */
    public static bool RequiresOpenScreenMdns(string adbSha256)
    {
        return string.Equals(adbSha256, WindowsAdb3405Sha256, StringComparison.OrdinalIgnoreCase);
    }
}
