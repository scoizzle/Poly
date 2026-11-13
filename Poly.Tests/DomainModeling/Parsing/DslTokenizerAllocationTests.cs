using Poly.DomainModeling.Language;
using Poly.Grammar;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// G-12: the DSL tokenizer scans onto spans, so reading the token stream allocates no string for a word, a
/// keyword, punctuation, a number or a string literal; text is materialised only when a consumer asks for it.
/// Allocation is measured with <c>GetAllocatedBytesForCurrentThread</c> (a per-thread counter, so parallel tests
/// cannot disturb it) inside a non-async helper, so TUnit cannot hop threads mid-measurement, after one warming
/// <c>Peek</c>.
/// </summary>
public class DslTokenizerAllocationTests {
    private static bool IsKeyword(DslTokenKind kind) => kind is
        not (DslTokenKind.EndOfFile or DslTokenKind.Identifier or DslTokenKind.Number or DslTokenKind.StringLiteral)
        && char.IsLetter(DslGrammar.CanonicalText(kind)[0]);

    private static bool IsPunctuation(DslTokenKind kind) => kind is
        not (DslTokenKind.EndOfFile or DslTokenKind.Identifier or DslTokenKind.Number or DslTokenKind.StringLiteral)
        && !char.IsLetter(DslGrammar.CanonicalText(kind)[0]);

    /// <summary>Bytes allocated while reading every token of <paramref name="text"/> after the first; counts kinds into <paramref name="kinds"/>.</summary>
    private static long MeasureTokenize(string text, int[] kinds) {
        var reader = new DslTokenReader(text);
        kinds[(int)reader.Peek().Kind]++;
        reader.Consume(1);

        var before = GC.GetAllocatedBytesForCurrentThread();
        while (true) {
            var kind = reader.Peek().Kind;
            kinds[(int)kind]++;
            if (kind == DslTokenKind.EndOfFile)
                break;
            reader.Consume(1);
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Test]
    public async Task Tokenize_GoldenCorpus_AllocatesNoStringForWordsKeywordsPunctuation() {
        var kinds = new int[Enum.GetValues<DslTokenKind>().Length];
        var perDocument = new List<(string Name, long Bytes)>();
        foreach (var domain in GoldenDomains.Compilable)
            perDocument.Add((domain.Name, MeasureTokenize(domain.Poly, kinds)));

        await Assert.That(perDocument.Count).IsEqualTo(GoldenDomains.Compilable.Count);
        await Assert.That(kinds.Sum()).IsGreaterThan(4000);
        await Assert.That(kinds[(int)DslTokenKind.Identifier]).IsGreaterThan(0);
        await Assert.That(kinds[(int)DslTokenKind.Number]).IsGreaterThan(0);
        await Assert.That(kinds[(int)DslTokenKind.StringLiteral]).IsGreaterThan(0);
        await Assert.That(Enum.GetValues<DslTokenKind>().Where(IsKeyword).Sum(k => kinds[(int)k])).IsGreaterThan(0);
        await Assert.That(Enum.GetValues<DslTokenKind>().Where(IsPunctuation).Sum(k => kinds[(int)k])).IsGreaterThan(0);
        await Assert.That(perDocument.Where(d => d.Bytes != 0).Select(d => $"{d.Name}: {d.Bytes} bytes")).IsEmpty();
    }

    // Bytes allocated while materialising every identifier's slice of the same document after the first token.
    private static long MeasureSliceToString(string text, List<string> sink) {
        var reader = new DslTokenReader(text);
        sink.Add(reader.Peek().Span.ToString());
        reader.Consume(1);

        var before = GC.GetAllocatedBytesForCurrentThread();
        while (true) {
            var t = reader.Peek();
            if (t.Kind == DslTokenKind.EndOfFile)
                break;
            if (t.Kind == DslTokenKind.Identifier)
                sink.Add(t.Span.ToString());
            reader.Consume(1);
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    /// <summary>The meter sees an allocation in the same region shape, so a zero above is not a blind meter.</summary>
    [Test]
    public async Task Tokenize_PositiveControl_SliceToStringAllocates() {
        var text = GoldenDomains.Compilable[0].Poly;
        var sink = new List<string>(capacity: text.Length);
        var bytes = MeasureSliceToString(text, sink);

        await Assert.That(sink.Count).IsGreaterThan(10);
        await Assert.That(bytes).IsGreaterThan(0L);
    }

    [Test]
    public async Task DslToken_IsTwentyFourBytes() =>
        await Assert.That(System.Runtime.CompilerServices.Unsafe.SizeOf<DslToken>()).IsEqualTo(24);

    [Test]
    public async Task Name_Identifiers_InternOnce_KeywordsAndValuesNeverInterned() {
        const string text = "domain A\n\nA: entity {\n  B: Text default \"A\"\n  C: Number range(1, 2)\n}\n";
        var reader = new DslTokenReader(text);
        var tokens = new List<DslToken>();
        for (var t = reader.Peek(); t.Kind != DslTokenKind.EndOfFile; t = reader.Peek()) {
            tokens.Add(t);
            reader.Consume(1);
        }

        await Assert.That(reader.Source.Names.Count).IsEqualTo(0);
        var identifiers = tokens.Where(t => t.Kind == DslTokenKind.Identifier).ToList();
        var names = identifiers.Select(t => t.Name()).ToList();
        await Assert.That(names).IsEquivalentTo(["A", "A", "B", "C"]);
        await Assert.That(ReferenceEquals(names[0], names[1])).IsTrue();
        await Assert.That(reader.Source.Names.Count).IsEqualTo(3);

        var keywords = tokens.Where(t => t.Kind is DslTokenKind.Domain or DslTokenKind.Entity or DslTokenKind.Text or DslTokenKind.Equals).ToList();
        await Assert.That(keywords.Select(t => t.Name())).IsEquivalentTo(["domain", "entity", "Text", "default"]);
        await Assert.That(reader.Source.Names.Count).IsEqualTo(3);
    }

    [Test]
    public async Task Name_NotAWord_Throws_AndUnescape_NotAString_Throws() {
        var reader = new DslTokenReader("1.5 \"s\" : x");
        var number = reader.Peek(0);
        var literal = reader.Peek(1);
        var colon = reader.Peek(2);
        var word = reader.Peek(3);
        var end = reader.Peek(4);

        await Assert.That(() => number.Name()).Throws<InvalidOperationException>();
        await Assert.That(() => literal.Name()).Throws<InvalidOperationException>();
        await Assert.That(() => colon.Name()).Throws<InvalidOperationException>();
        await Assert.That(() => end.Name()).Throws<InvalidOperationException>();
        await Assert.That(() => word.Unescape()).Throws<InvalidOperationException>();
        await Assert.That(() => number.Unescape()).Throws<InvalidOperationException>();
        await Assert.That(reader.Source.Names.Count).IsEqualTo(0);
        await Assert.That(literal.Unescape()).IsEqualTo("s");
    }

    [Test]
    public async Task DslToken_Default_PrintsWithoutThrowing() {
        var text = default(DslToken).ToString();
        var real = new DslTokenReader("domain A").Peek(1).ToString();

        await Assert.That(text).IsEqualTo("DslToken { Kind = EndOfFile, Start = 0, Length = 0, Source = null }");
        await Assert.That(real).IsEqualTo("DslToken { Kind = Identifier, Start = 7, Length = 1, Line = 1, Col = 8, Span = A }");
    }

    [Test]
    public async Task DslToken_Equality_IsKindStartLengthAndSourceReference() {
        var source = new SourceText("a a");
        var other = new SourceText("a a");
        var a = new DslToken(DslTokenKind.Identifier, 0, 1, source);

        await Assert.That(a).IsEqualTo(new DslToken(DslTokenKind.Identifier, 0, 1, source));
        await Assert.That(a.GetHashCode()).IsEqualTo(new DslToken(DslTokenKind.Identifier, 0, 1, source).GetHashCode());
        await Assert.That(a).IsNotEqualTo(new DslToken(DslTokenKind.Identifier, 0, 1, other));
        await Assert.That(a).IsNotEqualTo(new DslToken(DslTokenKind.Identifier, 2, 1, source));
        await Assert.That(a).IsNotEqualTo(new DslToken(DslTokenKind.Identifier, 0, 2, source));
        await Assert.That(a).IsNotEqualTo(new DslToken(DslTokenKind.Text, 0, 1, source));
        await Assert.That(new DslTokenReader(source).Peek(0)).IsEqualTo(a);
    }

    /// <summary>G-13's ratchet reads this shape: the obsolete shim is the only string-typed property or field.</summary>
    [Test]
    public async Task DslToken_OnlyStringMemberIsTheObsoleteTextShim() {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
        var stringProperties = typeof(DslToken).GetProperties(flags).Where(p => p.PropertyType == typeof(string)).Select(p => p.Name).ToList();
        var stringFields = typeof(DslToken).GetFields(flags).Where(f => f.FieldType == typeof(string)).ToList();
        var obsolete = typeof(DslToken).GetProperty("Text")!.GetCustomAttributes(typeof(ObsoleteAttribute), false).Cast<ObsoleteAttribute>().Single();

        await Assert.That(stringProperties).IsEquivalentTo(["Text"]);
        await Assert.That(stringFields).IsEmpty();
        await Assert.That(obsolete.DiagnosticId).IsEqualTo("POLYTOK001");
        await Assert.That(typeof(DslToken).GetMethod(nameof(DslToken.Name), Type.EmptyTypes)!.ReturnType).IsEqualTo(typeof(string));
        await Assert.That(typeof(DslToken).GetMethod(nameof(DslToken.Unescape), Type.EmptyTypes)!.ReturnType).IsEqualTo(typeof(string));
        await Assert.That(typeof(DslToken).GetProperty("Offset")).IsNull();
    }

    [Test]
    public async Task Reader_SourceTextConstructor_TokensPointIntoThatSource() {
        var source = new SourceText("\uFEFFdomain A");
        var reader = new DslTokenReader(source);
        var name = reader.Peek(1);

        await Assert.That(ReferenceEquals(reader.Source, source)).IsTrue();
        await Assert.That(ReferenceEquals(name.Source, source)).IsTrue();
        await Assert.That((name.Start, name.Length, name.Line, name.Col)).IsEqualTo((8, 1, 1, 8));
        await Assert.That(new DslTokenReader("x").Source.Text.ToString()).IsEqualTo("x");
    }
}