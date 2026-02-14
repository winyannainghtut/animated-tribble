using System.Net;
using System.Text.Json;

namespace CodexBar.Core.HostAPIs;

public sealed record HttpApiRequest(
    HttpMethod Method,
    Uri Uri,
    string? Body,
    IReadOnlyDictionary<string, string>? Headers,
    TimeSpan? Timeout
);

public sealed record HttpApiResponse(
    HttpStatusCode StatusCode,
    string Body,
    IReadOnlyDictionary<string, string> Headers
);

public interface IHttpApi
{
    Task<HttpApiResponse> SendAsync(HttpApiRequest request, CancellationToken cancellationToken);
    Task<JsonDocument> GetJsonAsync(Uri uri, IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken);
}
