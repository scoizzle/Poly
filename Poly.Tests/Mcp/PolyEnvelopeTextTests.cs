using System.Text.Json;

using ModelContextProtocol.Protocol;

using Poly.Mcp;
using Poly.Mcp.Surface;

namespace Poly.Tests.Mcp;

/// <summary>
/// The text block of a v2 result is a fixed-prose projection of the structured envelope:
/// essentials and flattened data, never serialized JSON.
/// </summary>
public class PolyEnvelopeTextTests {
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static string Text(string json) => PolyEnvelopeText.ProjectText(Parse(json));

    [Test]
    public async Task Failure_ReportsAdvertisedEnvelope() {
        var result = ToolExceptionBoundary.Failure(new InvalidOperationException("boom"));

        await Assert.That(result.IsError).IsTrue();
        var envelope = result.StructuredContent!.Value;
        await Assert.That(envelope.GetProperty("ok").GetBoolean()).IsFalse();
        var summary = envelope.GetProperty("summary").GetString();
        await Assert.That(summary).Contains("InvalidOperationException");
        await Assert.That(summary).Contains("boom");
        await Assert.That(envelope.GetProperty("errorCode").GetString()).IsEqualTo("INTERNAL_ERROR");
        await Assert.That(envelope.TryGetProperty("data", out _)).IsFalse();
        await Assert.That(envelope.TryGetProperty("state", out _)).IsFalse();
        var text = ((TextContentBlock)result.Content[0]).Text;
        await Assert.That(text).StartsWith("error: INTERNAL_ERROR");
        await Assert.That(text).Contains("boom");
    }

    [Test]
    public async Task UnknownArgumentEnvelope_TextCarriesCodeAndSummary() {
        var text = Text("""
            {"ok":false,"summary":"Unknown argument(s) for poly_guide: bogus. Valid: topic.","findings":[],"data":null,"next":["poly_guide"],"state":null,"errorCode":"UNKNOWN_ARGUMENT"}
            """);

        await Assert.That(text).StartsWith("error: UNKNOWN_ARGUMENT — ");
        await Assert.That(text).Contains("Valid: topic");
        await Assert.That(text).Contains("next: poly_guide");
    }

    [Test]
    public async Task Finding_LineCarriesPositionHintAndFix() {
        var text = Text("""
            {"ok":false,"summary":"Rejected.","findings":[{"severity":"Error","code":"POLY042","path":"Product.Total","message":"Out of range.","hint":"Widen it.","fix":"range(0, 500)","line":4,"column":9}],"next":[]}
            """);

        await Assert.That(text).Contains(
            "finding: Error POLY042 Product.Total [4:9] Out of range. hint: Widen it. fix: range(0, 500)");
    }

    [Test]
    public async Task Findings_OverCap_AddsMoreLine() {
        var findings = string.Join(",", Enumerable.Range(0, 25).Select(i =>
            $$"""{"severity":"Warning","code":"W{{i}}","path":"P{{i}}","message":"m{{i}}"}"""));
        var text = Text($$"""{"ok":false,"summary":"s","findings":[{{findings}}],"next":[]}""");

        await Assert.That(text.Split('\n').Count(l => l.StartsWith("finding: ", StringComparison.Ordinal)))
            .IsEqualTo(PolyEnvelopeText.FindingCap);
        await Assert.That(text).Contains("+5 more");
    }

    [Test]
    public async Task Result_WithoutStructuredContent_PassesThrough() {
        var original = new CallToolResult { Content = [new TextContentBlock { Text = "plain" }] };

        await Assert.That(PolyEnvelopeText.Apply(original)).IsSameReferenceAs(original);
    }

    [Test]
    public async Task Apply_ReplacesContentAndPreservesIsError() {
        var envelope = Parse("""{"ok":false,"summary":"nope","errorCode":"X"}""");
        var result = PolyEnvelopeText.Apply(new CallToolResult {
            IsError = true,
            StructuredContent = envelope,
            Content = [new TextContentBlock { Text = envelope.GetRawText() }],
        });

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Content).Count().IsEqualTo(1);
        await Assert.That(((TextContentBlock)result.Content[0]).Text).StartsWith("error: X — nope");
        await Assert.That(result.StructuredContent!.Value.GetRawText()).IsEqualTo(envelope.GetRawText());
    }

    [Test]
    public async Task Workspace_StateLine() {
        var text = Text("""
            {"ok":true,"summary":"Created.","state":{"workspace":"ws-1","model":"V2","revision":3,"entities":1,"instances":0},"next":["poly_define(workspace=\"ws-1\")","poly_check"]}
            """);

        await Assert.That(text).StartsWith("ok — Created.");
        await Assert.That(text).Contains("workspace: ws-1 model=V2 rev=3 entities=1 instances=0");
        await Assert.That(text).Contains("next: poly_define(workspace=\"ws-1\"), poly_check");
        await Assert.That(text).DoesNotContain("\"findings\"");
    }

    [Test]
    public async Task Guide_BodyIsInline() {
        var text = Text("""{"ok":true,"summary":"Poly quickstart.","data":{"guide":"# Quickstart\nline two"},"next":[]}""");

        await Assert.That(text).Contains("# Quickstart\nline two");
    }

    [Test]
    public async Task Data_IsFlattenedToKeyValueLines() {
        var text = Text("""
            {"ok":true,"summary":"s","data":{"a":{"b":1,"c":[true,"x"]},"n":null,"sessions":[{"id":"ws-1"}]},"next":[]}
            """);

        await Assert.That(text).Contains("a.b: 1");
        await Assert.That(text).Contains("a.c[0]: true");
        await Assert.That(text).Contains("a.c[1]: x");
        await Assert.That(text).Contains("sessions[0].id: ws-1");
        await Assert.That(text).DoesNotContain("n:");
    }

    [Test]
    public async Task Simulate_StepLineCarriesSummaryAndData() {
        var text = Text("""
            {"ok":true,"summary":"Scenario ran.","data":{"trace":[{"index":0,"op":"create","ok":true,"summary":"Created.","data":{"id":"i-1","stage":"Draft"}},{"index":1,"op":"invoke","ok":false,"summary":"Blocked.","data":null}],"count":2,"failedAt":1},"next":[]}
            """);

        await Assert.That(text).Contains("step 0 create ok: Created. — id=i-1, stage=Draft");
        await Assert.That(text).Contains("step 1 invoke FAILED: Blocked.");
        await Assert.That(text).Contains("count: 2");
    }

    [Test]
    public async Task Witness_IsACountLine() {
        var text = Text("""
            {"ok":true,"summary":"Proven.","data":{"verdict":"proven","witness":[{"x":1},{"x":2}]},"next":[]}
            """);

        await Assert.That(text).Contains("witness: 2 context(s)");
        await Assert.That(text).DoesNotContain("witness[0]");
    }

    [Test]
    public async Task Export_InlineAndPathLines() {
        var inline = Text("""{"ok":true,"summary":"s","data":{"format":"dsl","content":"domain X","bytes":8},"next":[]}""");
        var file = Text("""{"ok":true,"summary":"s","data":{"format":"csharp","path":"/tmp/a.cs","bytes":5000,"sha256":"abc"},"next":[]}""");

        await Assert.That(inline).Contains("artifact dsl 8B\ndomain X");
        await Assert.That(file).Contains("artifact csharp /tmp/a.cs 5000B sha256=abc");
    }

    [Test]
    public async Task MissingOptionalParts_AreTolerated() {
        var text = Text("""{"ok":true,"summary":"bare"}""");

        await Assert.That(text).IsEqualTo("ok — bare");
    }
}