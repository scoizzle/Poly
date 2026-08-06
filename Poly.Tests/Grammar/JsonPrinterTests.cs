using Poly.Grammar;

namespace Poly.Tests.Grammar;

// ─── Token writer with JSON-specific formatting ────────────
sealed class JsonTokenWriter : StringTokenWriter<JsonKind> {
    public override string CanonicalText(JsonKind kind) => kind switch {
        JsonKind.LBrace => "{",
        JsonKind.RBrace => "}",
        JsonKind.LBracket => "[",
        JsonKind.RBracket => "]",
        JsonKind.Colon => ":",
        JsonKind.Comma => ",",
        JsonKind.True => "true",
        JsonKind.False => "false",
        JsonKind.Null => "null",
        _ => base.CanonicalText(kind),
    };

    public override void WriteValue(JsonKind kind, string value) {
        switch (kind) {
            case JsonKind.String:
                WriteRaw("\"");
                WriteRaw(value);
                WriteRaw("\"");
                break;
            default:
                WriteRaw(value);
                break;
        }
    }
}

// ─── Tests ─────────────────────────────────────────────────
public sealed class JsonPrinterTests {
    private static Grammar<JsonKind> JsonValueGrammar() {
        var g = new Grammar<JsonKind>();
        g.Define("value")
            .Pattern("string").Value(JsonKind.String).Commit()
            .Pattern("number").Value(JsonKind.Number).Commit()
            .Pattern("true").Token(JsonKind.True).Commit()
            .Pattern("false").Token(JsonKind.False).Commit()
            .Pattern("null").Token(JsonKind.Null).Commit()
            .Pattern("object").Balanced(JsonKind.LBrace, JsonKind.RBrace).Commit()
            .Pattern("array").Balanced(JsonKind.LBracket, JsonKind.RBracket).Commit();
        return g;
    }

    // ═══════════════════════════════════════════════════════
    //  1. Fixed-token patterns (no content callback needed)
    // ═══════════════════════════════════════════════════════
    [Test]
    public async Task PrintTrue() {
        var g = JsonValueGrammar();
        var writer = new JsonTokenWriter();
        var printer = new Printer<JsonKind>(g, writer);

        printer.Print("true");
        await Assert.That(writer.GetOutput()).IsEqualTo("true");
    }

    [Test]
    public async Task PrintFalse() {
        var g = JsonValueGrammar();
        var writer = new JsonTokenWriter();
        var printer = new Printer<JsonKind>(g, writer);

        printer.Print("false");
        await Assert.That(writer.GetOutput()).IsEqualTo("false");
    }

    [Test]
    public async Task PrintNull() {
        var g = JsonValueGrammar();
        var writer = new JsonTokenWriter();
        var printer = new Printer<JsonKind>(g, writer);

        printer.Print("null");
        await Assert.That(writer.GetOutput()).IsEqualTo("null");
    }

    // ═══════════════════════════════════════════════════════
    //  2. Value-bearing patterns with content callback
    // ═══════════════════════════════════════════════════════
    [Test]
    public async Task PrintString() {
        var g = JsonValueGrammar();
        var writer = new JsonTokenWriter();
        var printer = new Printer<JsonKind>(g, writer);

        // "string" pattern = Token(JsonKind.String). 
        // MatchToken auto-emits via CanonicalText, but WriteValue handles quoting.
        // For Token-only patterns, the printer auto-emits. But without a content
        // callback, there's no value. So we use content to supply the value.
        printer.Print("string", ctx => {
            ctx.Emit(JsonKind.String, "hello");
        });
        await Assert.That(writer.GetOutput()).IsEqualTo("\"hello\"");
    }

    [Test]
    public async Task PrintNumber() {
        var g = JsonValueGrammar();
        var writer = new JsonTokenWriter();
        var printer = new Printer<JsonKind>(g, writer);

        printer.Print("number", ctx => {
            ctx.Emit(JsonKind.Number, "42");
        });
        await Assert.That(writer.GetOutput()).IsEqualTo("42");
    }

    // ═══════════════════════════════════════════════════════
    //  3. Object — Balanced body populated via callback
    // ═══════════════════════════════════════════════════════
    [Test]
    public async Task PrintEmptyObject() {
        var g = JsonValueGrammar();
        var writer = new JsonTokenWriter();
        var printer = new Printer<JsonKind>(g, writer);

        printer.Print("object");
        // No callback → compact: {}
        await Assert.That(writer.GetOutput()).IsEqualTo("{}");
    }

    [Test]
    public async Task PrintObjectWithMembers() {
        var g = JsonValueGrammar();
        var writer = new JsonTokenWriter();
        var printer = new Printer<JsonKind>(g, writer);

        printer.Print("object", ctx => {
            ctx.Emit(JsonKind.String, "name");
            ctx.Emit(JsonKind.Colon);
            ctx.Space();
            ctx.Emit(JsonKind.Number, "42");
        });

        var output = writer.GetOutput();
        // Balanced: { \n [content] \n }
        await Assert.That(output).StartsWith("{");
        await Assert.That(output).EndsWith("}");
        await Assert.That(output).Contains("\"name\"");
        await Assert.That(output).Contains(": 42");
    }

    // ═══════════════════════════════════════════════════════
    //  4. Array — Balanced with bracket delimiters
    // ═══════════════════════════════════════════════════════
    [Test]
    public async Task PrintEmptyArray() {
        var g = JsonValueGrammar();
        var writer = new JsonTokenWriter();
        var printer = new Printer<JsonKind>(g, writer);

        printer.Print("array");
        // No callback → compact: []
        await Assert.That(writer.GetOutput()).IsEqualTo("[]");
    }

    [Test]
    public async Task PrintArrayWithElements() {
        var g = JsonValueGrammar();
        var writer = new JsonTokenWriter();
        var printer = new Printer<JsonKind>(g, writer);

        printer.Print("array", ctx => {
            ctx.Emit(JsonKind.Number, "1");
            ctx.Emit(JsonKind.Comma);
            ctx.Space();
            ctx.Emit(JsonKind.Number, "2");
            ctx.Emit(JsonKind.Comma);
            ctx.Space();
            ctx.Emit(JsonKind.Number, "3");
        });

        var output = writer.GetOutput();
        await Assert.That(output).StartsWith("[");
        await Assert.That(output).EndsWith("]");
        await Assert.That(output).Contains("1, 2, 3");
    }

    // ═══════════════════════════════════════════════════════
    //  5. Round-trip: print then parse
    // ═══════════════════════════════════════════════════════
    [Test]
    public async Task RoundTrip_ObjectThenParse() {
        var g = JsonValueGrammar();
        var writer = new JsonTokenWriter();
        var printer = new Printer<JsonKind>(g, writer);

        printer.Print("object", ctx => {
            ctx.Emit(JsonKind.String, "name");
            ctx.Emit(JsonKind.Colon);
            ctx.Space();
            ctx.Emit(JsonKind.String, "hello");
        });

        var printed = writer.GetOutput();

        // Now parse it back via the matcher to verify round-trip
        var reader = new JsonTokenizer(printed);
        var matcher = new Matcher<JsonKind>(g, reader);
        var result = matcher.TryMatch("value");
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.PatternName).IsEqualTo("object");
    }

    // ═══════════════════════════════════════════════════════
    //  6. Lookup by rule name — uses first matching pattern
    // ═══════════════════════════════════════════════════════
    [Test]
    public async Task PrintNamedPatternNotFound_Throws() {
        var g = JsonValueGrammar();
        var writer = new JsonTokenWriter();
        var printer = new Printer<JsonKind>(g, writer);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => {
            printer.Print("nonexistent");
            return Task.CompletedTask;
        });
        await Assert.That(ex!.Message).Contains("nonexistent");
    }

    // ═══════════════════════════════════════════════════════
    //  7. Canonical text for unconfigured kinds uses enum name
    // ═══════════════════════════════════════════════════════
    [Test]
    public async Task CanonicalText_DefaultsToLowercaseEnumName() {
        // Use a fresh writer without JsonTokenWriter's overrides
        var writer = new StringTokenWriter<JsonKind>();
        writer.Write(JsonKind.LBrace);
        writer.Write(JsonKind.Colon);
        writer.Write(JsonKind.True);
        await Assert.That(writer.GetOutput()).IsEqualTo("lbracecolontrue");
    }
}