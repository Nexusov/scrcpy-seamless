using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Xunit;

namespace ScrcpySeamless.IpcProcess.Tests;

/// <summary>Checks the real native entry point only on paths that reject before device startup.</summary>
public sealed class ProductionNativeBootstrapTests
{
    private static readonly TimeSpan ProcessLimit = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CleanupLimit = TimeSpan.FromSeconds(5);
    private const int OutputLimitBytes = 2 * 1024 * 1024;
    private const int ErrorLimitBytes = 64 * 1024;

    /// <summary>Ordinary CLI requests finish with closed stdin and retain human-readable stdout.</summary>
    [Theory]
    [InlineData("--version")]
    [InlineData("--help")]
    public async Task OrdinaryCliDoesNotWaitForMachineHandshake(string argument)
    {
        NativeResult result = await RunNativeAsync([argument]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("scrcpy", Encoding.UTF8.GetString(result.StandardOutput).ToLowerInvariant());
    }

    /// <summary>Machine selection without the reviewed bootstrap fields emits no protocol bytes.</summary>
    [Fact]
    public async Task IncompleteMachineBootstrapFailsBeforeHandshake()
    {
        NativeResult result = await RunNativeAsync(["--seamless-machine"]);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Empty(result.StandardOutput);
        Assert.Contains("Incomplete or unexpected managed machine bootstrap", result.StandardError);
    }

    /// <summary>A live PID with a wrong creation time is not accepted as the owning parent.</summary>
    [Fact]
    public async Task WrongParentCreationTimeRejectsBeforeHandshake()
    {
        using Process parent = Process.GetCurrentProcess();
        string[] arguments = MachineArguments(parent.Id, parent.StartTime.ToFileTimeUtc() + 1);
        NativeResult result = await RunNativeAsync(arguments);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Empty(result.StandardOutput);
    }

    /// <summary>An incompatible hello receives only a framed rejection and exits before device startup.</summary>
    [Fact]
    public async Task IncompatibleHelloRejectsBeforeNativeReady()
    {
        using Process parent = Process.GetCurrentProcess();
        string[] arguments = MachineArguments(parent.Id, parent.StartTime.ToFileTimeUtc());
        const string hello = "{\"messageType\":\"hello\",\"product\":\"different-product\",\"protocolMajor\":1,\"protocolMinor\":0,\"requiredCapabilities\":[],\"supportedCapabilities\":[]}";
        byte[] payload = Encoding.UTF8.GetBytes(hello);
        byte[] frame = new byte[4 + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)payload.Length);
        payload.CopyTo(frame.AsSpan(4));

        NativeResult result = await RunNativeAsync(arguments, frame);

        Assert.NotEqual(0, result.ExitCode);
        Assert.True(result.StandardOutput.Length >= 4);
        uint responseLength = BinaryPrimitives.ReadUInt32LittleEndian(result.StandardOutput);
        Assert.Equal((uint)(result.StandardOutput.Length - 4), responseLength);
        using JsonDocument response = JsonDocument.Parse(result.StandardOutput.AsMemory(4));
        Assert.Equal("helloResult", response.RootElement.GetProperty("messageType").GetString());
        Assert.Equal("productMismatch", response.RootElement.GetProperty("status").GetString());
    }

    /// <summary>Supplies a valid session and the exact parent identity to the production option parser.</summary>
    private static string[] MachineArguments(int parentPid, long parentCreated) =>
    [
        "--seamless-machine",
        "--seamless-session-id=" + Guid.NewGuid().ToString("D"),
        "--seamless-parent-pid=" + parentPid,
        "--seamless-parent-created=" + parentCreated,
    ];

    /// <summary>Runs only explicitly bounded bootstrap paths and settles the exact started process.</summary>
    private static async Task<NativeResult> RunNativeAsync(string[] arguments, byte[]? standardInput = null)
    {
        string executable = RequireProductionNative();
        string testRoot = Environment.GetEnvironmentVariable("SCRCPY_IPC_TEST_ROOT") ??
            throw new DirectoryNotFoundException("The isolated IPC test root is required.");
        string absentAdb = Path.Combine(testRoot, "absent-adb-" + Guid.NewGuid().ToString("N") + ".exe");
        ProcessStartInfo startInfo = new()
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        // A failed rejection must still be unable to launch the shared ADB client.
        startInfo.Environment["ADB"] = absentAdb;
        startInfo.Environment["SCRCPY_SERVER_PATH"] = Path.Combine(testRoot,
            "absent-server-" + Guid.NewGuid().ToString("N"));

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new() { StartInfo = startInfo };
        Assert.True(process.Start());
        using CancellationTokenSource deadline = new(ProcessLimit);

        try
        {
            Task<byte[]> outputTask = ReadBoundedAsync(process.StandardOutput.BaseStream,
                OutputLimitBytes, deadline.Token);
            Task<byte[]> errorTask = ReadBoundedAsync(process.StandardError.BaseStream,
                ErrorLimitBytes, deadline.Token);
            if (standardInput is not null)
            {
                await process.StandardInput.BaseStream.WriteAsync(standardInput, deadline.Token);
            }

            process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token);
            byte[][] streams = await Task.WhenAll(outputTask, errorTask).WaitAsync(deadline.Token);
            return new NativeResult(process.ExitCode, streams[0], Encoding.UTF8.GetString(streams[1]));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill();
                await process.WaitForExitAsync().WaitAsync(CleanupLimit);
            }
        }
    }

    /// <summary>Bounds test memory even if the native entry point violates its expected output path.</summary>
    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int limit, CancellationToken token)
    {
        using MemoryStream output = new();
        byte[] buffer = new byte[4096];
        int count;
        while ((count = await stream.ReadAsync(buffer, token)) != 0)
        {
            if (output.Length + count > limit)
            {
                throw new InvalidDataException("Native bootstrap output exceeded the test limit.");
            }

            output.Write(buffer, 0, count);
        }

        return output.ToArray();
    }

    /// <summary>Fails rather than silently skipping the real executable when CI omitted it.</summary>
    private static string RequireProductionNative()
    {
        string? path = Environment.GetEnvironmentVariable("SCRCPY_IPC_PRODUCTION_NATIVE");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) ||
            !string.Equals(Path.GetFileName(path), "scrcpy.exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new FileNotFoundException("Set SCRCPY_IPC_PRODUCTION_NATIVE to the built scrcpy.exe.", path);
        }

        return Path.GetFullPath(path);
    }

    /// <summary>Retains binary stdout separately from human diagnostics.</summary>
    private sealed record NativeResult(int ExitCode, byte[] StandardOutput, string StandardError);
}
