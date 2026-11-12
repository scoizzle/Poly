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

    [Test]
    public async Task HttpSide_NoContent_EmptyBody_HasNoJsonAndNoLocation() {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var outcome = await Side(handler).InvokeAsync("/api/tallies/a", "bump");

        await Assert.That(outcome.Status).IsEqualTo(204);
        await Assert.That(outcome.Json).IsNull();
        await Assert.That(outcome.Raw).IsEqualTo("");
        await Assert.That(outcome.Location).IsNull();
    }

    [Test]
    public async Task HttpSide_AbsoluteLocation_IsKeptAsWritten() {
        var handler = new FakeHandler(_ => {
            var response = new HttpResponseMessage(HttpStatusCode.Created);
            response.Headers.Location = new Uri("http://localhost:5000/api/books/978", UriKind.Absolute);
            return response;
        });

        var outcome = await Side(handler).CreateAsync("/api/books", new JsonObject());

        await Assert.That(outcome.Location).IsEqualTo("http://localhost:5000/api/books/978");
    }

    [Test]
    public async Task HttpSide_Invoke_NullArguments_PostsAnEmptyObject() {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await Side(handler).InvokeAsync("/api/tallies/a/", "bump", null);

        await Assert.That(handler.Requests.Single().Body).IsEqualTo("{}");
        await Assert.That(handler.Requests.Single().Uri).IsEqualTo("http://localhost:5000/api/tallies/a/bump");
    }
}

/// <summary>The probe runner over a fake handler: no host is booted.</summary>
public class RequestMatrixProbeTests {
    private sealed class Fake(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request, body);
        }
    }

    private static readonly DomainMatrix Matrix = new("d", [new EntityMatrix(
        "Thing", "/api/things", "Code",
        JsonNode.Parse("""{"Code":"a","Name":"x"}""")!.AsObject(),
        JsonNode.Parse("{}")!.AsObject(),
        new ActionProbe("Do", JsonNode.Parse("{}")!.AsObject(), JsonNode.Parse("""{"n":1}""")!.AsObject()))]);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>A healthy host unless a knob says otherwise.</summary>
    private static async Task<Dictionary<string, ProbeObservation>> Run(
        bool listThrows = false, string readBody = """{"code":"a","name":"x"}""", HttpStatusCode rejectedStatus = HttpStatusCode.BadRequest) {
        var handler = new Fake((request, body) => {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path == "/api/things")
                return listThrows ? throw new HttpRequestException("refused") : Json(HttpStatusCode.OK, "[]");
            if (request.Method == HttpMethod.Post && path == "/api/things") {
                if (body == "{}")
                    return Json(HttpStatusCode.BadRequest, "{}");
                var created = Json(HttpStatusCode.Created, "{}");
                created.Headers.Location = new Uri("/api/things/a", UriKind.Relative);
                return created;
            }
            if (request.Method == HttpMethod.Get && path == "/api/things/a")
                return Json(HttpStatusCode.OK, readBody);
            if (request.Method == HttpMethod.Post && path == "/api/things/a/do")
                return body == "{}" ? new HttpResponseMessage(HttpStatusCode.OK) : Json(rejectedStatus, "{}");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var side = new HttpSide(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") });
        return (await RequestMatrix.RunAsync(side, Matrix)).ToDictionary(o => o.Probe);
    }

    [Test]
    public async Task RunAsync_HealthyHost_EveryProbePasses() {
        var all = await Run();

        await Assert.That(all.Values.Where(o => !o.Passed).Select(o => $"{o.Probe}: {o.Observed}")).IsEmpty();
    }

    [Test]
    public async Task RunAsync_TransportFailure_ObservesStatusErrNotStatusZero() {
        var all = await Run(listThrows: true);

        await Assert.That(all["Thing.List"].Passed).IsFalse();
        await Assert.That(all["Thing.List"].Observed).IsEqualTo("status=err;location=none");
    }

    [Test]
    public async Task RunAsync_ReadBodyThatDoesNotEchoTheCreatedRow_FailsReadCreated() {
        var all = await Run(readBody: """{"code":"a","name":"someone else"}""");

        await Assert.That(all["Thing.ReadCreated"].Passed).IsFalse();
        await Assert.That(all["Thing.ReadCreated"].Observed).IsEqualTo("status=200;location=none");
    }

    [Test]
    public async Task RunAsync_ActionRejectedWith404_FailsActionRejected() {
        var all = await Run(rejectedStatus: HttpStatusCode.NotFound);

        await Assert.That(all["Thing.ActionRejected"].Passed).IsFalse();
        await Assert.That(all["Thing.ActionRejected"].Observed).IsEqualTo("status=404;location=none");
    }

    [Test]
    public async Task RunAsync_ActionRejectedWith409Or422_PassesActionRejected() {
        foreach (var status in new[] { HttpStatusCode.Conflict, HttpStatusCode.UnprocessableEntity }) {
            var all = await Run(rejectedStatus: status);
            await Assert.That(all["Thing.ActionRejected"].Passed).IsTrue();
        }
    }
}