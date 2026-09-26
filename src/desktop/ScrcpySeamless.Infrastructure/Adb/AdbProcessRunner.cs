using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace ScrcpySeamless.Infrastructure.Adb;

public sealed record AdbProcessResult(int ExitCode, string StandardOutput, string StandardError, bool OutputTruncated);

/** Executes one bounded ADB command so gateway responses can be tested without a daemon. */
public interface IAdbProcessRunner
{
    Task<AdbProcessResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout,
        CancellationToken cancellationToken, string? standardInput = null);
}

/** Owns one ADB child process and bounds its lifetime and captured output. */
public sealed class AdbProcessRunner : IAdbProcessRunner
{
    private const int MaximumOutputCharacters = 65_536;
    private static readonly TimeSpan TerminationWaitTimeout = TimeSpan.FromSeconds(10);
    private readonly string executablePath;
    private readonly IReadOnlyDictionary<string, string> childEnvironment;

    public AdbProcessRunner(string executablePath, IReadOnlyDictionary<string, string>? childEnvironment = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        this.executablePath = executablePath;
        this.childEnvironment = childEnvironment is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(childEnvironment);
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

        using CancellationTokenSource readerSource = CancellationTokenSource.CreateLinkedTokenSource(timeoutSource.Token);
        Task<(string Text, bool Truncated)>? outputTask = null;
        Task<(string Text, bool Truncated)>? errorTask = null;

        try
        {
            outputTask = ReadBoundedAsync(
                process.StandardOutput,
                readerSource.Token);
            errorTask = ReadBoundedAsync(
                process.StandardError,
                readerSource.Token);

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
            try
            {
                if (!process.HasExited)
                {
                    try
                    {
                        // ADB may launch a shared daemon. Terminate only this command process.
                        process.Kill();
                    }
                    catch (InvalidOperationException) when (process.HasExited)
                    {
                        // The child exited between the state check and termination.
                    }
                    catch (Win32Exception) when (process.HasExited)
                    {
                        // The child exited between the state check and termination.
                    }

                    using CancellationTokenSource terminationSource = new(TerminationWaitTimeout);

                    try
                    {
                        await process.WaitForExitAsync(terminationSource.Token);
                    }
                    catch (OperationCanceledException) when (terminationSource.IsCancellationRequested)
                    {
                        throw new TimeoutException("ADB process did not exit after termination.");
                    }
                }
            }
            finally
            {
                await readerSource.CancelAsync();

                if (outputTask is not null && errorTask is not null)
                {
                    try
                    {
                        await Task.WhenAll(outputTask, errorTask).WaitAsync(TerminationWaitTimeout);
                    }
                    catch (OperationCanceledException) when (readerSource.IsCancellationRequested)
                    {
                        // Both canceled readers have completed before the process streams are disposed.
                    }
                }
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
        foreach (string argument in arguments)
        {
            ArgumentNullException.ThrowIfNull(argument);
            startInfo.ArgumentList.Add(argument);
        }

        // Runtime composition supplies version-specific child settings; the runner does not choose ADB policy.
        foreach ((string name, string value) in childEnvironment)
        {
            startInfo.Environment[name] = value;
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
