namespace CodexBar.Core.HostAPIs;

public sealed record PtySendRule(string TriggerSubstring, string InputToSend);

public sealed record PtyCommandRequest(
    string FileName,
    string Arguments,
    string? InitialInput,
    IReadOnlyList<PtySendRule>? SendOnSubstring,
    TimeSpan Timeout,
    string? WorkingDirectory = null
);

public sealed record PtyCommandResult(
    int ExitCode,
    string StdOut,
    string StdErr,
    bool TimedOut
);

public interface IPtyApi
{
    Task<PtyCommandResult> RunAsync(PtyCommandRequest request, CancellationToken cancellationToken);
}
