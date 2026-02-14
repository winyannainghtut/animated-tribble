using System.Diagnostics;
using System.Text;

namespace CodexBar.Core.HostAPIs;

public sealed class ConPtyApi : IPtyApi
{
    public async Task<PtyCommandResult> RunAsync(PtyCommandRequest request, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            Arguments = request.Arguments,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = request.WorkingDirectory ?? Environment.CurrentDirectory
        };

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var sync = new object();
        var sentRules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is null)
            {
                return;
            }

            lock (sync)
            {
                stdout.AppendLine(args.Data);
                TryApplySendRules(process, stdout.ToString(), request.SendOnSubstring, sentRules);
            }
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is null)
            {
                return;
            }

            lock (sync)
            {
                stderr.AppendLine(args.Data);
            }
        };

        var started = process.Start();
        if (!started)
        {
            throw new InvalidOperationException($"Failed to start process '{request.FileName}'.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!string.IsNullOrWhiteSpace(request.InitialInput))
        {
            await process.StandardInput.WriteAsync(request.InitialInput.AsMemory(), cancellationToken).ConfigureAwait(false);
            await process.StandardInput.FlushAsync().ConfigureAwait(false);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(request.Timeout);

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

        return new PtyCommandResult(
            ExitCode: timedOut ? -1 : process.ExitCode,
            StdOut: stdout.ToString(),
            StdErr: stderr.ToString(),
            TimedOut: timedOut);
    }

    private static void TryApplySendRules(
        Process process,
        string currentStdOut,
        IReadOnlyList<PtySendRule>? rules,
        HashSet<string> sentRules)
    {
        if (rules is null || rules.Count == 0)
        {
            return;
        }

        foreach (var rule in rules)
        {
            if (sentRules.Contains(rule.TriggerSubstring))
            {
                continue;
            }

            if (currentStdOut.Contains(rule.TriggerSubstring, StringComparison.OrdinalIgnoreCase))
            {
                process.StandardInput.Write(rule.InputToSend);
                process.StandardInput.Flush();
                sentRules.Add(rule.TriggerSubstring);
            }
        }
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best effort kill.
        }
    }
}
