using Poly.DomainModeling.Language;
using Poly.Mcp.Surface;

namespace Poly.Tests.Mcp;

public class DslOutlineTests {
    private const string Doc = """
        domain Shop

        // Orders come first
        Zebra: entity {
          Total: Number range(0, 100) // sum so far
          Note: Text optional
        }

        Alpha: entity {
          Name: Text
        }
        """;

    private static string Slice(string text, OutlineNode node) => text[node.StartOffset..node.EndOffset];

    [Test]
    public async Task Outline_FindsTopLevelEntitiesInOrder() {
        var outline = DslOutline.Build(Doc);

        var entities = outline.Roots.Where(n => n.Kind == "entity").ToList();

        await Assert.That(entities.Select(e => e.Name).ToArray()).IsEquivalentTo(new[] { "Zebra", "Alpha" });
        await Assert.That(Slice(Doc, entities[0])).StartsWith("Zebra: entity {");
        await Assert.That(Slice(Doc, entities[0])).EndsWith("}");
        await Assert.That(entities[0].Children.Select(c => c.Path).ToArray())
            .IsEquivalentTo(new[] { "Zebra.Total", "Zebra.Note" });
        await Assert.That(Slice(Doc, entities[0].Children[0])).IsEqualTo("Total: Number range(0, 100)");
    }

    [Test]
    public async Task Outline_HandlesSingleLineBlock() {
        const string text = "domain D\nE: entity {\n  Live: stage {}\n  Name: Text\n}";

        var outline = DslOutline.Build(text);
        var live = outline.Find("E.Live", out var code, out _);

        await Assert.That(code).IsNull();
        await Assert.That(live).IsNotNull();
        await Assert.That(Slice(text, live!)).IsEqualTo("Live: stage {}");
        await Assert.That(Slice(text, outline.Find("E.Name", out _, out _)!)).IsEqualTo("Name: Text");
    }

    [Test]
    public async Task Outline_HandlesSameLineMembers() {
        const string text = "domain D\nE: entity {\n  Lexeme: Text Note: Text optional\n  Other: Text\n}";

        var outline = DslOutline.Build(text);
        var lexeme = outline.Find("E.Lexeme", out var code, out var error);

        await Assert.That(lexeme).IsNull();
        await Assert.That(code).IsEqualTo("EDIT_NOT_ADDRESSABLE");
        await Assert.That(error!).Contains("line 3");
        await Assert.That(outline.Find("E.Other", out var otherCode, out _)).IsNotNull();
        await Assert.That(otherCode).IsNull();
    }

    [Test]
    public async Task Outline_SkipsBracesInCommentsAndStrings() {
        const string text = "domain D\nE: entity {\n  // stray } and {\n  Code: Text pattern(\"}{\")\n  Next: Text\n}\nF: entity {\n  X: Text\n}";

        var outline = DslOutline.Build(text);

        await Assert.That(outline.Roots.Where(n => n.Kind == "entity").Select(n => n.Name).ToArray())
            .IsEquivalentTo(new[] { "E", "F" });
        await Assert.That(outline.Find("E.Next", out _, out _)).IsNotNull();
        await Assert.That(Slice(text, outline.Find("E.Code", out _, out _)!)).IsEqualTo("Code: Text pattern(\"}{\")");
    }

    [Test]
    public async Task Outline_UsesTabsAndCrlfIndent() {
        const string text = "domain D\r\nE: entity {\r\n\tName: Text\r\n\tAge: Number\r\n}";

        var outline = DslOutline.Build(text);
        var name = outline.Find("E.Name", out _, out _);

        await Assert.That(name).IsNotNull();
        await Assert.That(name!.Indent).IsEqualTo("\t");
        await Assert.That(Slice(text, name)).IsEqualTo("Name: Text");
        await Assert.That(name.StartLine).IsEqualTo(3);
    }

    [Test]
    public async Task Outline_RejectsUnnamedMembers() {
        const string text = "domain D\nE: entity {\n  Draft: stage {\n    entry { }\n    Submit: action { transition to Active }\n  }\n  Active: stage {}\n}";

        var outline = DslOutline.Build(text);
        var entry = outline.Find("E.Draft.entry", out var code, out _);

        await Assert.That(entry).IsNull();
        await Assert.That(code).IsEqualTo("EDIT_NOT_ADDRESSABLE");
        await Assert.That(outline.Find("E.Draft.Submit", out _, out _)).IsNotNull();
        await Assert.That(outline.Find("E.Missing", out var missing, out _)).IsNull();
        await Assert.That(missing).IsEqualTo("EDIT_PATH_NOT_FOUND");
    }

    [Test]
    public async Task Token_OffsetAndLengthAreSourceExact() {
        const string text = "A: \"x\\\"y\"\n  B 12 \"open";
        var reader = new DslTokenReader(text);
        var tokens = new List<DslToken>();
        for (var t = reader.Peek(); t.Kind != DslTokenKind.EndOfFile; t = reader.Peek()) {
            tokens.Add(t);
            reader.Consume(1);
        }

        await Assert.That(tokens.Select(t => text.Substring(t.Offset, t.Length)).ToArray())
            .IsEquivalentTo(new[] { "A", ":", "\"x\\\"y\"", "B", "12", "\"open" });
        await Assert.That(tokens[2].Text).IsEqualTo("x\"y");
        await Assert.That(tokens.Select(t => (t.Line, t.Col)).ToArray())
            .IsEquivalentTo(new[] { (1, 1), (1, 2), (1, 4), (2, 3), (2, 5), (2, 8) });
    }
}