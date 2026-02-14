using System.Text.Json;
using CodexBar.Core.Models;
using CodexBar.Core.Utilities;

namespace CodexBar.Core.Providers.Codex;

internal static class CodexStrategies
{
    public static IReadOnlyList<IProviderFetchStrategy> Build()
    {
        return new IProviderFetchStrategy[]
        {
            BuildOauthStrategy(),
            BuildCliRpcStrategy(),
            BuildCliPtyStrategy()
        };
    }

    private static IProviderFetchStrategy BuildOauthStrategy()
    {
        return new DelegateFetchStrategy(
            id: "codex.oauth",
            kind: FetchKind.OAuth,
            availability: (context, _) =>
            {
                var authPath = GetAuthPath(context);
                var token = CodexParser.TryGetAuthToken(authPath);
                return ValueTask.FromResult(!string.IsNullOrWhiteSpace(token));
            },
            fetch: async (context, cancellationToken) =>
            {
                var authPath = GetAuthPath(context);
                var token = CodexParser.TryGetAuthToken(authPath);
                if (string.IsNullOrWhiteSpace(token))
                {
                    return ProviderFetchResult.Failed(ProviderId.Codex, "Codex auth token not available.");
                }

                var response = await context.HttpApi.SendAsync(
                    new HostAPIs.HttpApiRequest(
                        Method: HttpMethod.Get,
                        Uri: new Uri("https://chatgpt.com/backend-api/wham/usage"),
                        Body: null,
                        Headers: new Dictionary<string, string>
                        {
                            ["Authorization"] = $"Bearer {token}",
                            ["Accept"] = "application/json"
                        },
                        Timeout: TimeSpan.FromSeconds(20)),
                    cancellationToken).ConfigureAwait(false);

                if ((int)response.StatusCode >= 400)
                {
                    return ProviderFetchResult.Failed(ProviderId.Codex, $"OAuth usage request failed with HTTP {(int)response.StatusCode}");
                }

                using var json = JsonDocument.Parse(response.Body);
                var usage = CodexParser.ParseOAuthPayload(json.RootElement, context.NowUtc);
                if (usage is null)
                {
                    return ProviderFetchResult.Failed(ProviderId.Codex, "Unable to parse Codex OAuth usage payload.");
                }

                return ProviderFetchResult.FromUsage(usage, Array.Empty<FetchAttempt>());
            },
            shouldFallback: static (exception, _) => exception is not UnauthorizedAccessException);
    }

    private static IProviderFetchStrategy BuildCliRpcStrategy()
    {
        return new DelegateFetchStrategy(
            id: "codex.cli-rpc",
            kind: FetchKind.Rpc,
            availability: static (_, _) => ValueTask.FromResult(ProcessRunner.CommandExists("codex")),
            fetch: async (context, cancellationToken) =>
            {
                var rpcInput = string.Join('\n',
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"clientName\":\"CodexBar\",\"clientVersion\":\"1.0\"}}",
                    "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"account/read\"}",
                    "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"account/rateLimits/read\"}",
                    "{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"shutdown\"}",
                    string.Empty);

                var run = await ProcessRunner.RunAsync(
                    fileName: "codex",
                    arguments: "-s read-only -a untrusted app-server",
                    stdin: rpcInput,
                    timeout: TimeSpan.FromSeconds(20),
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                if (run.TimedOut)
                {
                    return ProviderFetchResult.Failed(ProviderId.Codex, "Codex CLI RPC timed out.");
                }

                if (run.ExitCode != 0 && string.IsNullOrWhiteSpace(run.StdOut))
                {
                    return ProviderFetchResult.Failed(ProviderId.Codex, $"Codex CLI RPC failed: {run.StdErr.Trim()}");
                }

                if (!TryParseRpcResult(run.StdOut, 2, out var account) || !TryParseRpcResult(run.StdOut, 3, out var limits))
                {
                    return ProviderFetchResult.Failed(ProviderId.Codex, "Unable to parse Codex CLI RPC response.");
                }

                var usage = CodexParser.ParseRpcPayload(account.Value, limits.Value, context.NowUtc);
                if (usage is null)
                {
                    return ProviderFetchResult.Failed(ProviderId.Codex, "Codex CLI RPC payload missing usage data.");
                }

                return ProviderFetchResult.FromUsage(usage, Array.Empty<FetchAttempt>());
            });
    }

    private static IProviderFetchStrategy BuildCliPtyStrategy()
    {
        return new DelegateFetchStrategy(
            id: "codex.cli-pty",
            kind: FetchKind.Cli,
            availability: static (_, _) => ValueTask.FromResult(ProcessRunner.CommandExists("codex")),
            fetch: async (context, cancellationToken) =>
            {
                var result = await context.PtyApi.RunAsync(
                    new HostAPIs.PtyCommandRequest(
                        FileName: "codex",
                        Arguments: "/status",
                        InitialInput: null,
                        SendOnSubstring: null,
                        Timeout: TimeSpan.FromSeconds(20)),
                    cancellationToken).ConfigureAwait(false);

                if (result.TimedOut)
                {
                    return ProviderFetchResult.Failed(ProviderId.Codex, "Codex CLI PTY status timed out.");
                }

                var combined = string.Join(Environment.NewLine, result.StdOut, result.StdErr).Trim();
                var usage = CodexParser.ParseStatusOutput(combined, context.NowUtc);
                if (usage is null)
                {
                    return ProviderFetchResult.Failed(ProviderId.Codex, "Unable to parse `codex /status` output.");
                }

                return ProviderFetchResult.FromUsage(usage, Array.Empty<FetchAttempt>());
            });
    }

    private static string GetAuthPath(ProviderFetchContext context)
        => Path.Combine(context.HomeDirectory, ".codex", "auth.json");

    private static bool TryParseRpcResult(string stdout, int id, out JsonElement? result)
    {
        foreach (var line in stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("{"))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                if (!doc.RootElement.TryGetProperty("id", out var idElement) || !idElement.TryGetInt32(out var parsedId) || parsedId != id)
                {
                    continue;
                }

                if (!doc.RootElement.TryGetProperty("result", out var resultElement))
                {
                    continue;
                }

                result = JsonDocument.Parse(resultElement.GetRawText()).RootElement;
                return true;
            }
            catch
            {
                // Ignore malformed lines.
            }
        }

        result = null;
        return false;
    }
}
