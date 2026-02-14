using System.Diagnostics;
using System.Text;

namespace CodexBar.Core.Utilities;

public sealed record ProcessRunResult(int ExitCode, string StdOut, string StdErr, bool TimedOut);

public static class ProcessRunner
{
    public static async Task<ProcessRunResult> RunAsync(
        string fileName,
        string arguments,
        string? stdin,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Unable to start command: {fileName} {arguments}");
        }

        if (!string.IsNullOrEmpty(stdin))
        {
            await process.StandardInput.WriteAsync(stdin.AsMemory(), cancellationToken).ConfigureAwait(false);
            await process.StandardInput.FlushAsync().ConfigureAwait(false);
        }

        process.StandardInput.Close();

        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
            TryKillProcessTree(process);
        }

        var stdout = await stdOutTask.ConfigureAwait(false);
        var stderr = await stdErrTask.ConfigureAwait(false);

        return new ProcessRunResult(timedOut ? -1 : process.ExitCode, stdout, stderr, timedOut);
    }

    public static bool CommandExists(string command)
    {
        try
        {
            var checker = OperatingSystem.IsWindows() ? "where" : "which";
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = checker,
                Arguments = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return false;
            }

            process.WaitForExit(1500);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<IReadOnlyList<string>> RunLinesAsync(
        string fileName,
        string arguments,
        string? stdin,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var result = await RunAsync(fileName, arguments, stdin, timeout, cancellationToken).ConfigureAwait(false);
        if (result.TimedOut)
        {
            return Array.Empty<string>();
        }

        return result.StdOut
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best effort.
        }
    }
}
