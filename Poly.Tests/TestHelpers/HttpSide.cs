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

    public Task<HttpOutcome> CreateAsync(string route, JsonObject body) => Task.FromResult(new HttpOutcome(0, null, null, ""));

    public Task<HttpOutcome> GetAsync(string path) => Task.FromResult(new HttpOutcome(0, null, null, ""));

    public Task<HttpOutcome> ListAsync(string route) => Task.FromResult(new HttpOutcome(0, null, null, ""));

    public Task<HttpOutcome> InvokeAsync(string route, string action, JsonObject? arguments = null) =>
        Task.FromResult(new HttpOutcome(0, null, null, ""));
}