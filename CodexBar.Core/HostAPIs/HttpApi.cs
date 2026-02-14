using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CodexBar.Core.HostAPIs;

public sealed class HttpApi : IHttpApi, IDisposable
{
    private readonly HttpClient _client;
    private readonly HashSet<string> _allowlistedDomains;
    private readonly bool _ownsClient;

    public HttpApi(HttpClient? httpClient = null, IEnumerable<string>? allowlistedDomains = null)
    {
        _client = httpClient ?? new HttpClient();
        _ownsClient = httpClient is null;
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CodexBar", "1.0"));

        _allowlistedDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var domain in DefaultAllowlist())
        {
            _allowlistedDomains.Add(domain);
        }

        if (allowlistedDomains is not null)
        {
            foreach (var domain in allowlistedDomains)
            {
                _allowlistedDomains.Add(domain);
            }
        }
    }

    public async Task<HttpApiResponse> SendAsync(HttpApiRequest request, CancellationToken cancellationToken)
    {
        EnsureAllowlisted(request.Uri);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (request.Timeout.HasValue)
        {
            linkedCts.CancelAfter(request.Timeout.Value);
        }

        using var message = new HttpRequestMessage(request.Method, request.Uri);

        if (!string.IsNullOrWhiteSpace(request.Body))
        {
            message.Content = new StringContent(request.Body, Encoding.UTF8, "application/json");
        }

        if (request.Headers is not null)
        {
            foreach (var header in request.Headers)
            {
                if (!message.Headers.TryAddWithoutValidation(header.Key, header.Value))
                {
                    message.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
        }

        using var response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);

        var headers = response.Headers
            .Concat(response.Content.Headers)
            .ToDictionary(
                keySelector: static h => h.Key,
                elementSelector: static h => string.Join(",", h.Value),
                comparer: StringComparer.OrdinalIgnoreCase);

        return new HttpApiResponse(response.StatusCode, body, headers);
    }

    public async Task<JsonDocument> GetJsonAsync(Uri uri, IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
    {
        var response = await SendAsync(
            new HttpApiRequest(
                HttpMethod.Get,
                uri,
                Body: null,
                Headers: headers,
                Timeout: TimeSpan.FromSeconds(20)),
            cancellationToken).ConfigureAwait(false);

        if ((int)response.StatusCode >= 400)
        {
            throw new HttpRequestException($"HTTP {(int)response.StatusCode} from {uri.Host}: {response.Body}");
        }

        return JsonDocument.Parse(response.Body);
    }

    private void EnsureAllowlisted(Uri uri)
    {
        var host = uri.Host;
        if (_allowlistedDomains.Any(domain =>
                host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith($".{domain}", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        throw new InvalidOperationException($"HTTP domain is not allowlisted: {host}");
    }

    private static IEnumerable<string> DefaultAllowlist()
    {
        yield return "chatgpt.com";
        yield return "openai.com";
        yield return "claude.ai";
        yield return "cursor.sh";
        yield return "cursor.com";
        yield return "googleapis.com";
        yield return "status.openai.com";
        yield return "status.anthropic.com";
        yield return "www.githubstatus.com";
        yield return "status.cursor.com";
        yield return "status.cloud.google.com";
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }
}
