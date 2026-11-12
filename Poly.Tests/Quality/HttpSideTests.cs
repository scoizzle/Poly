using System.Net;
using System.Text;
using System.Text.Json.Nodes;

using Poly.Tests.TestHelpers;

namespace Poly.Tests.Quality;

/// <summary>The HTTP side over a fake handler: no host is booted.</summary>
public class HttpSideTests {
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler {
        public List<(HttpMethod Method, string Uri, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.RequestUri!.ToString(), body));
            return respond(request);
        }
    }

    private static HttpSide Side(FakeHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") });

    [Test]
    public async Task HttpSide_Create_ReadsStatusLocationAndBody() {
        var handler = new FakeHandler(_ => {
            var response = new HttpResponseMessage(HttpStatusCode.Created) {
                Content = new StringContent("""{"name":"Ann"}""", Encoding.UTF8, "application/json"),
            };
            response.Headers.Location = new Uri("/api/patrons/7", UriKind.Relative);
            return response;
        });

        var outcome = await Side(handler).CreateAsync("/api/patrons", new JsonObject { ["Name"] = "Ann" });

        await Assert.That(outcome.Status).IsEqualTo(201);
        await Assert.That(outcome.Location).IsEqualTo("/api/patrons/7");
        await Assert.That(outcome.Json!.Value.GetProperty("name").GetString()).IsEqualTo("Ann");
        await Assert.That(outcome.Raw).Contains("Ann");
        await Assert.That(handler.Requests.Single().Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(handler.Requests.Single().Body).Contains("\"Name\"");
    }

    [Test]
    public async Task HttpSide_Get_NonJsonBody_KeepsRawAndNoJson() {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound) {
            Content = new StringContent("gone", Encoding.UTF8, "text/plain"),
        });

        var outcome = await Side(handler).GetAsync("/api/patrons/9");

        await Assert.That(outcome.Status).IsEqualTo(404);
        await Assert.That(outcome.Json).IsNull();
        await Assert.That(outcome.Raw).IsEqualTo("gone");
        await Assert.That(outcome.Location).IsNull();
    }

    [Test]
    public async Task HttpSide_List_RequestsTheRouteWithGet() {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent("[]", Encoding.UTF8, "application/json"),
        });

        var outcome = await Side(handler).ListAsync("/api/patrons");

        await Assert.That(outcome.Status).IsEqualTo(200);
        await Assert.That(handler.Requests.Single().Method).IsEqualTo(HttpMethod.Get);
        await Assert.That(handler.Requests.Single().Uri).IsEqualTo("http://localhost:5000/api/patrons");
    }

    [Test]
    public async Task HttpSide_Invoke_PostsArgumentsToTheActionRoute() {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var outcome = await Side(handler).InvokeAsync("/api/warehouses/WH-001", "adjustCapacity", new JsonObject { ["delta"] = 1 });

        await Assert.That(outcome.Status).IsEqualTo(200);
        await Assert.That(handler.Requests.Single().Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(handler.Requests.Single().Uri).EndsWith("/api/warehouses/WH-001/adjustCapacity");
        await Assert.That(handler.Requests.Single().Body).Contains("\"delta\"");
    }
}