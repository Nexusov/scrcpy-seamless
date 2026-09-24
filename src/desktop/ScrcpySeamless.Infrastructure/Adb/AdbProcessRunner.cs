using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace ScrcpySeamless.Infrastructure.Adb;

public sealed record AdbProcessResult(int ExitCode, string StandardOutput, string StandardError, bool OutputTruncated);

/** Owns one ADB child process and bounds its lifetime and captured output. */
public sealed class AdbProcessRunner
{
    private const int MaximumOutputCharacters = 65_536;
    private readonly string executablePath;

    public AdbProcessRunner(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        this.executablePath = executablePath;
    }

    public async Task<AdbProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        string? standardInput = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        cancellationToken.ThrowIfCancellationRequested();
        using Process process = new() { StartInfo = CreateStartInfo(arguments, standardInput is not null) };
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        if (!process.Start())
        {
            throw new InvalidOperationException("ADB process did not start.");
        }

        try
        {
            Task<(string Text, bool Truncated)> outputTask = ReadBoundedAsync(
                process.StandardOutput,
                timeoutSource.Token);
            Task<(string Text, bool Truncated)> errorTask = ReadBoundedAsync(
                process.StandardError,
                timeoutSource.Token);

            if (standardInput is not null)
            {
                await process.StandardInput.WriteLineAsync(standardInput.AsMemory(), timeoutSource.Token);
                process.StandardInput.Close();
            }

            Task exitTask = process.WaitForExitAsync(timeoutSource.Token);
            await Task.WhenAll(outputTask, errorTask, exitTask);
            (string output, bool outputTruncated) = await outputTask;
            (string error, bool errorTruncated) = await errorTask;
            return new AdbProcessResult(process.ExitCode, output, error, outputTruncated || errorTruncated);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("ADB operation timed out.");
        }
        finally
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) when (process.HasExited)
                {
                    // The child exited between the state check and termination.
                }
                catch (Win32Exception) when (process.HasExited)
                {
                    // The child exited between the state check and termination.
                }

                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    /** Builds a direct child-process invocation without shell interpolation. */
    internal ProcessStartInfo CreateStartInfo(IReadOnlyList<string> arguments, bool redirectStandardInput = false)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = executablePath,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executablePath))!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = redirectStandardInput,
        };
        startInfo.Environment["ADB_MDNS_OPENSCREEN"] = "1";

        foreach (string argument in arguments)
        {
            ArgumentNullException.ThrowIfNull(argument);
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    /** Drains a pipe even after the capture cap to avoid blocking the child. */
    private static async Task<(string Text, bool Truncated)> ReadBoundedAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        char[] buffer = new char[4_096];
        StringBuilder output = new();
        bool truncated = false;

        while (true)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);

            if (read == 0)
            {
                break;
            }

            int remaining = MaximumOutputCharacters - output.Length;
            int captured = Math.Min(read, remaining);

            if (captured > 0)
            {
                output.Append(buffer, 0, captured);
            }

            truncated |= captured != read;
        }

        return (output.ToString(), truncated);
    }
}
