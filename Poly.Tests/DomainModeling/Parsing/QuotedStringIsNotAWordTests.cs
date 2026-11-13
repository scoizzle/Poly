using Poly.Compile;
using Poly.DomainModeling.Language;
using Poly.Packs.Product;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// G-13c: at the ten decision sites that read a token whose kind the code did not check, a quoted string is
/// not a word. Each test names its site (G-13a's baseline label) and states what the neighbouring code does
/// for a NUMBER at the same position, which is what a string now gets. Identifiers and keywords decide as
/// before; <c>ResolverBaselineTests</c> pins that.
/// </summary>
public sealed class QuotedStringIsNotAWordTests {
    private static string Doc(string body) => "domain A\n" + body + "\n";

    private static IReadOnlyList<OutlineNode> Roots(string text) => DslOutline.Build(text).Roots;

    // Entities.fallback.unsupported (DomainResolver.Entities.cs:265): `Go: 5` reports the positioned
    // "Expected type..." error; a string is no longer the unsupported keyword it spells.
    [Test]
    public async Task EntitiesFallback_StringSpellingAnUnsupportedKeyword_IsNotThatKeyword() {
        var result = ResolverBaselineTests.Resolve(Doc("X: entity {\n  Go: \"actor\"\n}"));
        await Assert.That(result).IsEqualTo("ERR Expected type, stage, action, policy, or navigation property after 'Go:' @3:7");
    }

    // TopLevel.enum.reserved.word (DomainResolver.TopLevel.cs:156-158): `Red 5` reports "Expected RBrace, got '5' (Number)".
    [Test]
    public async Task EnumReserved_StringAfterMember_IsNotAReservedWord() {
        var result = ResolverBaselineTests.Resolve(Doc("S: enum { Red \"abc\" }"));
        await Assert.That(result).IsEqualTo("ERR Expected RBrace, got 'abc' (StringLiteral) @2:15");
    }

    [Test]
    public async Task EnumMember_QuotedMember_IsNotAReservedWord() {
        var result = ResolverBaselineTests.Resolve(Doc("S: enum { \"A\" }"));
        await Assert.That(result).IsEqualTo("ERR Expected RBrace, got 'A' (StringLiteral) @2:11");
    }

    [Test]
    public async Task EnumMemberAnnotation_QuotedAnnotationName_IsNotAReservedWord() {
        var result = ResolverBaselineTests.Resolve(Doc("S: enum { A \"label\"(\"x\") }"));
        await Assert.That(result).IsEqualTo("ERR Expected RBrace, got 'label' (StringLiteral) @2:13");
    }

    // Outline.domain (DslOutline.cs:151): `domain 5` is labelled by its source slice, "5". A string is labelled
    // by its source slice too, quotes and escapes included, not decoded into a name.
    [Test]
    public async Task OutlineDomain_StringName_IsLabelledBySourceSliceNotDecoded() {
        var roots = Roots("domain \"A\\\"b\"\nX: entity { }\n");
        await Assert.That(roots[0].Kind).IsEqualTo("domain");
        await Assert.That(roots[0].Name).IsEqualTo("\"A\\\"b\"");
    }

    // Outline.public-value (DslOutline.cs:166): `public Value V { }` (not the word) is not a value type.
    [Test]
    public async Task OutlinePublicValue_QuotedValue_IsNotAValueType() {
        var roots = Roots(Doc("public \"value\" V { A: Text }"));
        await Assert.That(roots.Select(r => r.Kind).ToArray()).IsEquivalentTo(new[] { "domain", "unknown" });
    }

    // Outline.top.kind (DslOutline.cs:336): `X: 5` outlines as unknown.
    [Test]
    public async Task OutlineTopKind_QuotedEnum_IsUnknownNotAnEnum() {
        var roots = Roots(Doc("X: \"enum\" { A }"));
        await Assert.That(roots[1].Kind).IsEqualTo("unknown");
        await Assert.That(roots[1].Name).IsEqualTo("X");
    }

    [Test]
    public async Task OutlineTopKind_QuotedEntity_IsUnknownNotAnEntity() {
        var roots = Roots(Doc("X: \"entity\" { N: Text }"));
        await Assert.That(roots[1].Kind).IsEqualTo("unknown");
        await Assert.That(roots[1].Children.Count).IsEqualTo(0);
    }

    // Outline.marked.kind (DslOutline.cs:365) is unreachable for a string once :166 checks the word; the
    // kind of `public value V` is still the word.
    // Outline.marked.name (DslOutline.cs:366): `public value 5 { }` is labelled "5", the source slice.
    [Test]
    public async Task OutlineMarkedName_QuotedName_IsLabelledBySourceSlice() {
        var roots = Roots(Doc("public value \"V\" { N: Text }"));
        await Assert.That(roots[1].Kind).IsEqualTo("value");
        await Assert.That(roots[1].Name).IsEqualTo("\"V\"");
    }

    // Outline.member.public-action (DslOutline.cs:479): `public action 5 { }` is labelled "5".
    [Test]
    public async Task OutlinePublicAction_QuotedName_IsLabelledBySourceSlice() {
        var roots = Roots(Doc("X: entity {\n  public action \"Go\" { }\n}"));
        var action = roots[1].Children.Single();
        await Assert.That(action.Kind).IsEqualTo("action");
        await Assert.That(action.Name).IsEqualTo("\"Go\"");
    }

    // Edit.top-kind (PolyEdit.cs:439): `T: 5` is classed an entity and appended after the last entity; a quoted
    // "enum" is not placed with the enums.
    [Test]
    public async Task EditTopKind_QuotedEnum_IsPlacedAsAnEntityLikeANumber() {
        const string source = "domain A\nX: entity {\n  N: Text\n}\nS: enum { A, B }\n";
        string Add(string dsl) {
            var outcome = PolyEdit.Apply(source,
                "[{\"op\":\"add\",\"parent\":\"\",\"dsl\":" + System.Text.Json.JsonSerializer.Serialize(dsl) + "}]");
            return outcome.Applied[0].Line.ToString();
        }
        await Assert.That(Add("T: \"enum\" { A }")).IsEqualTo(Add("T: 5"));
    }

    // Compiler.extension (PolyCompiler.cs:320): the scan for an extension's `uses` token matches an identifier. With
    // no header `uses`, the host's implicit ids are checked; an unknown one is reported unplaced, not on a later
    // `uses "nope"` whose string spells the same id.
    [Test]
    public async Task CompilerUsesScan_QuotedIdAfterTheHeader_IsNotMatched() {
        var compiler = new PolyCompiler(ProductCatalog.Catalog, new SessionOptions(ProductCatalog.Catalog, ["nope"]));
        var result = compiler.Analyze("domain A\nX: entity { }\nuses \"nope\"\n", "t.poly");
        var diagnostic = result.Diagnostics.Single(d => d.Code == "DSL0002");
        await Assert.That(diagnostic.Placed).IsFalse();
    }
}
