using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;

using Poly.Compile;
using Poly.Packs.Product;

using Polyc.Lsp;

namespace Poly.Tests.Polyc;

public class PolycLspTests {
    private sealed class Client : IDisposable {
        private readonly AnonymousPipeServerStream _toServer = new(PipeDirection.Out);
        private readonly AnonymousPipeServerStream _fromServer = new(PipeDirection.In);
        private readonly AnonymousPipeClientStream _serverIn;
        private readonly AnonymousPipeClientStream _serverOut;
        private readonly LspTransport _transport;
        private readonly Task<int> _server;
        private readonly TimeSpan _readLimit;
        private Task<JsonNode?>? _lastRead;
        private int _id;

        public Client(TimeSpan? readLimit = null, TimeSpan? stopLimit = null) {
            _readLimit = readLimit ?? TimeSpan.FromSeconds(30);
            _serverIn = new AnonymousPipeClientStream(PipeDirection.In, _toServer.ClientSafePipeHandle);
            _serverOut = new AnonymousPipeClientStream(PipeDirection.Out, _fromServer.ClientSafePipeHandle);
            _transport = new LspTransport(_fromServer, _toServer);
            var server = new LspServer(_serverIn, _serverOut);
            _server = Task.Run(server.Run);
        }

        public JsonNode Request(string method, JsonObject parameters) {
            var id = ++_id;
            _transport.Write(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters });
            while (true) {
                var message = Next();
                if (message["id"]?.GetValue<int>() == id)
                    return message;
            }
        }

        public void Notify(string method, JsonObject parameters) =>
            _transport.Write(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = parameters });

        public JsonNode NextPublish() {
            while (true) {
                var message = Next();
                if (message["method"]?.GetValue<string>() == "textDocument/publishDiagnostics")
                    return message["params"]!;
            }
        }

        private JsonNode Next() {
            var read = _lastRead = Task.Run(_transport.Read);
            if (!read.Wait(_readLimit))
                throw new TimeoutException($"No LSP message within {_readLimit.TotalSeconds:0.###}s.");
            return read.Result ?? throw new EndOfStreamException();
        }

        public int Stop() {
            Request("shutdown", []);
            Notify("exit", []);
            return WaitForExit();
        }

        public int WaitForExit() => _server.Result;

        public bool ThreadsExited(TimeSpan bound) =>
            Task.WaitAll([_server, .. _lastRead is null ? Array.Empty<Task>() : [_lastRead]], bound);

        public void Dispose() {
            _toServer.Dispose();
            _fromServer.Dispose();
            _serverIn.Dispose();
            _serverOut.Dispose();
        }
    }

    /// <summary>Starves the pool (max threads = processor count, flooded with blocked items) and asks a started
    /// client to answer <c>initialize</c>. The client must not need a pool thread.</summary>
    [Test]
    [NotInParallel]
    public async Task Client_AnswersInitialize_WhileThePoolIsStarved() {
        ThreadPool.GetMaxThreads(out var maxWorkers, out var maxIo);
        var gate = new ManualResetEventSlim(); // never disposed: a queued blocker may still touch it after the test
        JsonNode? init;
        Client? client = null;
        try {
            ThreadPool.SetMaxThreads(Environment.ProcessorCount, maxIo);
            for (var i = 0; i < Environment.ProcessorCount * 4; i++)
                ThreadPool.UnsafeQueueUserWorkItem(static g => g.Wait(), gate, preferLocal: false);
            client = new Client();
            init = client.Request("initialize", new JsonObject());
        }
        finally {
            gate.Set();
            ThreadPool.SetMaxThreads(maxWorkers, maxIo);
            // Off the test thread and bounded: a Dispose that blocks must fail a test, not hang the run.
            if (client is not null)
                await Task.WhenAny(Task.Factory.StartNew(client.Dispose, TaskCreationOptions.LongRunning), Task.Delay(TimeSpan.FromSeconds(10)));
        }
        await Assert.That(init["result"]!["capabilities"]!["hoverProvider"]!.GetValue<bool>()).IsTrue();
    }

    [Test]
    public async Task Stop_ServerNeverSentExit_FailsWithinTheBound() {
        using var client = new Client(stopLimit: TimeSpan.FromMilliseconds(500));
        client.Request("initialize", new JsonObject());
        var wait = Task.Factory.StartNew(() => client.WaitForExit(), TaskCreationOptions.LongRunning);
        var first = await Task.WhenAny(wait, Task.Delay(TimeSpan.FromSeconds(10)));
        await Assert.That(first == wait).IsTrue().Because("WaitForExit is still blocked 10s after a 500ms bound");
        await Assert.That(async () => await wait).Throws<TimeoutException>();
    }

    [Test]
    public async Task Dispose_AfterATimedOutRead_ReturnsAndLeavesNoBlockedThread() {
        var client = new Client(readLimit: TimeSpan.FromMilliseconds(300));
        var timedOut = false;
        try {
            client.Request("initialize", new JsonObject());
            client.NextPublish(); // no document is open, so no message ever arrives
        }
        catch (TimeoutException) {
            timedOut = true;
        }
        await Assert.That(timedOut).IsTrue();

        var dispose = Task.Factory.StartNew(client.Dispose, TaskCreationOptions.LongRunning);
        var first = await Task.WhenAny(dispose, Task.Delay(TimeSpan.FromSeconds(10)));
        await Assert.That(first == dispose).IsTrue().Because("Dispose is still blocked 10s after a timed-out read");
        await Assert.That(client.ThreadsExited(TimeSpan.FromSeconds(5))).IsTrue()
            .Because("the server or the reader is still blocked 5s after Dispose");
    }

    private const string Uri = "file:///tmp/polyc-lsp/orders.poly";
    private static readonly string File = LspServer.FileOf(Uri);

    private static JsonObject Doc(string uri) => new() { ["uri"] = uri };

    private static List<CompileDiagnostic> CheckJson(string text) {
        var result = new PolyCompiler(ProductCatalog.Catalog, new SessionOptions(ProductCatalog.Catalog, ProductCatalog.ImplicitIds)).Compile(text, File);
        var json = JsonNode.Parse(DiagnosticFormat.Report([result], []))!;
        return [.. json.AsArray().Select(n => DiagnosticFormat.FromJson(n!))];
    }

    private static List<CompileDiagnostic> Published(JsonNode publish) =>
        [.. publish["diagnostics"]!.AsArray().Select(n => LspDiagnostics.FromLsp(File, n!))];

    private static void Open(Client client, string text) =>
        client.Notify("textDocument/didOpen", new JsonObject {
            ["textDocument"] = new JsonObject { ["uri"] = Uri, ["languageId"] = "poly", ["version"] = 1, ["text"] = text },
        });

    [Test]
    public async Task Publish_MatchesCheckJson_AndClearsAfterEdit() {
        using var client = new Client();
        var init = client.Request("initialize", new JsonObject { ["capabilities"] = new JsonObject() });
        await Assert.That(init["result"]!["capabilities"]!["hoverProvider"]!.GetValue<bool>()).IsTrue();
        client.Notify("initialized", []);

        var broken = PolycTestSupport.ManyErrors(2);
        Open(client, broken);
        var first = Published(client.NextPublish());
        var expected = CheckJson(broken);
        await Assert.That(first.Count).IsGreaterThanOrEqualTo(2);
        await Assert.That(first.SequenceEqual(expected)).IsTrue();

        var fixedText = PolycTestSupport.Orders;
        client.Notify("textDocument/didChange", new JsonObject {
            ["textDocument"] = new JsonObject { ["uri"] = Uri, ["version"] = 2 },
            ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = fixedText }),
        });
        var second = Published(client.NextPublish());
        await Assert.That(second.SequenceEqual(CheckJson(fixedText))).IsTrue();
        await Assert.That(second.Any(d => d.IsError)).IsFalse();
        await Assert.That(client.Stop()).IsEqualTo(0);
    }

    /// <summary>
    /// C-03 carry (pin, green on master): the language server's default seed is the one
    /// <c>Options.CreateCompiler(null)</c> builds, so a no-<c>uses</c> document with a
    /// <c>unique</c> property is judged the same as in <c>polyc check</c>.
    /// </summary>
    [Test]
    public async Task LspServer_DefaultSeed_MatchesCreateCompilerNull() {
        using var client = new Client();
        client.Request("initialize", new JsonObject());
        const string text = "domain D\nA: entity {\n  X: Text unique\n}\n";
        Open(client, text);
        var published = Published(client.NextPublish());

        var viaOptions = global::Polyc.Options.CreateCompiler(null).Compile(text, File);
        var expected = JsonNode.Parse(DiagnosticFormat.Report([viaOptions], []))!.AsArray()
            .Select(n => DiagnosticFormat.FromJson(n!)).ToList();
        await Assert.That(published.Select(d => d.Code).SequenceEqual(expected.Select(d => d.Code))).IsTrue();
        await Assert.That(published.Any(d => d.IsError)).IsFalse();
    }

    [Test]
    public async Task Publish_UnknownUses_IsDsl0002OnHeaderToken() {
        using var client = new Client();
        client.Request("initialize", new JsonObject());
        const string text = "domain D\nuses nope\nA: entity {\n  X: Text\n}\n";
        Open(client, text);
        var published = Published(client.NextPublish());
        await Assert.That(published.SequenceEqual(CheckJson(text))).IsTrue();
        await Assert.That(published.Single().Code).IsEqualTo("DSL0002");
        await Assert.That(published.Single().Line).IsEqualTo(2);
    }

    [Test]
    public async Task HoverDefinitionAndSymbols_ReadTheOutline() {
        using var client = new Client();
        client.Request("initialize", new JsonObject());
        const string text = "domain Shop\n\nProduct: entity {\n  Sku: Text unique\n}\n\nLine: entity {\n  item: Product\n}\n";
        Open(client, text);
        client.NextPublish();

        var hover = client.Request("textDocument/hover", new JsonObject {
            ["textDocument"] = Doc(Uri),
            ["position"] = new JsonObject { ["line"] = 3, ["character"] = 3 },
        });
        await Assert.That(hover["result"]!["contents"]!["value"]!.GetValue<string>()).Contains("property");
        await Assert.That(hover["result"]!["contents"]!["value"]!.GetValue<string>()).Contains("Sku");

        var definition = client.Request("textDocument/definition", new JsonObject {
            ["textDocument"] = Doc(Uri),
            ["position"] = new JsonObject { ["line"] = 7, ["character"] = 10 },
        });
        await Assert.That(definition["result"]!["range"]!["start"]!["line"]!.GetValue<int>()).IsEqualTo(2);
        await Assert.That(definition["result"]!["uri"]!.GetValue<string>()).IsEqualTo(Uri);

        var symbols = client.Request("textDocument/documentSymbol", new JsonObject { ["textDocument"] = Doc(Uri) });
        var names = symbols["result"]!.AsArray().Select(s => s!["name"]!.GetValue<string>()).ToList();
        await Assert.That(names).Contains("Product");
        await Assert.That(names).Contains("Line");
        var product = symbols["result"]!.AsArray().Single(s => s!["name"]!.GetValue<string>() == "Product")!;
        await Assert.That(product["children"]!.AsArray().Single()!["name"]!.GetValue<string>()).IsEqualTo("Sku");
    }

    [Test]
    public async Task ProtocolFailure_ExitsTwo() {
        using var toServer = new AnonymousPipeServerStream(PipeDirection.Out);
        using var fromServer = new AnonymousPipeServerStream(PipeDirection.In);
        using var serverIn = new AnonymousPipeClientStream(PipeDirection.In, toServer.ClientSafePipeHandle);
        using var serverOut = new AnonymousPipeClientStream(PipeDirection.Out, fromServer.ClientSafePipeHandle);
        var server = new LspServer(serverIn, serverOut, stderr: TextWriter.Null);
        var run = Task.Run(server.Run);
        toServer.Write(Encoding.ASCII.GetBytes("\r\n"));
        toServer.Flush();
        var finished = run.Wait(TimeSpan.FromSeconds(10));
        await Assert.That(finished).IsTrue();
        await Assert.That(run.Result).IsEqualTo(2);
    }

    [Test]
    public async Task MalformedNotification_KeepsServing() {
        using var client = new Client();
        client.Request("initialize", new JsonObject());
        Open(client, PolycTestSupport.Orders);
        client.NextPublish();
        client.Notify("textDocument/didChange", new JsonObject {
            ["textDocument"] = new JsonObject { ["uri"] = Uri, ["version"] = 2 },
        });
        await Assert.That(client.Stop()).IsEqualTo(0);
    }

    [Test]
    public async Task UnknownRequest_ReturnsMethodNotFound() {
        using var client = new Client();
        client.Request("initialize", new JsonObject());
        var response = client.Request("textDocument/completion", new JsonObject());
        await Assert.That(response["error"]!["code"]!.GetValue<int>()).IsEqualTo(-32601);
    }
}