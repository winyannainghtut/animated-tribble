using System.Text.Json;
using CodexBar.Core.HostAPIs;
using CodexBar.Core.Models;
using CodexBar.Core.Providers.Codex;
using CodexBar.Core.Providers.Shared;
using CodexBar.Core.Utilities;

namespace CodexBar.Core.Providers;

public static class ProviderCatalog
{
    public static IReadOnlyList<IProviderDescriptor> CreateAll()
    {
        var providers = new List<IProviderDescriptor>
        {
            new CodexDescriptor(),
            BuildClaude(),
            BuildCursor(),
            BuildGemini(),
            BuildGeneric(ProviderId.Antigravity, "Antigravity", "Session", "Monthly", false, false, "#0EA5E9"),
            BuildGeneric(ProviderId.Droid, "Droid", "Session", "Monthly", false, false, "#22C55E"),
            BuildGeneric(ProviderId.Copilot, "GitHub Copilot", "Session", "Monthly", false, false, "#7C3AED"),
            BuildGeneric(ProviderId.Zai, "z.ai", "Session", "Monthly", true, false, "#EF4444"),
            BuildGeneric(ProviderId.Kiro, "Kiro", "Session", "Monthly", false, false, "#0F766E"),
            BuildGeneric(ProviderId.VertexAi, "Vertex AI", "Request quota", "Daily", false, true, "#F97316"),
            BuildGeneric(ProviderId.Augment, "Augment", "Session", "Monthly", false, false, "#8B5CF6"),
            BuildGeneric(ProviderId.Amp, "Amp", "Session", "Monthly", false, false, "#EC4899"),
            BuildGeneric(ProviderId.JetBrainsAi, "JetBrains AI", "Session", "Monthly", false, false, "#2563EB"),
            BuildGeneric(ProviderId.ContinueDev, "Continue", "Session", "Monthly", false, true, "#14B8A6"),
            BuildGeneric(ProviderId.SourcegraphCody, "Cody", "Session", "Monthly", false, true, "#F59E0B"),
            BuildGeneric(ProviderId.Replit, "Replit", "Session", "Monthly", true, false, "#111827"),
            BuildGeneric(ProviderId.Aider, "Aider", "Session", "Monthly", false, true, "#059669")
        };

        return providers;
    }

    private static IProviderDescriptor BuildClaude()
    {
        var strategies = new IProviderFetchStrategy[]
        {
            new DelegateFetchStrategy(
                id: "claude.oauth-token",
                kind: FetchKind.OAuth,
                availability: static (_, _) => ValueTask.FromResult(ProcessRunner.CommandExists("claude")),
                fetch: async (context, cancellationToken) =>
                {
                    var tokenProbe = await ProcessRunner.RunAsync(
                        "claude",
                        "auth token",
                        stdin: null,
                        timeout: TimeSpan.FromSeconds(8),
                        cancellationToken).ConfigureAwait(false);

                    var token = tokenProbe.StdOut.Trim();
                    if (string.IsNullOrWhiteSpace(token))
                    {
                        return ProviderFetchResult.Failed(ProviderId.Claude, "Claude auth token unavailable from CLI.");
                    }

                    var response = await context.HttpApi.SendAsync(
                        new HttpApiRequest(
                            HttpMethod.Get,
                            new Uri("https://claude.ai/api/usage"),
                            null,
                            new Dictionary<string, string>
                            {
                                ["Authorization"] = $"Bearer {token}",
                                ["Accept"] = "application/json"
                            },
                            TimeSpan.FromSeconds(15)),
                        cancellationToken).ConfigureAwait(false);

                    if ((int)response.StatusCode >= 400)
                    {
                        return ProviderFetchResult.Failed(ProviderId.Claude, $"Claude usage endpoint returned HTTP {(int)response.StatusCode}.");
                    }

                    using var json = JsonDocument.Parse(response.Body);
                    var usage = GenericProviderParser.TryParseFromJson(
                        ProviderId.Claude,
                        json.RootElement,
                        "5-hour limit",
                        "Weekly limit",
                        context.NowUtc,
                        note: "source=oauth",
                        supportsCredits: false);

                    return usage is null
                        ? ProviderFetchResult.Failed(ProviderId.Claude, "Claude OAuth payload parse failed.")
                        : ProviderFetchResult.FromUsage(usage, Array.Empty<FetchAttempt>());
                }),
            new DelegateFetchStrategy(
                id: "claude.cookies",
                kind: FetchKind.Cookies,
                availability: async (context, cancellationToken) =>
                {
                    var cookies = await context.BrowserCookieApi.ReadCookiesAsync(
                        new BrowserCookieQuery(new[] { "claude.ai" }, BrowserKind.Any),
                        cancellationToken).ConfigureAwait(false);
                    return cookies.Count > 0;
                },
                fetch: async (context, cancellationToken) =>
                {
                    var cookies = await context.BrowserCookieApi.ReadCookiesAsync(
                        new BrowserCookieQuery(new[] { "claude.ai" }, BrowserKind.Any),
                        cancellationToken).ConfigureAwait(false);
                    if (cookies.Count == 0)
                    {
                        return ProviderFetchResult.Failed(ProviderId.Claude, "No claude.ai cookies found.");
                    }

                    var response = await context.HttpApi.SendAsync(
                        new HttpApiRequest(
                            HttpMethod.Get,
                            new Uri("https://claude.ai/api/usage"),
                            null,
                            new Dictionary<string, string>
                            {
                                ["Cookie"] = GenericProviderParser.BuildCookieHeader(cookies),
                                ["Accept"] = "application/json"
                            },
                            TimeSpan.FromSeconds(15)),
                        cancellationToken).ConfigureAwait(false);

                    if ((int)response.StatusCode >= 400)
                    {
                        return ProviderFetchResult.Failed(ProviderId.Claude, $"Claude cookie fetch returned HTTP {(int)response.StatusCode}.");
                    }

                    using var json = JsonDocument.Parse(response.Body);
                    var usage = GenericProviderParser.TryParseFromJson(
                        ProviderId.Claude,
                        json.RootElement,
                        "5-hour limit",
                        "Weekly limit",
                        context.NowUtc,
                        note: "source=cookies",
                        supportsCredits: false);

                    return usage is null
                        ? ProviderFetchResult.Failed(ProviderId.Claude, "Unable to parse Claude cookie payload.")
                        : ProviderFetchResult.FromUsage(usage, Array.Empty<FetchAttempt>());
                }),
            BuildCliTextStrategy(ProviderId.Claude, "claude", "status", "5-hour limit", "Weekly limit", supportsCredits: false)
        };

        return new ProviderDescriptor
        {
            Id = ProviderId.Claude,
            DisplayName = "Claude Code",
            SessionLabel = "5-hour limit",
            SecondaryLabel = "Weekly limit",
            SupportsCredits = false,
            SupportsTokenCost = true,
            SupportsStatusPolling = true,
            SupportsLogin = true,
            IconResourceName = "claude",
            PrimaryColor = "#D97706",
            ToggleTitle = "Show Claude usage",
            DefaultEnabled = true,
            FetchStrategies = strategies
        };
    }

    private static IProviderDescriptor BuildCursor()
    {
        var cookieStrategy = new DelegateFetchStrategy(
            id: "cursor.cookies",
            kind: FetchKind.Cookies,
            availability: async (context, cancellationToken) =>
            {
                var cookies = await context.BrowserCookieApi.ReadCookiesAsync(
                    new BrowserCookieQuery(new[] { "cursor.sh", "cursor.com" }, BrowserKind.Any),
                    cancellationToken).ConfigureAwait(false);
                return cookies.Count > 0;
            },
            fetch: async (context, cancellationToken) =>
            {
                var cookies = await context.BrowserCookieApi.ReadCookiesAsync(
                    new BrowserCookieQuery(new[] { "cursor.sh", "cursor.com" }, BrowserKind.Any),
                    cancellationToken).ConfigureAwait(false);
                if (cookies.Count == 0)
                {
                    return ProviderFetchResult.Failed(ProviderId.Cursor, "No Cursor cookies found.");
                }

                var response = await context.HttpApi.SendAsync(
                    new HttpApiRequest(
                        HttpMethod.Get,
                        new Uri("https://www.cursor.com/api/dashboard/usage"),
                        null,
                        new Dictionary<string, string>
                        {
                            ["Cookie"] = GenericProviderParser.BuildCookieHeader(cookies),
                            ["Accept"] = "application/json"
                        },
                        TimeSpan.FromSeconds(15)),
                    cancellationToken).ConfigureAwait(false);

                if ((int)response.StatusCode >= 400)
                {
                    return ProviderFetchResult.Failed(ProviderId.Cursor, $"Cursor usage endpoint HTTP {(int)response.StatusCode}");
                }

                using var json = JsonDocument.Parse(response.Body);
                var usage = GenericProviderParser.TryParseFromJson(
                    ProviderId.Cursor,
                    json.RootElement,
                    "Session limit",
                    "Monthly limit",
                    context.NowUtc,
                    note: "source=cookies",
                    supportsCredits: false);

                return usage is null
                    ? ProviderFetchResult.Failed(ProviderId.Cursor, "Unable to parse Cursor usage payload.")
                    : ProviderFetchResult.FromUsage(usage, Array.Empty<FetchAttempt>());
            });

        return new ProviderDescriptor
        {
            Id = ProviderId.Cursor,
            DisplayName = "Cursor",
            SessionLabel = "Session limit",
            SecondaryLabel = "Monthly limit",
            SupportsCredits = false,
            SupportsTokenCost = false,
            SupportsStatusPolling = true,
            SupportsLogin = true,
            IconResourceName = "cursor",
            PrimaryColor = "#0284C7",
            ToggleTitle = "Show Cursor usage",
            DefaultEnabled = true,
            FetchStrategies = new[] { cookieStrategy }
        };
    }

    private static IProviderDescriptor BuildGemini()
    {
        var strategies = new IProviderFetchStrategy[]
        {
            new DelegateFetchStrategy(
                id: "gemini.gcloud",
                kind: FetchKind.OAuth,
                availability: static (_, _) => ValueTask.FromResult(ProcessRunner.CommandExists("gcloud")),
                fetch: async (context, cancellationToken) =>
                {
                    var tokenResult = await ProcessRunner.RunAsync(
                        "gcloud",
                        "auth print-access-token",
                        stdin: null,
                        timeout: TimeSpan.FromSeconds(10),
                        cancellationToken).ConfigureAwait(false);

                    var token = tokenResult.StdOut.Trim();
                    if (string.IsNullOrWhiteSpace(token))
                    {
                        return ProviderFetchResult.Failed(ProviderId.Gemini, "gcloud access token unavailable.");
                    }

                    var response = await context.HttpApi.SendAsync(
                        new HttpApiRequest(
                            HttpMethod.Get,
                            new Uri("https://generativelanguage.googleapis.com/v1beta/models"),
                            null,
                            new Dictionary<string, string>
                            {
                                ["Authorization"] = $"Bearer {token}",
                                ["Accept"] = "application/json"
                            },
                            TimeSpan.FromSeconds(15)),
                        cancellationToken).ConfigureAwait(false);

                    if ((int)response.StatusCode >= 400)
                    {
                        return ProviderFetchResult.Failed(ProviderId.Gemini, $"Gemini API request failed with HTTP {(int)response.StatusCode}");
                    }

                    var usage = new ProviderUsageSnapshot(
                        ProviderId.Gemini,
                        new UsageWindowSnapshot("Project quota", 0, null, null),
                        new UsageWindowSnapshot("Daily quota", 0, null, null),
                        Credits: null,
                        EstimatedTokenCostUsd: null,
                        IsStale: true,
                        FetchedAtUtc: context.NowUtc,
                        IncidentSummary: null,
                        Notes: new[] { "source=gcloud", "Gemini quota APIs require project-specific configuration." });

                    return ProviderFetchResult.FromUsage(usage, Array.Empty<FetchAttempt>());
                }),
            BuildCliTextStrategy(ProviderId.Gemini, "gemini", "quota", "Project quota", "Daily quota", supportsCredits: false)
        };

        return new ProviderDescriptor
        {
            Id = ProviderId.Gemini,
            DisplayName = "Gemini",
            SessionLabel = "Project quota",
            SecondaryLabel = "Daily quota",
            SupportsCredits = false,
            SupportsTokenCost = true,
            SupportsStatusPolling = true,
            SupportsLogin = true,
            IconResourceName = "gemini",
            PrimaryColor = "#2563EB",
            ToggleTitle = "Show Gemini usage",
            DefaultEnabled = false,
            FetchStrategies = strategies
        };
    }

    private static IProviderDescriptor BuildGeneric(
        ProviderId providerId,
        string displayName,
        string sessionLabel,
        string secondaryLabel,
        bool supportsCredits,
        bool supportsTokenCost,
        string color)
    {
        var command = providerId.ToString().ToLowerInvariant();
        var strategies = new IProviderFetchStrategy[]
        {
            BuildApiTokenPlaceholder(providerId, sessionLabel, secondaryLabel, supportsCredits),
            BuildCliTextStrategy(providerId, command, "status", sessionLabel, secondaryLabel, supportsCredits)
        };

        return new ProviderDescriptor
        {
            Id = providerId,
            DisplayName = displayName,
            SessionLabel = sessionLabel,
            SecondaryLabel = secondaryLabel,
            SupportsCredits = supportsCredits,
            SupportsTokenCost = supportsTokenCost,
            SupportsStatusPolling = false,
            SupportsLogin = true,
            IconResourceName = providerId.ToString().ToLowerInvariant(),
            PrimaryColor = color,
            ToggleTitle = $"Show {displayName} usage",
            DefaultEnabled = false,
            FetchStrategies = strategies
        };
    }

    private static IProviderFetchStrategy BuildApiTokenPlaceholder(
        ProviderId providerId,
        string sessionLabel,
        string secondaryLabel,
        bool supportsCredits)
    {
        return new DelegateFetchStrategy(
            id: $"{providerId.ToString().ToLowerInvariant()}.api-token",
            kind: FetchKind.ApiToken,
            availability: async (context, cancellationToken) =>
            {
                var secret = await context.CredentialStore
                    .ReadSecretAsync($"CodexBar:{providerId}:api-token", cancellationToken)
                    .ConfigureAwait(false);
                return !string.IsNullOrWhiteSpace(secret);
            },
            fetch: async (context, cancellationToken) =>
            {
                var secret = await context.CredentialStore
                    .ReadSecretAsync($"CodexBar:{providerId}:api-token", cancellationToken)
                    .ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(secret))
                {
                    return ProviderFetchResult.Failed(providerId, "API token not configured in Credential Manager.");
                }

                var staleUsage = new ProviderUsageSnapshot(
                    providerId,
                    new UsageWindowSnapshot(sessionLabel, 0, null, null),
                    new UsageWindowSnapshot(secondaryLabel, 0, null, null),
                    supportsCredits ? 0m : null,
                    EstimatedTokenCostUsd: null,
                    IsStale: true,
                    FetchedAtUtc: context.NowUtc,
                    IncidentSummary: null,
                    Notes: new[] { "source=api-token", "Provider endpoint integration pending provider-specific API contract." });

                return ProviderFetchResult.FromUsage(staleUsage, Array.Empty<FetchAttempt>());
            });
    }

    private static IProviderFetchStrategy BuildCliTextStrategy(
        ProviderId providerId,
        string command,
        string arguments,
        string sessionLabel,
        string secondaryLabel,
        bool supportsCredits)
    {
        return new DelegateFetchStrategy(
            id: $"{providerId.ToString().ToLowerInvariant()}.cli",
            kind: FetchKind.Cli,
            availability: (_, _) => ValueTask.FromResult(ProcessRunner.CommandExists(command)),
            fetch: async (context, cancellationToken) =>
            {
                var result = await context.PtyApi.RunAsync(
                    new PtyCommandRequest(command, arguments, null, null, TimeSpan.FromSeconds(12)),
                    cancellationToken).ConfigureAwait(false);

                if (result.TimedOut)
                {
                    return ProviderFetchResult.Failed(providerId, $"{command} {arguments} timed out.");
                }

                var combined = string.Join(Environment.NewLine, result.StdOut, result.StdErr);
                var usage = GenericProviderParser.TryParseFromText(
                    providerId,
                    combined,
                    sessionLabel,
                    secondaryLabel,
                    context.NowUtc,
                    note: "source=cli",
                    supportsCredits: supportsCredits);

                return usage is null
                    ? ProviderFetchResult.Failed(providerId, $"Unable to parse output from '{command} {arguments}'.")
                    : ProviderFetchResult.FromUsage(usage, Array.Empty<FetchAttempt>());
            });
    }
}
