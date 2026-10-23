using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using Poly.Interpretation;
using Poly.Interpretation.CSharp;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Interpretation;

/// <summary>
/// The node corpus the producer table is checked against.
/// <para>
/// Evidence first: every node reachable from C# text this suite already carries
/// is read with <see cref="CSharpLanguage.Read"/> and collected — the emitted goldens
/// and the program literals in the <c>Poly.Tests/Interpretation</c> and
/// <c>Poly.Tests/Integration</c> sources. <see cref="Cases"/> keeps one representative
/// per kind, because a producer claim is made per kind.
/// </para>
/// <para>
/// The parsed representative is contextual: a node lifted out of a golden references
/// locals and types declared around it, so compiling it alone rejects for reasons that
/// have nothing to do with its kind. Kinds whose claim is proved by compiling therefore
/// use a self-contained <see cref="DeclaredSamples"/> program instead — the shape the
/// suite already constructs for that kind.
/// </para>
/// </summary>
internal static class NodeFactCorpus {
    /// <summary>
    /// One case per kind: the node of that kind, the program a producer is exercised with
    /// (the node itself for a parsed case, the sample program containing it otherwise),
    /// and where the case came from.
    /// </summary>
    public sealed record CorpusCase(Type Kind, string Source, Node KindNode, Node Root);

    /// <summary>
    /// Statement-level C# the suite does not otherwise carry, each naming the kind it
    /// evidences. A claim with no corpus case is a missing case before it is a reason for
    /// a sample node, and a writer claim needs text the writer itself is exercised on.
    /// </summary>
    private static readonly (string Kind, string Text)[] AddedTexts = [
        ("BitwiseNot", "var y = ~1L;"),
        ("ShiftRight", "var x = 8L >> 2;"),
        ("DoWhileLoop", "do { var y = 1L; } while (false);"),
        ("BreakStatement", "while (true) { break; }"),
        ("ContinueStatement", "while (true) { continue; }"),
        ("GotoStatement", "var x = 0L; goto done; done: x = 1L;"),
        ("TryCatchFinally", "try { var y = 1L; } catch { var z = 2L; }"),
        ("UsingStatement", "using var d = new System.IO.MemoryStream();"),
        ("SwitchStatement", "switch (1L) { case 1L: break; default: break; }"),
        ("TypeAs", "var o = \"a\" as object;"),
        ("MapTypeReference", "class M { public Dictionary<string, int> D { get; set; } }"),
        ("Divide", "var y = 8L / 2;"),
        ("Modulo", "var y = 8L % 3;"),
        ("UnaryMinus", "var y = -1L;"),
        ("InterpolatedString", "var y = $\"a{1 + 2}\";"),
        ("BitwiseAnd", "var y = 6L & 3L;"),
        ("BitwiseOr", "var y = 6L | 3L;"),
        ("BitwiseXor", "var y = 6L ^ 3L;"),
        ("ShiftLeft", "var y = 1L << 3;"),
        ("ThrowExpression", "var e = new System.InvalidOperationException(); var y = true ? 1L : throw e;"),
        ("ForEachLoop", "var a = new long[2]; foreach (var v in a) { var w = v; }"),
        ("IndexAccess", "var a = new long[2]; var y = a[0];"),
        ("And", "true && false;"),
        ("Or", "true || false;"),
        ("Not", "!true;"),
        ("Equal", "1L == 2L;"),
        ("NotEqual", "1L != 2L;"),
        ("LessThanOrEqual", "1L <= 2L;"),
        ("GreaterThan", "1L > 2L;"),
        ("GreaterThanOrEqual", "1L >= 2L;"),
        ("Conditional", "true ? 1L : 2L;"),
        ("Lambda", "{ var f = (long v) => v; }"),
        ("Parameter", "{ var f = (long v) => v; }"),
    ];

    private static readonly (IReadOnlyList<(string Source, CompilationUnitNode Unit)> Units,
        IReadOnlyList<CorpusCase> Cases, IReadOnlySet<Type> ParsedKinds, IReadOnlySet<Type> CompilingProgramKinds) Loaded = Load();

    /// <summary>Every C# program text the suite carries, parsed as a unit.</summary>
    public static IReadOnlyList<(string Source, CompilationUnitNode Unit)> Units => Loaded.Units;

    public static IReadOnlyList<CorpusCase> Cases => Loaded.Cases;

    /// <summary>The kinds the reader produced from that text, before any declared sample.</summary>
    public static IReadOnlySet<Type> ParsedKinds => Loaded.ParsedKinds;

    /// <summary>The kinds a program the VM accepts contains — the VM's corpus evidence.</summary>
    public static IReadOnlySet<Type> CompilingProgramKinds => Loaded.CompilingProgramKinds;

    /// <summary>A writer subject is text the writer produced (a golden) or a corpus case this slice adds.</summary>
    public static bool IsWriterSubject(string source) =>
        source.EndsWith(".cs.golden", StringComparison.Ordinal)
        || source.StartsWith("added corpus case", StringComparison.Ordinal);

    public static IEnumerable<Node> Walk(Node node) {
        yield return node;
        foreach (var child in node.Children) {
            if (child is null)
                continue;
            foreach (var descendant in Walk(child))
                yield return descendant;
        }
    }

    public static IReadOnlySet<Type> KindsOf(Node node) =>
        new HashSet<Type>(Walk(node).Select(n => n.GetType()));

    private static (IReadOnlyList<(string Source, CompilationUnitNode Unit)> Units,
        IReadOnlyList<CorpusCase> Cases, IReadOnlySet<Type> ParsedKinds, IReadOnlySet<Type> CompilingProgramKinds) Load() {
        var units = new List<(string Source, CompilationUnitNode Unit)>();
        var cases = new Dictionary<Type, CorpusCase>();
        var parsed = new HashSet<Type>();
        foreach (var (source, text) in Texts()) {
            CompilationUnitNode unit;
            try {
                unit = CSharpLanguage.Read(text);
            }
            catch (Exception) {
                continue;
            }
            units.Add((source, unit));
            foreach (var node in Walk(unit)) {
                parsed.Add(node.GetType());
                cases.TryAdd(node.GetType(), new CorpusCase(node.GetType(), source, node, node));
            }
        }
        foreach (var (kind, source, program) in DeclaredSamples()) {
            var node = Walk(program).FirstOrDefault(n => n.GetType() == kind)
                ?? throw new InvalidOperationException($"Sample '{source}' does not contain {kind.Name}.");
            cases[kind] = new CorpusCase(kind, source, node, program);
        }
        return (units, [.. cases.Values.OrderBy(c => c.Kind.Name, StringComparer.Ordinal)], parsed,
            CompilingPrograms(units, cases.Values));
    }

    /// <summary>
    /// The programs the VM accepts: each parsed text as a program block, plus each declared
    /// sample program. A kind missing from their kinds has no compiling evidence.
    /// </summary>
    private static IReadOnlySet<Type> CompilingPrograms(
        IReadOnlyList<(string Source, CompilationUnitNode Unit)> units,
        IEnumerable<CorpusCase> cases) {
        var kinds = new HashSet<Type>();
        foreach (var (_, unit) in units) {
            try {
                var program = CSharpScript.ProgramBlock(unit);
                Interpreter.Compile(program);
                foreach (var kind in KindsOf(program))
                    kinds.Add(kind);
            }
            catch (Exception) {
            }
        }
        foreach (var corpusCase in cases.Where(c => c.Source.StartsWith("sample:", StringComparison.Ordinal))) {
            try {
                Interpreter.Compile(corpusCase.Root);
                foreach (var kind in KindsOf(corpusCase.Root))
                    kinds.Add(kind);
            }
            catch (Exception) {
            }
        }
        return kinds;
    }

    private static IEnumerable<(string Source, string Text)> Texts() {
        var root = FindRepoRoot();
        foreach (var (kind, text) in AddedTexts)
            yield return ($"added corpus case for {kind}: {text}", text);
        var goldenRoot = Path.Combine(root, "Poly.Tests", "DomainModeling", "Compile", "EmitGolden");
        foreach (var path in Files(goldenRoot, "*.cs.golden"))
            yield return (Relative(root, path), File.ReadAllText(path));

        foreach (var folder in new[] { "Poly.Tests/Interpretation", "Poly.Tests/Integration" }) {
            foreach (var path in Files(Path.Combine(root, folder), "*.cs")) {
                foreach (var literal in ProgramLiterals(File.ReadAllText(path)))
                    yield return ($"{Relative(root, path)}: {OneLine(literal)}", literal);
            }
        }
    }

    /// <summary>The string literals in a test source that can be a C# fragment at all.</summary>
    private static IEnumerable<string> ProgramLiterals(string source) =>
        CSharpSyntaxTree.ParseText(source)
            .GetRoot()
            .DescendantNodes()
            .OfType<LiteralExpressionSyntax>()
            .Where(literal => literal.Kind() == SyntaxKind.StringLiteralExpression)
            .Select(literal => literal.Token.ValueText)
            .Where(text => text.Length > 0 && text.Length < 20_000)
            .Where(text => text.Contains(';') || text.Contains('}'));

    private static IEnumerable<string> Files(string directory, string pattern) =>
        Directory.Exists(directory)
            ? Directory.GetFiles(directory, pattern, SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal)
            : [];

    private static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    private static string OneLine(string text) {
        var line = text.ReplaceLineEndings(" ").Trim();
        return line.Length <= 60 ? line : line[..60] + "…";
    }

    private static string FindRepoRoot() {
        var dir = AppContext.BaseDirectory;
        while (dir is not null) {
            if (File.Exists(Path.Combine(dir, "docs/CORE.md")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("Could not find repo root from " + AppContext.BaseDirectory);
    }

    /// <summary>
    /// Self-contained programs for the kinds no corpus text can carry: the VM-only statements
    /// and markers, and the kinds whose parsed representative is too contextual to compile.
    /// This is the plan's fallback for a corpus sweep that cannot prove a Vm claim — the parsed
    /// node reaches analysis with its surrounding locals missing, so it rejects for a reason
    /// that has nothing to do with its kind. Each program is the shape an existing test already
    /// constructs for that kind.
    /// </summary>
    private static IEnumerable<(Type Kind, string Source, Node Program)> DeclaredSamples() {
        var x = new Variable("x");
        var bits = new Variable("bits");

        yield return Sample<Await>("LanguageVmTests.Await_CompletedTask_ReturnsResult",
            new Await(new Invoke(new Member(new Constant(new Awaitable()), nameof(Awaitable.Completed)))));
        yield return Sample<BitwiseAnd>("LanguageVmTests.BitwiseAnd_Mask",
            new BitwiseAnd(new Constant(6L), new Constant(3L)));
        yield return Sample<BitwiseNot>("LanguageVmTests", new BitwiseNot(new Constant(1L)));
        yield return Sample<BitwiseOr>("LanguageVmTests", new BitwiseOr(new Constant(6L), new Constant(3L)));
        yield return Sample<BitwiseXor>("LanguageVmTests", new BitwiseXor(new Constant(6L), new Constant(3L)));
        yield return Sample<Coalesce>("LanguageVmTests.Coalesce_EmptyString_KeepsLeft",
            new Coalesce(new Constant("a"), new Constant("b")));
        yield return Sample<Comment>("LanguageVmTests.Comment_AsProgram_IsVoid", new Comment("note"));
        yield return Sample<Default>("LanguageVmTests.Default_Untyped_IsZero", new Default());
        yield return Sample<ForEachLoop>("ForEachLoopTests.ForEachLoop_UsesLoopVariableInsideBody", new Block([
            new Assignment(x, new Constant(0L)),
            new ForEachLoop(
                new Variable("item"),
                new Constant(new[] { 1, 2, 3 }),
                new Assignment(x, new Add(x, new Variable("item")))),
        ], [x]));
        yield return Sample<GotoStatement>("LanguageVmTests.Goto_AndLabel_SkipAssignment", new Block([
            new GotoStatement("exit"),
            new LabelDeclaration("exit", x),
        ], [x]));
        yield return Sample<IfStatement>("LanguageVmTests.IfStatement_ThenBranch_Runs",
            new IfStatement(new Constant(true), new Constant(1L)));
        yield return Sample<IndexAccess>("LanguageVmTests.IndexAccess_Array_ReturnsElement",
            new IndexAccess(new NewArray(TypeReference.To<long>(), new Constant(2L)), new Constant(0L)));
        yield return Sample<LabelDeclaration>("LanguageVmTests.Goto_AndLabel_SkipAssignment", new Block([
            new Assignment(x, new Constant(10L)),
            new GotoStatement("exit"),
            new LabelDeclaration("exit", x),
        ], [x]));
        yield return Sample<NullForgiving>("LanguageVmTests", new NullForgiving(new Constant("x")));
        yield return Sample<PopCount>("LanguageVmTests", new PopCount(new Constant(7L)));
        yield return Sample<ShiftLeft>("LanguageVmTests", new ShiftLeft(new Constant(1L), new Constant(3L)));
        yield return Sample<ShiftRight>("LanguageVmTests", new ShiftRight(new Constant(8L), new Constant(2L)));
        yield return Sample<StridedSetBits>("LanguageVmTests.StridedSetBits_SetsWordBits", new Block([
            new Assignment(bits, new NewArray(TypeReference.To<long>(), new Constant(2L))),
            new StridedSetBits(bits, new Constant(4L), new Constant(2L), new Constant(8L)),
        ], [bits]));
        yield return Sample<SuspendNode>("LanguageVmTests", new SuspendNode(new Constant(7L), "bp"));
        yield return Sample<SwitchStatement>("CSharpLanguageTests.PlainSwitch", new Block([
            new Assignment(x, new Constant(1L)),
            new SwitchStatement(x, [
                new SwitchCase(new Constant(1L), new Constant(2L)),
            ], new Constant(3L)),
        ], [x]));
        yield return Sample<ThrowExpression>("LanguageVmTests.ThrowExpression_ThrowsOperand",
            new ThrowExpression(new New(TypeReference.To<InvalidOperationException>(), new Constant("vm"))));
        yield return Sample<TryCatchFinally>("LanguageVmTests", new TryCatchFinally(new Constant(1L)));
        yield return Sample<TypeAs>("LanguageVmTests.TypeAs_MatchingReference_KeepsObject",
            new TypeAs(new Constant("hello"), TypeReference.To<object>()));
        yield return Sample<TypeCast>("LanguageVmTests.TypeCast_IntToDouble_BitcastResult",
            new TypeCast(new Constant(1L), TypeReference.To<long>()));
        yield return Sample<TypeIs>("LanguageVmTests", new TypeIs(new Constant("x"), TypeReference.To<string>()));
        yield return Sample<TypeOf>("LanguageVmTests.TypeOf_String_ReturnsRuntimeType",
            new TypeOf(TypeReference.To<string>()));
        yield return Sample<UsingStatement>("UsingStatementVmTests.Using_NonIDisposable_SkipsDispose_Completes",
            new UsingStatement(new Constant("not-disposable"), new Constant(42L)));

        yield return Sample<AttributedNode>("LanguageSurfaceTests.AnalysisOnlyKinds_AreNotScriptEntry",
            new AttributedNode(new Constant(1L), [new AttributeNode("Key", [])]));
        yield return Sample<ClrTypeReference>("LanguageSurfaceTests.CompileRejectKinds_FailLoud",
            new ClrTypeReference(typeof(string)));
        yield return Sample<ParameterReference>("LanguageSurfaceTests.CompileRejectKinds_FailLoud",
            new ParameterReference());
        yield return Sample<ResolvedTypeReference>(
            "CSharpWriterFailClosedTests.KindsWithoutWriterArm_Throw_InsteadOfPrintingTheirToString",
            new ResolvedTypeReference(ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(string))));
        yield return Sample<TypeDefinitionReference>("LanguageSurfaceTests.CompileRejectKinds_FailLoud",
            new TypeDefinitionReference(ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(string))));
        yield return Sample<UnionTypeReference>(
            "CSharpWriterFailClosedTests.KindsWithoutWriterArm_Throw_InsteadOfPrintingTheirToString",
            new UnionTypeReference([TypeReference.To<long>()]));
    }

    private static (Type Kind, string Source, Node Program) Sample<T>(string source, Node program) =>
        (typeof(T), "sample: " + source, program);

    private sealed class Awaitable {
        public Task<long> Completed() => Task.FromResult(42L);
    }
}