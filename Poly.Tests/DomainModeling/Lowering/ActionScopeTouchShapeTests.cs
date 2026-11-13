using Poly.Ast.Nodes;
using Poly.DomainModeling;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Libraries.Http;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation.CSharp;
using Poly.Packs.Sqlite;
using Poly.Runtime;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// L-03c (#683): where the module touches an entity. <c>ActionScope.Touch(this)</c> sits at the entry of each mutator
/// (the action wrapper, a subscription handler, <c>Attach*</c>, <c>Register*Subscriber</c>), the factory records what it
/// created with <c>ActionScope.Create</c>, and the notify cascade restores its entry depth on the way out. A closed-set
/// tripwire holds every mutator the exporter emits to that rule, so a new one without a Touch fails here.
/// </summary>
public class ActionScopeTouchShapeTests {
    private const string Dsl = """
        domain T
        Shelf: entity {
          Label: Text
          books: many Book
          when books Out { assign Label to "out" }
        }
        Book: entity {
          Code: Text
          shelf: Shelf
          In: stage { Lend: action { transition to Out } }
          Out: stage { entry { transition to Back } }
          Back: stage { }
        }
        Owner: entity {
          Label: Text
          Count: Number default(0)
          Busy: stage {
            Bump: action { assign Count to Count + 1 }
          }
          Idle: stage { }
        }
        """;

    private static string Print(string dsl) {
        var session = ParitySessions.Create(ExtensionCatalog.ProductAuthoring);
        var changes = new PolyDslParser(dsl, session).Parse();
        var evolved = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(changes, session: session);
        if (!evolved.Succeeded)
            throw new InvalidOperationException(string.Join("; ", evolved.Analysis.Diagnostics
                .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                .Select(d => d.Message)));
        return new CSharpGenerator().Generate(session.Lower(evolved.Root!, evolved.Analysis));
    }

    /// <summary>One method's text by a signature fragment, without its body partner.</summary>
    private static string Method(string cs, string signature) {
        var start = cs.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0)
            throw new InvalidOperationException($"Method '{signature}' not found.");
        var depth = 0;
        for (var i = cs.IndexOf('{', start); i < cs.Length; i++) {
            if (cs[i] == '{')
                depth++;
            else if (cs[i] == '}' && --depth == 0)
                return cs[start..(i + 1)];
        }
        throw new InvalidOperationException($"Unbalanced braces for '{signature}'.");
    }

    private static int At(string text, string needle) {
        var at = text.IndexOf(needle, StringComparison.Ordinal);
        if (at < 0)
            throw new InvalidOperationException($"'{needle}' not found in:\n{text}");
        return at;
    }

    private const string Touch = "Poly.Runtime.ActionScope.Touch(this);";

    [Test]
    public async Task Wrapper_TouchesThisFirstInsideTheTry_AndTheBodyDoesNot() {
        var cs = Print(Dsl);
        var wrapper = Method(cs, "DomainResult Bump(");
        var body = Method(cs, "DomainResult __BodyOfBump(");

        await Assert.That(wrapper).Contains(Touch);
        await Assert.That(At(wrapper, "try")).IsLessThan(At(wrapper, Touch));
        await Assert.That(At(wrapper, Touch)).IsLessThan(At(wrapper, "this.__BodyOfBump("));
        // The absence is paired with the positive above: the body carries none of its own.
        await Assert.That(body).Contains("this.Count");
        await Assert.That(body).DoesNotContain("ActionScope.Touch");
    }

    // Amendment 9: gates, then Touch, then the automatic-chain clear, then the effects.
    [Test]
    public async Task Handler_TouchesAfterTheGatesAndBeforeTheChainClearAndTheEffects() {
        var cs = Print("""
            domain H
            Shelf: entity {
              Label: Text
              books: many Book
              Open: stage {
                when books Out { assign Label to "out" transition to Shut }
              }
              Shut: stage { entry { transition to Open } }
            }
            Book: entity {
              Code: Text
              In: stage { Lend: action { transition to Out } }
              Out: stage { }
            }
            """);
        var handler = Method(cs, "DomainResult WhenEachBookOut(");

        await Assert.That(At(handler, "!= ShelfStage.Open")).IsLessThan(At(handler, Touch));
        await Assert.That(At(handler, Touch)).IsLessThan(At(handler, "this.ClearAutomaticStageChain()"));
        await Assert.That(At(handler, "this.ClearAutomaticStageChain()")).IsLessThan(At(handler, "this.Label = "));
    }

    // The handler's Touch is in the body the simulator runs, not only in the printed text.
    [Test]
    public async Task Handler_TouchIsInTheModuleBodyTheSimulatorRuns() {
        var (domain, analysis) = EvolvedDomain.FromDsl(Dsl);
        var shelf = DomainProgramProjection.ToSyntax(domain, analysis).Single(t => t.Name == "Shelf");
        var handler = shelf.Methods!.Single(m => m.Name.StartsWith("WhenEach", StringComparison.Ordinal));

        await Assert.That(new CSharpGenerator().Generate(handler.Body!)).Contains("ActionScope.Touch(this)");
    }

    [Test]
    public async Task AttachAndRegister_TouchThisBeforeTheirWrites() {
        var cs = Print(Dsl);
        var attach = Method(cs, "void AttachBooks(");
        var register = Method(cs, "Subscriber(Shelf subscriber)");

        await Assert.That(At(attach, Touch)).IsLessThan(At(attach, "this._books.Add("));
        await Assert.That(At(register, Touch)).IsLessThan(At(register, "this._shelfBooksOutSubscribers = new"));
    }

    // F3: the factory records the instance after `new` and before any peer attach.
    [Test]
    public async Task Factory_RecordsTheCreatedInstanceAfterNewAndBeforeAttach() {
        var cs = Print(Dsl);
        var create = Method(cs, "static DomainResult<Book> Create(");

        await Assert.That(create).Contains("Poly.Runtime.ActionScope.Create(created, \"Book\");");
        await Assert.That(At(create, "new Book(")).IsLessThan(At(create, "ActionScope.Create(created"));
        await Assert.That(At(create, "ActionScope.Create(created")).IsLessThan(At(create, ".AttachBooks(created)"));
    }

    // F2: the cascade returns its entry depth and the exit assigns it back, so a rollback inside the cascade (which
    // restores the depth the action started with) cannot be decremented below it by the unwinding finally.
    [Test]
    public async Task NotifyCascade_ExitAssignsTheEntryDepth() {
        var cs = Print(Dsl);
        var notify = Method(cs, "DomainResult NotifyOutSubscribers(");

        await Assert.That(cs).Contains("private int EnterNotifyCascade(string stageName)");
        await Assert.That(cs).Contains("private void ExitNotifyCascade(int depth)");
        await Assert.That(Method(cs, "void ExitNotifyCascade(")).Contains("this._notifyDepth = depth;");
        await Assert.That(notify).Contains("var __depth = this.EnterNotifyCascade(\"Out\");");
        await Assert.That(notify).Contains("this.ExitNotifyCascade(__depth);");
    }

    // ── the closed-set tripwire (amendment 2) ──────────────────────────

    static readonly ExtensionCatalog ProbeCatalog = ExtensionCatalog.Core.With(new SqliteLibrary()).With(new HttpLibrary());
    static readonly string[] ProbeSeed = [.. ExtensionCatalog.ProductAuthoring, "sqlite"];

    /// <summary>
    /// Methods that write <c>this</c> with no Touch of their own, each with its reason. Anything else that writes
    /// <c>this</c> must Touch first, or be a <c>__BodyOf</c> body whose wrapper does.
    /// </summary>
    internal static readonly IReadOnlyList<(Func<string, bool> Matches, string Reason)> AllowList = [
        (name => name == "__Restore", "the scope's own restore"),
        (name => name is "EnterNotifyCascade" or "ExitNotifyCascade",
            "the cascade depth; the exit assigns the entry depth back (F2), so a rollback never needs it"),
        (name => name is "NoteAutomaticStage" or "ClearAutomaticStageChain",
            "the automatic-stage chain; every trigger clears it first, so it is not observable after an action (G8)"),
        (name => name.StartsWith("Create", StringComparison.Ordinal),
            "Create{Nav}: lowered create-in calls it from an action body or a handler, which touched `this` first"),
    ];

    internal static IEnumerable<TypeDefinitionNode> Lower(string poly) {
        var session = DomainSession.ForSource(poly, ProbeSeed, ProbeCatalog);
        var changes = DomainCompilation.WithSeed(new PolyDslParser(poly, session).Parse(), ProbeSeed);
        var outcome = new DomainEvolution(new Domain("_", [])).ApplyAndAnalyze(changes, session: session);
        if (!outcome.Succeeded)
            throw new InvalidOperationException(string.Join("; ", outcome.Analysis.Diagnostics
                .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                .Select(d => d.Message)));
        _ = session.Lower(outcome.Root!, outcome.Analysis);
        return session.ArtifactCatalog.Artifacts
            .Where(a => a.Descriptor.Id.Type == DomainSession.EntityType && a.Payload is IEnumerable<TypeDefinitionNode>)
            .SelectMany(a => (IEnumerable<TypeDefinitionNode>)a.Payload!);
    }

    internal static IEnumerable<Node> PreOrder(Node node) {
        yield return node;
        foreach (var child in node.Children) {
            if (child is null)
                continue;
            foreach (var descendant in PreOrder(child))
                yield return descendant;
        }
    }

    private static bool IsThisField(Node? node) => node switch {
        Member { Value: ThisReference } => true,
        TypeCast { Operand: Member { Value: ThisReference } } => true,
        _ => false,
    };

    private static bool IsWriteToThis(Node node) => node switch {
        Assignment { Destination: var target } => IsThisField(target),
        Invoke { Delegate: Member { MemberName: "Add" or "Clear" or "Remove" or "Insert" or "RemoveAt", Value: var list } } =>
            IsThisField(list),
        _ => false,
    };

    private static bool IsTouchOfThis(Node node) =>
        node is Invoke { Delegate: Member { MemberName: "Touch", Value: ClrTypeReference { RuntimeType: var type } }, Arguments: [ThisReference] }
        && type == typeof(ActionScope);

    /// <summary>Every method that writes <c>this</c> before (or without) touching it, as <c>Type.Method</c>.</summary>
    internal static List<string> Violations(IEnumerable<TypeDefinitionNode> types) {
        var violations = new List<string>();
        foreach (var type in types) {
            var methods = type.Methods ?? [];
            static bool TouchesBeforeWriting(MethodDefinitionNode method, bool requireWrite) {
                var nodes = PreOrder(method.Body!).ToList();
                var write = nodes.FindIndex(IsWriteToThis);
                var touch = nodes.FindIndex(IsTouchOfThis);
                if (write < 0)
                    return !requireWrite || touch >= 0;
                return touch >= 0 && touch < write;
            }
            foreach (var method in methods) {
                if (method.Body is null || method.IsStatic)
                    continue;
                if (!PreOrder(method.Body).Any(IsWriteToThis))
                    continue;
                if (TouchesBeforeWriting(method, requireWrite: true))
                    continue;
                if (method.Name.StartsWith(DomainToCSharpExporter.ActionBodyPrefix, StringComparison.Ordinal)) {
                    var wrapperName = method.Name[DomainToCSharpExporter.ActionBodyPrefix.Length..];
                    var wrapper = methods.FirstOrDefault(m => m.Name == wrapperName && m.Body is not null);
                    if (wrapper is not null && TouchesBeforeWriting(wrapper, requireWrite: false)
                        && PreOrder(wrapper.Body!).Any(IsTouchOfThis))
                        continue;
                }
                if (AllowList.Any(entry => entry.Matches(method.Name)))
                    continue;
                violations.Add($"{type.Name}.{method.Name}");
            }
        }
        return violations;
    }

    public static IEnumerable<string> Samples() => GoldenDomains.Lowerable.Select(d => d.Path).Order(StringComparer.Ordinal);

    [Test]
    [MethodDataSource(nameof(Samples))]
    public async Task EveryMutator_TouchesThisBeforeItWrites(string relativePath) {
        var types = Lower(File.ReadAllText(Path.Combine(GoldenDomains.RepoRoot, relativePath))).ToList();

        await Assert.That(string.Join(", ", Violations(types))).IsEqualTo("");
    }

    // The tripwire is not vacuous: every mutator kind is in the probe module, and stripping one Touch is named.
    [Test]
    public async Task Tripwire_WhenOneTouchIsStripped_NamesThatMethod() {
        var types = Lower(Dsl).ToList();
        var methods = types.SelectMany(t => t.Methods ?? []).Select(m => m.Name).ToList();
        foreach (var kind in new[] { "Bump", "__BodyOfBump", "AttachBooks", "WhenEachBookOut", "__Restore", "EnterNotifyCascade" })
            await Assert.That(methods).Contains(kind);
        await Assert.That(methods.Any(m => m.StartsWith("Register", StringComparison.Ordinal))).IsTrue();
        await Assert.That(string.Join(", ", Violations(types))).IsEqualTo("");

        foreach (var (typeName, methodName) in new[] {
                     ("Shelf", "AttachBooks"), ("Shelf", "WhenEachBookOut"), ("Owner", "Bump") }) {
            var stripped = types.Select(t => t.Name != typeName ? t : t with {
                Methods = [.. t.Methods!.Select(m => m.Name != methodName ? m : m with { Body = Strip(m.Body!) })]
            }).ToList();
            var named = methodName == "Bump" ? "Owner.__BodyOfBump" : $"{typeName}.{methodName}";
            await Assert.That(Violations(stripped)).Contains(named);
        }
    }

    // ── the golden delta, predicted (amendment 6) ─────────────────────

    /// <summary>
    /// The predicted hunk classes, read off every checked-in golden and demo file: H1 a Touch first in each wrapper's
    /// try, H2 one in each handler, H3 one first in each <c>Attach*</c>, H4 one first in each <c>Register*Subscriber</c>,
    /// H5 the entry depth captured and assigned back in each notify method, H6 <c>ActionScope.Create</c> after
    /// <c>new</c> in each factory. The counts are the files' own mutators on the merged head (S-13a included): a file
    /// outside the predicted set, or a mutator without its hunk, fails here.
    /// </summary>
    [Test]
    public async Task Goldens_EveryMutatorCarriesItsPredictedHunk() {
        var files = Directory.EnumerateFiles(Path.Combine(GoldenDomains.RepoRoot, "Poly.Tests", "DomainModeling", "Compile", "EmitGolden"),
                "*.cs.golden", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(GoldenDomains.RepoRoot, "demo", "Poly.RestApi"), "*.cs"))
            .Order(StringComparer.Ordinal)
            .ToList();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal) {
            ["H1"] = 0,
            ["H2"] = 0,
            ["H3"] = 0,
            ["H4"] = 0,
            ["H5"] = 0,
            ["H6"] = 0
        };
        var problems = new List<string>();
        var (goldens, demo) = (0, 0);
        foreach (var file in files) {
            var text = File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal);
            var name = Path.GetRelativePath(GoldenDomains.RepoRoot, file);
            var before = counts.Values.Sum();
            void Each(string hunk, string signature, Func<string, bool> carries) {
                foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(text, signature)) {
                    var method = Method(text[match.Index..], match.Value);
                    if (carries(method))
                        counts[hunk]++;
                    else
                        problems.Add($"{name}: {hunk} missing in '{match.Value}'");
                }
            }
            static string FirstStatement(string method) =>
                method[(method.IndexOf('{') + 1)..].TrimStart().Split('\n')[0].Trim();

            Each("H1", @"DomainResult(<[\w.?<>]+>)? \w+\([^)]*\)\n\s*\{\n\s*var __scope = ",
                m => System.Text.RegularExpressions.Regex.IsMatch(m, @"try\n\s*\{\n\s*Poly\.Runtime\.ActionScope\.Touch\(this\);\n\s*var __result = "));
            Each("H2", @"DomainResult When(Each|Any|All)\w*\(", m => m.Contains(Touch, StringComparison.Ordinal));
            Each("H3", @"void Attach\w+\(", m => FirstStatement(m) == Touch);
            Each("H4", @"void Register\w+Subscriber\(", m => FirstStatement(m) == Touch);
            Each("H5", @"DomainResult Notify\w+Subscribers\(", m =>
                FirstStatement(m).StartsWith("var __depth = this.EnterNotifyCascade(", StringComparison.Ordinal)
                && m.Contains("this.ExitNotifyCascade(__depth);", StringComparison.Ordinal));
            Each("H6", @"static DomainResult<\w+> Create\(", m => {
                var entity = System.Text.RegularExpressions.Regex.Match(m, @"DomainResult<(\w+)>").Groups[1].Value;
                return System.Text.RegularExpressions.Regex.IsMatch(m,
                    $@"var created = new {entity}\([^\n]*\);\n\s*Poly\.Runtime\.ActionScope\.Create\(created, ""{entity}""\);");
            });
            if (counts.Values.Sum() > before || problems.Any(p => p.StartsWith(name + ":", StringComparison.Ordinal))) {
                if (file.EndsWith(".golden", StringComparison.Ordinal))
                    goldens++;
                else
                    demo++;
            }
        }

        await Assert.That(string.Join("\n", problems)).IsEqualTo("");
        await Assert.That((goldens, demo)).IsEqualTo((48, 5));
        await Assert.That(string.Join(",", counts.Select(c => $"{c.Key}={c.Value}")))
            .IsEqualTo("H1=118,H2=23,H3=29,H4=20,H5=18,H6=53");
    }

    private static Node Strip(Node body) => body switch {
        Block block => new Block([.. block.Nodes.Where(n => !IsTouchOfThis(n)).Select(Strip)], block.Variables),
        TryCatchFinally tcf => tcf with { TryBlock = Strip(tcf.TryBlock) },
        _ => body,
    };
}