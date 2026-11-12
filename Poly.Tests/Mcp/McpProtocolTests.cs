using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using ModelContextProtocol.Server;

using Poly.Mcp;
using Poly.Mcp.Surface;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Mcp;

/// <summary>
/// The wire contract for caller mistakes. One filter, derived from each tool's published
/// <c>inputSchema</c>, runs before the SDK binder, so an unknown name, a wrong JSON type, or a
/// missing workspace comes back as a typed envelope instead of <c>INTERNAL_ERROR</c>.
/// </summary>
public class McpProtocolTests {
    private const string Valid = """
        domain V2
        Product: entity {
          Total: Number range(0, 100)
          Draft: stage { Submit: action { transition to Active } }
          Active: stage {}
        }
        """;

    [Test]
    public async Task CallerErrors_ReturnTypedErrors_AtTheWire() {
        using var host = await PolycProcess.McpAsync(McpTarget.Polyc, TimeSpan.FromSeconds(20));
        await host.InitializeAsync();
        var workspace = await OpenWorkspaceAsync(host);

        // One defect per row: every other argument in the row is valid. Every row runs before
        // any assertion, so one run reports all of them.
        var explicitNull = JsonNode.Parse("""{"workspace":null}""")!.AsObject();
        explicitNull["poly"] = Valid;
        var rows = new (string Row, string Tool, JsonObject Arguments, string Code, string[] Fragments)[] {
            ("reset:\"yes\" names reset/boolean", "poly_simulate",
                new JsonObject { ["workspace"] = workspace, ["steps"] = "[]", ["reset"] = "yes" },
                "INVALID_ARGUMENT", ["reset", "boolean"]),
            ("a missing required workspace", "poly_define",
                new JsonObject { ["poly"] = Valid },
                "WORKSPACE_REQUIRED", ["poly_workspace"]),
            // A4 characterisation, green on master: blank and explicit null already reach
            // TryWorkspace, so the filter hands both to the tool body instead of re-producing it.
            ("workspace:\"\" is blank", "poly_define",
                new JsonObject { ["workspace"] = "", ["poly"] = Valid },
                "WORKSPACE_REQUIRED", ["poly_workspace"]),
            ("workspace:null is explicit null", "poly_define",
                explicitNull,
                "WORKSPACE_REQUIRED", ["poly_workspace"]),
            ("workspace:42 is the wrong type", "poly_define",
                new JsonObject { ["workspace"] = 42, ["poly"] = Valid },
                "INVALID_ARGUMENT", ["workspace", "string"]),
            // #488's repro. M-07 widens `steps` to array-or-string and inverts this assertion.
            ("steps as a JSON array is invalid until M-07 accepts arrays", "poly_simulate",
                new JsonObject { ["workspace"] = workspace, ["steps"] = new JsonArray() },
                "INVALID_ARGUMENT", ["steps", "string"]),
            // Green on master: the unknown-name error is not folded into INVALID_ARGUMENT.
            ("an unknown argument still names the valid ones", "poly_guide",
                new JsonObject { ["bogus"] = 1 },
                "UNKNOWN_ARGUMENT", ["Valid: topic"]),
        };

        var observed = new List<(string Code, string Summary)>();
        foreach (var row in rows) {
            var result = await host.ToolAsync(row.Tool, row.Arguments);
            observed.Add((result["errorCode"]?.GetValue<string>() ?? "<none>",
                result["summary"]?.GetValue<string>() ?? ""));
        }

        var table = string.Join("\n", rows.Select((row, i) => $"  {row.Row} => {observed[i].Code}: {observed[i].Summary}"));
        for (var i = 0; i < rows.Length; i++) {
            await Assert.That(observed[i].Code).IsEqualTo(rows[i].Code)
                .Because($"{rows[i].Row}\nobserved:\n{table}");
            foreach (var fragment in rows[i].Fragments)
                await Assert.That(observed[i].Summary).Contains(fragment)
                    .Because($"{rows[i].Row}\nobserved:\n{table}");
        }
    }

    /// <summary>
    /// A1: only the SDK binder's two shapes are caller errors. A bare ArgumentException from a
    /// tool body is a real defect and must stay loud.
    /// </summary>
    [Test]
    public async Task Boundary_MapsBinderExceptionsToInvalidArgument_KeepsInternalErrorForDefects() {
        // The exact shape the SDK binder throws (#533: "… (Parameter 'arguments')").
        await AssertCodeAsync(
            new ArgumentException("The arguments dictionary is missing a value for the required parameter 'workspace'.", "arguments"),
            "INVALID_ARGUMENT");
        await AssertCodeAsync(new JsonException("The JSON value could not be converted to System.Boolean."), "INVALID_ARGUMENT");
        await AssertCodeAsync(new ArgumentException("boom"), "INTERNAL_ERROR");
        await AssertCodeAsync(new InvalidOperationException("boom"), "INTERNAL_ERROR");
        await AssertCodeAsync(new NullReferenceException("boom"), "INTERNAL_ERROR");
    }

    /// <summary>A6: the Handle seam logs a binder failure at Warning naming the tool.</summary>
    [Test]
    public async Task BinderFailure_IsLoggedAsAWarning_NamingTheTool() {
        var logger = new RecordingLogger();
        var result = ToolExceptionBoundary.Handle(
            new ArgumentException("The arguments dictionary is missing a value for the required parameter 'workspace'.", "arguments"),
            "poly_define",
            logger);

        await Assert.That(result.StructuredContent!.Value.GetProperty("errorCode").GetString()).IsEqualTo("INVALID_ARGUMENT");
        await Assert.That(logger.Entries.Count).IsEqualTo(1);
        await Assert.That(logger.Entries[0].Level).IsEqualTo(LogLevel.Warning);
        await Assert.That(logger.Entries[0].Message).Contains("poly_define");

        var defect = new RecordingLogger();
        ToolExceptionBoundary.Handle(new InvalidOperationException("boom"), "poly_define", defect);
        await Assert.That(defect.Entries[0].Level).IsEqualTo(LogLevel.Error);
    }

    [Test]
    public async Task LimitExceeded_IsInvalidArgumentNamingTheLimit() {
        var spec = new[] {
            new ArgumentSpec("poly", new HashSet<string>(StringComparer.Ordinal) { "string" }, Required: false),
            new ArgumentSpec("steps", new HashSet<string>(StringComparer.Ordinal) { "array" }, Required: false),
        };
        var limits = new Dictionary<(string Tool, string Argument), ArgumentLimit> {
            [("poly_define", "poly")] = new(MaxChars: 8, MaxItems: null),
            [("poly_define", "steps")] = new(MaxChars: null, MaxItems: 2),
        };

        var characters = ArgumentValidator.Validate("poly_define",
            Args(("poly", JsonSerializer.SerializeToElement(new string('x', 40)))), spec, limits);
        await Assert.That(characters.Verdict!.Value.ErrorCode).IsEqualTo("INVALID_ARGUMENT");
        await Assert.That(characters.Verdict!.Value.Message).Contains("poly");
        await Assert.That(characters.Verdict!.Value.Message).Contains("40");
        await Assert.That(characters.Verdict!.Value.Message).Contains("8");

        // MaxItems is measured on the raw JSON array, before any binder sees it.
        var items = ArgumentValidator.Validate("poly_define",
            Args(("steps", JsonSerializer.SerializeToElement(new[] { 1, 2, 3 }))), spec, limits);
        await Assert.That(items.Verdict!.Value.ErrorCode).IsEqualTo("INVALID_ARGUMENT");
        await Assert.That(items.Verdict!.Value.Message).Contains("3");
        await Assert.That(items.Verdict!.Value.Message).Contains("2");

        // The shipped registry is empty: a 1 MB document is not rejected.
        var big = ArgumentValidator.Validate("poly_define",
            Args(("poly", JsonSerializer.SerializeToElement(new string('x', 1 << 20)))), spec, ArgumentLimits.Registry);
        await Assert.That(big.Verdict.HasValue).IsFalse();
    }

    [Test]
    public async Task Validator_RejectsEveryWrongJsonKind_WithTheExpectedType() {
        var surface = new PolySurfaceTools(McpTestTools.Store, NullLogger<PolySurfaceTools>.Instance);
        var tools = typeof(PolySurfaceTools).GetMethods()
            .Select(method => (Method: method, Attribute: method.GetCustomAttribute<McpServerToolAttribute>()))
            .Where(pair => pair.Attribute?.Name is not null)
            .ToList();
        await Assert.That(tools.Count).IsEqualTo(7);

        foreach (var (method, attribute) in tools) {
            var tool = McpServerTool.Create(method, surface, new McpServerToolCreateOptions());
            var spec = ArgumentSpecCatalog.For(tool);
            foreach (var argument in spec) {
                var wrong = WrongValue(argument.JsonTypes);
                if (wrong is null)
                    continue;

                var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                foreach (var required in spec.Where(required => required.Required))
                    arguments[required.Name] = ValidValue(required.JsonTypes);
                arguments[argument.Name] = wrong.Value;

                var outcome = ArgumentValidator.Validate(tool.ProtocolTool.Name, arguments, spec, ArgumentLimits.Registry);
                await Assert.That(outcome.Verdict?.ErrorCode).IsEqualTo("INVALID_ARGUMENT")
                    .Because($"{attribute!.Name}.{argument.Name} must reject {wrong.Value.ValueKind}");
                await Assert.That(outcome.Verdict!.Value.Message).Contains(argument.Name);
                await Assert.That(outcome.Verdict!.Value.Message).Contains(ExpectedWord(argument.JsonTypes));
            }
        }
    }

    private static async Task<string> OpenWorkspaceAsync(McpClient host) {
        var created = await host.ToolAsync("poly_workspace", new JsonObject { ["name"] = "Protocol" });
        return created["state"]!["workspace"]!.GetValue<string>();
    }

    private static async Task AssertCodeAsync(Exception exception, string code) {
        var result = ToolExceptionBoundary.Failure(exception);
        await Assert.That(result.StructuredContent!.Value.GetProperty("errorCode").GetString()).IsEqualTo(code)
            .Because($"{exception.GetType().Name}: {exception.Message}");
    }

    private static Dictionary<string, JsonElement> Args(params (string Name, JsonElement Value)[] entries) =>
        entries.ToDictionary(entry => entry.Name, entry => entry.Value, StringComparer.Ordinal);

    private static JsonElement? WrongValue(IReadOnlySet<string> types) {
        if (!types.Contains("string"))
            return JsonSerializer.SerializeToElement("wrong");
        if (!types.Contains("boolean"))
            return JsonSerializer.SerializeToElement(true);
        if (!types.Contains("number") && !types.Contains("integer"))
            return JsonSerializer.SerializeToElement(1);
        if (!types.Contains("object"))
            return JsonSerializer.SerializeToElement(new JsonObject());
        if (!types.Contains("array"))
            return JsonSerializer.SerializeToElement(new JsonArray());
        return null;
    }

    private static JsonElement ValidValue(IReadOnlySet<string> types) {
        if (types.Contains("string"))
            return JsonSerializer.SerializeToElement("x");
        if (types.Contains("boolean"))
            return JsonSerializer.SerializeToElement(true);
        if (types.Contains("number") || types.Contains("integer"))
            return JsonSerializer.SerializeToElement(1);
        if (types.Contains("object"))
            return JsonSerializer.SerializeToElement(new JsonObject());
        if (types.Contains("array"))
            return JsonSerializer.SerializeToElement(new JsonArray());
        return JsonSerializer.SerializeToElement((string?)null);
    }

    private static string ExpectedWord(IReadOnlySet<string> types) {
        if (types.Contains("string"))
            return "string";
        if (types.Contains("boolean"))
            return "boolean";
        if (types.Contains("number") || types.Contains("integer"))
            return "number";
        if (types.Contains("object"))
            return "object";
        if (types.Contains("array"))
            return "array";
        return "null";
    }

    private sealed class RecordingLogger : ILogger {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}