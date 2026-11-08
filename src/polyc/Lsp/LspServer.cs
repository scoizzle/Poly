using System.Text.Json.Nodes;

using Poly.Compile;
using Poly.DomainModeling.Language;
using Poly.Packs.Product;

namespace Polyc.Lsp;

/// <summary>
/// Hand-rolled stdio language server. Diagnostics come from <see cref="PolyCompiler"/>, the same
/// core <c>polyc check</c> drives; hover, definition and symbols read <see cref="DslOutline"/>.
/// </summary>
public sealed class LspServer {
    private readonly LspTransport _transport;
    private readonly PolyCompiler _compiler;
    private readonly Dictionary<string, Document> _documents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CancellationTokenSource> _running = new(StringComparer.Ordinal);
    private readonly List<Task> _tasks = [];
    private readonly TextWriter _stderr;
    private readonly object _gate = new();
    private bool _shutdown;

    private sealed record Document(string Text, int Version);

    public LspServer(Stream input, Stream output, string seed = "authoring", TextWriter? stderr = null) {
        _transport = new LspTransport(input, output);
        _compiler = new PolyCompiler(ProductCatalog.Catalog, seed);
        _stderr = stderr ?? Console.Error;
    }

    /// <summary>Runs until <c>exit</c> or end of input. Returns the process exit code.</summary>
    public int Run() {
        try {
            while (_transport.Read() is { } message) {
                var method = message["method"]?.GetValue<string>();
                var id = message["id"];
                if (method is null)
                    continue;
                if (method == "exit") {
                    WaitForChecks();
                    return _shutdown ? 0 : 1;
                }
                try {
                    Dispatch(method, id, message["params"]);
                }
                catch (Exception ex) when (id is not null) {
                    Respond(id, error: new JsonObject { ["code"] = -32603, ["message"] = ex.Message });
                }
                catch (Exception ex) {
                    _stderr.WriteLine($"polyc lsp: {ex.Message}");
                }
            }
        }
        catch (Exception ex) {
            _stderr.WriteLine($"polyc lsp: protocol failure: {ex.Message}");
            return 2;
        }
        WaitForChecks();
        return _shutdown ? 0 : 1;
    }

    private void Dispatch(string method, JsonNode? id, JsonNode? parameters) {
        switch (method) {
            case "initialize":
                Respond(id!, new JsonObject {
                    ["capabilities"] = new JsonObject {
                        ["textDocumentSync"] = new JsonObject { ["openClose"] = true, ["change"] = 1 },
                        ["hoverProvider"] = true,
                        ["definitionProvider"] = true,
                        ["documentSymbolProvider"] = true,
                    },
                    ["serverInfo"] = new JsonObject { ["name"] = "polyc" },
                });
                break;
            case "initialized":
                break;
            case "shutdown":
                _shutdown = true;
                Respond(id!, null);
                break;
            case "textDocument/didOpen": {
                    var item = parameters!["textDocument"]!;
                    Change(item["uri"]!.GetValue<string>(), item["text"]!.GetValue<string>(), item["version"]?.GetValue<int>() ?? 0);
                    break;
                }
            case "textDocument/didChange": {
                    var uri = parameters!["textDocument"]!["uri"]!.GetValue<string>();
                    var version = parameters["textDocument"]!["version"]?.GetValue<int>() ?? 0;
                    var changes = parameters["contentChanges"]!.AsArray();
                    Change(uri, changes[^1]!["text"]!.GetValue<string>(), version);
                    break;
                }
            case "textDocument/didClose": {
                    var uri = parameters!["textDocument"]!["uri"]!.GetValue<string>();
                    lock (_gate) {
                        _documents.Remove(uri);
                        if (_running.Remove(uri, out var cts))
                            cts.Cancel();
                    }
                    Notify("textDocument/publishDiagnostics", new JsonObject { ["uri"] = uri, ["diagnostics"] = new JsonArray() });
                    break;
                }
            case "textDocument/hover":
                Respond(id!, Hover(parameters!));
                break;
            case "textDocument/definition":
                Respond(id!, Definition(parameters!));
                break;
            case "textDocument/documentSymbol":
                Respond(id!, DocumentSymbols(parameters!));
                break;
            default:
                if (id is not null)
                    Respond(id, error: new JsonObject { ["code"] = -32601, ["message"] = $"Method not found: {method}" });
                break;
        }
    }

    private void Change(string uri, string text, int version) {
        CancellationTokenSource cts;
        lock (_gate) {
            _documents[uri] = new Document(text, version);
            if (_running.TryGetValue(uri, out var previous))
                previous.Cancel();
            cts = _running[uri] = new CancellationTokenSource();
            _tasks.RemoveAll(t => t.IsCompleted);
            _tasks.Add(Task.Run(() => Check(uri, text, version, cts)));
        }
    }

    private void Check(string uri, string text, int version, CancellationTokenSource cts) {
        try {
            var result = _compiler.Compile(text, FileOf(uri), cts.Token);
            lock (_gate) {
                if (cts.IsCancellationRequested || !_documents.TryGetValue(uri, out var current) || current.Version != version)
                    return;
            }
            Notify("textDocument/publishDiagnostics", new JsonObject {
                ["uri"] = uri,
                ["version"] = version,
                ["diagnostics"] = new JsonArray([.. result.Diagnostics.Select(d => (JsonNode?)LspDiagnostics.ToLsp(d))]),
            });
        }
        catch (OperationCanceledException) {
        }
    }

    private void WaitForChecks() {
        Task[] tasks;
        lock (_gate)
            tasks = [.. _tasks];
        Task.WaitAll(tasks);
    }

    /// <summary>The file name a diagnostic carries: the local path of a file URI, else the URI.</summary>
    public static string FileOf(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.IsFile ? parsed.LocalPath : uri;

    // ── outline-backed requests ─────────────────────────────────

    private (Document Doc, DslOutline Outline, LineMap Lines, int Offset)? Locate(JsonNode parameters) {
        var uri = parameters["textDocument"]!["uri"]!.GetValue<string>();
        Document? doc;
        lock (_gate)
            _documents.TryGetValue(uri, out doc);
        if (doc is null)
            return null;
        var lines = new LineMap(doc.Text);
        var line = parameters["position"]!["line"]!.GetValue<int>() + 1;
        var column = parameters["position"]!["character"]!.GetValue<int>() + 1;
        try {
            return (doc, DslOutline.Build(doc.Text), lines, lines.OffsetOf(line, column));
        }
        catch (FormatException) {
            return null;
        }
    }

    private static JsonObject RangeOf(LineMap lines, int offset, int length) {
        var (startLine, startColumn) = lines.At(offset);
        var (endLine, endColumn) = lines.At(offset + length);
        return new JsonObject {
            ["start"] = new JsonObject { ["line"] = startLine - 1, ["character"] = startColumn - 1 },
            ["end"] = new JsonObject { ["line"] = endLine - 1, ["character"] = endColumn - 1 },
        };
    }

    private static JsonObject NameRange(LineMap lines, string text, OutlineNode node) {
        var fits = node.StartOffset + node.Name.Length <= text.Length
            && string.CompareOrdinal(text, node.StartOffset, node.Name, 0, node.Name.Length) == 0;
        return RangeOf(lines, node.StartOffset, fits ? node.Name.Length : 1);
    }

    private JsonNode? Hover(JsonNode parameters) {
        if (Locate(parameters) is not var (doc, outline, lines, offset))
            return null;
        var node = outline.All()
            .Where(n => n.StartOffset <= offset && offset < Math.Max(n.EndOffset, n.StartOffset + 1))
            .OrderBy(n => n.EndOffset - n.StartOffset)
            .FirstOrDefault();
        if (node is null)
            return null;
        return new JsonObject {
            ["contents"] = new JsonObject {
                ["kind"] = "markdown",
                ["value"] = $"**{node.Kind}** `{node.Name}`" + (node.Path.Length > 0 && node.Kind is not ("domain" or "uses") ? $"\n\n{node.Path}" : ""),
            },
            ["range"] = NameRange(lines, doc.Text, node),
        };
    }

    private JsonNode? Definition(JsonNode parameters) {
        if (Locate(parameters) is not var (doc, outline, lines, offset))
            return null;
        var word = WordAt(doc.Text, offset);
        if (word.Length == 0)
            return null;
        var target = outline.Roots.FirstOrDefault(n =>
            string.Equals(n.Name, word, StringComparison.Ordinal) && n.Kind is not ("domain" or "uses"));
        if (target is null)
            return null;
        return new JsonObject {
            ["uri"] = parameters["textDocument"]!["uri"]!.GetValue<string>(),
            ["range"] = NameRange(lines, doc.Text, target),
        };
    }

    private JsonNode? DocumentSymbols(JsonNode parameters) {
        var uri = parameters["textDocument"]!["uri"]!.GetValue<string>();
        Document? doc;
        lock (_gate)
            _documents.TryGetValue(uri, out doc);
        if (doc is null)
            return new JsonArray();
        DslOutline outline;
        try {
            outline = DslOutline.Build(doc.Text);
        }
        catch (FormatException) {
            return new JsonArray();
        }
        var lines = new LineMap(doc.Text);
        JsonObject Symbol(OutlineNode node) => new() {
            ["name"] = node.Name,
            ["kind"] = KindOf(node.Kind),
            ["range"] = RangeOf(lines, node.StartOffset, Math.Max(node.EndOffset - node.StartOffset, 1)),
            ["selectionRange"] = NameRange(lines, doc.Text, node),
            ["children"] = new JsonArray([.. node.Children.Select(c => (JsonNode?)Symbol(c))]),
        };
        return new JsonArray([.. outline.Roots.Select(n => (JsonNode?)Symbol(n))]);
    }

    private static int KindOf(string kind) => kind switch {
        "domain" => 2,
        "entity" => 5,
        "enum" => 10,
        "value" => 23,
        "contract" => 11,
        "bind" => 12,
        "stage" => 22,
        "action" => 6,
        "policy" => 12,
        "navigation" => 8,
        "property" => 7,
        _ => 13,
    };

    private static string WordAt(string text, int offset) {
        var start = offset;
        while (start > 0 && IsWord(text[start - 1]))
            start--;
        var end = offset;
        while (end < text.Length && IsWord(text[end]))
            end++;
        return text[start..end];
    }

    private static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';

    // ── wire ────────────────────────────────────────────────────

    private void Respond(JsonNode id, JsonNode? result = null, JsonObject? error = null) {
        var message = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone() };
        if (error is not null)
            message["error"] = error;
        else
            message["result"] = result;
        _transport.Write(message);
    }

    private void Notify(string method, JsonObject parameters) =>
        _transport.Write(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = parameters });
}