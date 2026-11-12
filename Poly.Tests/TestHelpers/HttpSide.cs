using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Poly.Tests.TestHelpers;

/// <summary>What one HTTP exchange answered: the status, the <c>Location</c> header when present, the body parsed
/// as JSON when it is JSON, and the raw body text.</summary>
public sealed record HttpOutcome(int Status, string? Location, JsonElement? Json, string Raw);

/// <summary>
/// A thin typed wrapper over the client of a booted <see cref="PackOracle"/>: create, read, list and invoke an
/// action by route. It is not a <c>ParitySide</c>; the parity side over HTTP belongs to Q-08.
/// </summary>
public sealed class HttpSide(HttpClient client) {
    public HttpClient Client { get; } = client;

    public static HttpSide On(PackOracle oracle) {
        ArgumentNullException.ThrowIfNull(oracle);
        return new HttpSide(oracle.Client);
    }

    public Task<HttpOutcome> CreateAsync(string route, JsonObject body) => SendAsync(HttpMethod.Post, route, body);

    public Task<HttpOutcome> GetAsync(string path) => SendAsync(HttpMethod.Get, path, null);

    public Task<HttpOutcome> ListAsync(string route) => SendAsync(HttpMethod.Get, route, null);

    public Task<HttpOutcome> InvokeAsync(string route, string action, JsonObject? arguments = null) =>
        SendAsync(HttpMethod.Post, $"{route.TrimEnd('/')}/{action}", arguments ?? []);

    private async Task<HttpOutcome> SendAsync(HttpMethod method, string path, JsonObject? body) {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.RelativeOrAbsolute));
        if (body is not null)
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await Client.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();
        JsonElement? json = null;
        try {
            using var document = JsonDocument.Parse(raw);
            json = document.RootElement.Clone();
        }
        catch (JsonException) {
            // not JSON: the raw text is the only body
        }
        return new HttpOutcome((int)response.StatusCode, response.Headers.Location?.ToString(), json, raw);
    }
}