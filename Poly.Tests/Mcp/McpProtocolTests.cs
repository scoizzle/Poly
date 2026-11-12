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
        using var host = await PolycProcess.McpAsync(TimeSpan.FromSeconds(20));
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
            // N1: a missing workspace must not mask a defect in another argument.
            ("a wrong-typed steps with no workspace names steps", "poly_simulate",
                new JsonObject { ["steps"] = 42 },
                "INVALID_ARGUMENT", ["steps"]),
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

        // N2: an explicit null on a nullable optional string is valid at the wire.
        var nullTopic = await host.ToolAsync("poly_guide", JsonNode.Parse("""{"topic":null}""")!.AsObject());
        await Assert.That(nullTopic["ok"]?.GetValue<bool>()).IsTrue();
    }

    /// <summary>N1: a missing workspace is reported only when every other argument is well-formed.</summary>
    [Test]
    public async Task Validator_MissingWorkspace_DoesNotMaskAnotherDefect() {
        var spec = ArgumentSpecCatalog.For(ToolFor("poly_simulate"));
        var outcome = ArgumentValidator.Validate("poly_simulate",
            Args(("steps", JsonSerializer.SerializeToElement(42))), spec, ArgumentLimits.Registry);

        await Assert.That(outcome.InsertEmptyWorkspace).IsFalse()
            .Because("a wrong-typed steps must be reported, not masked by the missing-workspace insert");
        await Assert.That(outcome.Verdict?.ErrorCode).IsEqualTo("INVALID_ARGUMENT");
        await Assert.That(outcome.Verdict!.Value.Message).Contains("steps");
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
        await Assert.That(big.Verdict.HasValue).IsFalse()
            .Because("G-09 owns the first registry entry; until it lands the registry rejects nothing");
    }

    /// <summary>
    /// S-a (N2): every parameter that is nullable in the signature has a published schema that
    /// accepts JSON null, so an explicit null on a nullable optional string is a valid call.
    /// </summary>
    [Test]
    public async Task ToolSignatures_NullableParameters_AcceptExplicitNull() {
        var nullability = new NullabilityInfoContext();
        foreach (var method in typeof(PolySurfaceTools).GetMethods()) {
            var attribute = method.GetCustomAttribute<McpServerToolAttribute>();
            if (attribute?.Name is null)
                continue;
            var spec = ArgumentSpecCatalog.For(ToolFor(attribute.Name));
            foreach (var parameter in method.GetParameters()) {
                if (nullability.Create(parameter).WriteState != NullabilityState.Nullable)
                    continue;
                var argument = spec.Single(candidate => candidate.Name == parameter.Name);
                await Assert.That(argument.JsonTypes.Contains("null")).IsTrue()
                    .Because($"{attribute.Name}.{parameter.Name} is nullable in the signature, so its published schema must accept null");
            }
        }
    }

    /// <summary>
    /// N2: an explicit null is valid on a nullable string and invalid on a non-nullable boolean,
    /// which is the line between "absent-like" and "wrong type".
    /// </summary>
    [Test]
    public async Task Validator_AcceptsNullOnANullableString_RejectsNullOnABoolean() {
        var guide = ArgumentSpecCatalog.For(ToolFor("poly_guide"));
        var accepted = ArgumentValidator.Validate("poly_guide",
            Args(("topic", JsonSerializer.SerializeToElement((string?)null))), guide, ArgumentLimits.Registry);
        await Assert.That(accepted.Verdict.HasValue).IsFalse()
            .Because("poly_guide.topic is a nullable string; an explicit null is a valid quickstart call");

        var simulate = ArgumentSpecCatalog.For(ToolFor("poly_simulate"));
        var resetNull = ArgumentValidator.Validate("poly_simulate",
            Args(
                ("workspace", JsonSerializer.SerializeToElement("ws")),
                ("steps", JsonSerializer.SerializeToElement("[]")),
                ("reset", JsonSerializer.SerializeToElement((bool?)null))),
            simulate, ArgumentLimits.Registry);
        await Assert.That(resetNull.Verdict?.ErrorCode).IsEqualTo("INVALID_ARGUMENT");
        await Assert.That(resetNull.Verdict!.Value.Message).Contains("reset");
        await Assert.That(resetNull.Verdict!.Value.Message).Contains("boolean");

        var define = ArgumentSpecCatalog.For(ToolFor("poly_define"));
        var applyNull = ArgumentValidator.Validate("poly_define",
            Args(
                ("workspace", JsonSerializer.SerializeToElement("ws")),
                ("poly", JsonSerializer.SerializeToElement("domain V2")),
                ("apply", JsonSerializer.SerializeToElement((bool?)null))),
            define, ArgumentLimits.Registry);
        await Assert.That(applyNull.Verdict?.ErrorCode).IsEqualTo("INVALID_ARGUMENT");
        await Assert.That(applyNull.Verdict!.Value.Message).Contains("apply");
    }

    /// <summary>S-b (N3): two wrong types in one call report the first in parameter order.</summary>
    [Test]
    public async Task Validator_TwoWrongTypes_ReportsParameterOrder() {
        var spec = new[] {
            new ArgumentSpec("first", new HashSet<string>(StringComparer.Ordinal) { "string" }, Required: false),
            new ArgumentSpec("second", new HashSet<string>(StringComparer.Ordinal) { "string" }, Required: false),
        };
        var outcome = ArgumentValidator.Validate("synthetic",
            Args(("first", JsonSerializer.SerializeToElement(true)), ("second", JsonSerializer.SerializeToElement(true))),
            spec, ArgumentLimits.Registry);

        await Assert.That(outcome.Verdict!.Value.Message).Contains("first");
        await Assert.That(outcome.Verdict!.Value.Message).DoesNotContain("second");
    }

    /// <summary>
    /// N3: adjacent precedence pairs. Unknown beats a limit; a limit beats a missing required
    /// argument; a missing required argument beats a wrong kind; a wrong kind beats the
    /// missing-workspace insert, which is the last resort.
    /// </summary>
    [Test]
    public async Task Validator_ReportsTheFirstPrecedenceCategory() {
        var guide = ArgumentSpecCatalog.For(ToolFor("poly_guide"));
        var limits = new Dictionary<(string Tool, string Argument), ArgumentLimit> {
            [("poly_guide", "topic")] = new(MaxChars: 8, MaxItems: null),
            [("poly_export", "inline")] = new(MaxChars: 3, MaxItems: null),
        };

        var unknownAndLimit = ArgumentValidator.Validate("poly_guide",
            Args(("topic", JsonSerializer.SerializeToElement(new string('x', 40))), ("bogus", JsonSerializer.SerializeToElement(1))),
            guide, limits);
        await Assert.That(unknownAndLimit.Verdict!.Value.ErrorCode).IsEqualTo("UNKNOWN_ARGUMENT");

        var export = ArgumentSpecCatalog.For(ToolFor("poly_export"));

        var limitAndMissing = ArgumentValidator.Validate("poly_export",
            Args(("workspace", JsonSerializer.SerializeToElement("ws")), ("inline", JsonSerializer.SerializeToElement("toolong"))),
            export, limits);
        await Assert.That(limitAndMissing.Verdict!.Value.ErrorCode).IsEqualTo("INVALID_ARGUMENT");
        await Assert.That(limitAndMissing.Verdict!.Value.Message).Contains("inline");

        var missingAndKind = ArgumentValidator.Validate("poly_export",
            Args(("workspace", JsonSerializer.SerializeToElement("ws")), ("inline", JsonSerializer.SerializeToElement("yes"))),
            export, ArgumentLimits.Registry);
        await Assert.That(missingAndKind.Verdict!.Value.ErrorCode).IsEqualTo("INVALID_ARGUMENT");
        await Assert.That(missingAndKind.Verdict!.Value.Message).Contains("format");

        // A valid workspace with a missing format takes the missing-required branch and names format.
        var missingFormat = ArgumentValidator.Validate("poly_export",
            Args(("workspace", JsonSerializer.SerializeToElement("ws"))), export, ArgumentLimits.Registry);
        await Assert.That(missingFormat.Verdict!.Value.ErrorCode).IsEqualTo("INVALID_ARGUMENT");
        await Assert.That(missingFormat.Verdict!.Value.Message).Contains("format");

        var simulate = ArgumentSpecCatalog.For(ToolFor("poly_simulate"));
        var kindBeforeWorkspace = ArgumentValidator.Validate("poly_simulate",
            Args(("steps", JsonSerializer.SerializeToElement(42))), simulate, ArgumentLimits.Registry);
        await Assert.That(kindBeforeWorkspace.Verdict!.Value.ErrorCode).IsEqualTo("INVALID_ARGUMENT");
        await Assert.That(kindBeforeWorkspace.Verdict!.Value.Message).Contains("steps");
        await Assert.That(kindBeforeWorkspace.InsertEmptyWorkspace).IsFalse();

        var onlyMissingWorkspace = ArgumentValidator.Validate("poly_export",
            Args(("format", JsonSerializer.SerializeToElement("dsl"))), export, ArgumentLimits.Registry);
        await Assert.That(onlyMissingWorkspace.Verdict.HasValue).IsFalse();
        await Assert.That(onlyMissingWorkspace.InsertEmptyWorkspace).IsTrue();
    }

    /// <summary>N4: the schema map fails closed on every shape it cannot read.</summary>
    [Test]
    public async Task ArgumentSpecCatalog_ThrowsOnAnUnmappableSchema() {
        await Assert.That(() => ArgumentSpecCatalog.Map("noProperties",
            JsonDocument.Parse("""{"type":"object"}""").RootElement)).Throws<InvalidOperationException>();
        await Assert.That(() => ArgumentSpecCatalog.Map("noType",
            JsonDocument.Parse("""{"type":"object","properties":{"x":{"description":"x"}}}""").RootElement)).Throws<InvalidOperationException>();
        await Assert.That(() => ArgumentSpecCatalog.Map("unpublishedRequired",
            JsonDocument.Parse("""{"type":"object","properties":{"x":{"type":"string"}},"required":["y"]}""").RootElement)).Throws<InvalidOperationException>();
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

    private static McpServerTool ToolFor(string name) {
        var surface = new PolySurfaceTools(McpTestTools.Store, NullLogger<PolySurfaceTools>.Instance);
        var method = typeof(PolySurfaceTools).GetMethods()
            .Single(candidate => candidate.GetCustomAttribute<McpServerToolAttribute>()?.Name == name);
        return McpServerTool.Create(method, surface, new McpServerToolCreateOptions());
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