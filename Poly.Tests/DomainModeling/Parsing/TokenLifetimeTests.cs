using System.Reflection;
using System.Runtime.CompilerServices;

using Poly.Compile;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.Grammar;
using Poly.Packs.Product;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// The lifetime rule for span tokens: a <see cref="DslToken"/>, its <see cref="SourceText"/>, the
/// per-document <see cref="NameTable"/>, a reader or a match result is held only by parse-scope
/// objects (the syntax tree, the resolver and its pending records, the cursor, the expression parser,
/// the outline builder). Everything a parse hands out (the change list, the compiled domain and its
/// analysis, a caught grammar error, the outline, an edit outcome) holds names as plain strings.
/// Checked on the instances, not the types: a field typed by an open generic or an interface says
/// nothing about what it holds.
/// </summary>
public sealed class TokenLifetimeTests {
    private const string Document = """
        domain Shop
        uses temporal
        Status: enum { Open iri("http://e.org/open") term("open"), Closed }
        Money: value { Amount: Decimal range(0.01, 1000) Currency: Text length(3) }
        function Twice(a: Number): Number = a * 2
        Pay: contract external "https://pay.example/\"v\"" "2024-11-20" {
          Req: value { Amount: Number }
          Charge: outbound operation Req
        }
        public entity Order key(Code) {
          Code: Text pattern("^[A-Z]{3}$") label("code")
          Total: Number range(-5, 100) default(1)
          Due: Date
          State: Status
          Lines: many Line via order
          Late: policy { Today - 3 Days > Due and count Lines > 1 }
          Draft: stage {
            entry { assign Total to 0 }
            when any Lines Done { transition to Closed }
            Submit: action (request: Req) { transition to Closed }
          }
          Closed: stage { }
        }
        Line: entity {
          order: Order
          Qty: Number
          Done: stage { }
        }
        Bill: bind Pay Charge to Submit request
        """;

    private static readonly Type[] HeldOnlyByParseScope = [
        typeof(DslToken), typeof(SourceText), typeof(NameTable), typeof(DslTokenReader), typeof(DslSyntaxTree),
    ];

    private static bool IsParseScope(Type type) =>
        HeldOnlyByParseScope.Contains(type)
        || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(MatchResult<,>));

    private static bool IsLeaf(Type type) =>
        type.IsPrimitive || type.IsEnum || type.IsPointer || type == typeof(string) || type == typeof(decimal)
        || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan) || type == typeof(Guid)
        || typeof(MemberInfo).IsAssignableFrom(type) || typeof(Assembly).IsAssignableFrom(type) || typeof(Module).IsAssignableFrom(type)
        || type == typeof(IntPtr) || type == typeof(UIntPtr) || type.FullName == "System.RuntimeType";

    /// <summary>Every object reachable from <paramref name="roots"/> through instance fields; returns the count and the paths to parse-scope objects.</summary>
    private static (int Visited, List<string> Found) Walk(params (string Name, object? Root)[] roots) {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var nodes = new List<(int Parent, string Edge)>();
        var stack = new Stack<(object Value, int Node)>();
        var found = new List<string>();
        foreach (var (name, root) in roots) {
            if (root is null)
                continue;
            nodes.Add((-1, name));
            stack.Push((root, nodes.Count - 1));
        }

        while (stack.Count > 0) {
            var (value, node) = stack.Pop();
            var type = value.GetType();
            if (IsLeaf(type) || !visited.Add(value))
                continue;
            if (visited.Count > 5_000_000)
                throw new InvalidOperationException("The object graph is larger than 5,000,000 objects.");
            if (IsParseScope(type)) {
                found.Add(Path(nodes, node) + " : " + type.Name);
                continue;
            }

            if (value is Array array) {
                if (IsLeaf(type.GetElementType()!))
                    continue;
                var i = 0;
                foreach (var item in array) {
                    if (item is not null) {
                        nodes.Add((node, $"[{i}]"));
                        stack.Push((item, nodes.Count - 1));
                    }
                    i++;
                }
                continue;
            }

            for (var t = type; t is not null; t = t.BaseType) {
                foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)) {
                    if (field.FieldType.IsPointer || IsLeaf(field.FieldType))
                        continue;
                    if (field.GetValue(value) is { } child) {
                        nodes.Add((node, "." + field.Name));
                        stack.Push((child, nodes.Count - 1));
                    }
                }
            }
        }

        return (visited.Count, found);
    }

    private static string Path(List<(int Parent, string Edge)> nodes, int node) {
        var parts = new List<string>();
        for (var n = node; n >= 0; n = nodes[n].Parent)
            parts.Add(nodes[n].Edge);
        parts.Reverse();
        return string.Concat(parts);
    }

    private static DomainSession SessionFor(string text) =>
        DomainSession.ForSource(text, ExtensionCatalog.ProductAuthoring, ProductCatalog.Catalog);

    [Test]
    public async Task Walk_FindsTheTokensASyntaxTreeHolds() {
        var tree = DslSyntaxTree.Parse(Document);
        var resolver = new DomainResolver(tree, SessionFor(Document));

        var (_, fromTree) = Walk(("tree", tree.Tokens));
        var (_, fromResolver) = Walk(("resolver", resolver));

        await Assert.That(fromTree.Count).IsGreaterThan(100);
        await Assert.That(fromTree.All(p => p.EndsWith(" : DslToken", StringComparison.Ordinal))).IsTrue();
        await Assert.That(fromResolver).IsNotEmpty();
    }

    [Test]
    public async Task NoParseResult_HoldsATokenSourceNameTableReaderOrMatch() {
        var tree = DslSyntaxTree.Parse(Document);
        var changes = new DomainResolver(tree, SessionFor(Document)).Resolve();
        var compiled = new PolyCompiler(ProductCatalog.Catalog, new SessionOptions(ProductCatalog.Catalog, ExtensionCatalog.ProductAuthoring))
            .Analyze(Document, "lifetime.poly");
        var outline = DslOutline.Build(Document);
        var edit = PolyEdit.Apply(Document, """[{"op":"add","parent":"Order","dsl":"Note: Text optional"}]""");
        const string broken = "domain A\nX: entity {\n  N: Text length(\"3\")\n}\n";
        Exception? error = null;
        try {
            new DomainResolver(DslSyntaxTree.Parse(broken), SessionFor(broken)).Resolve();
        }
        catch (GrammarException ex) {
            error = ex;
        }

        var (changesVisited, inChanges) = Walk(("changes", changes));
        var (compiledVisited, inCompiled) = Walk(("domain", compiled.Domain), ("analysis", compiled.Analysis), ("result", compiled));
        var (outlineVisited, inOutline) = Walk(("outline", outline), ("roots", outline.Roots));
        var (editVisited, inEdit) = Walk(("edit", edit));
        var (errorVisited, inError) = Walk(("error", error));

        await Assert.That(compiled.Diagnostics.Where(d => d.IsError).Select(d => $"{d.Code} {d.Line}:{d.Column} {d.Message}")).IsEmpty();
        await Assert.That(compiled.Domain).IsNotNull();
        await Assert.That(error).IsNotNull();
        await Assert.That(inChanges).IsEmpty();
        await Assert.That(inCompiled).IsEmpty();
        await Assert.That(inOutline).IsEmpty();
        await Assert.That(inEdit).IsEmpty();
        await Assert.That(inError).IsEmpty();
        // The walks are not vacuous: each reached the objects it was given.
        await Assert.That(changesVisited).IsGreaterThan(150);
        await Assert.That(compiledVisited).IsGreaterThan(10_000);
        await Assert.That(outlineVisited).IsGreaterThan(10);
        await Assert.That(editVisited).IsGreaterThan(2);
        await Assert.That(errorVisited).IsGreaterThan(1);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference<SourceText> Source, List<DomainChange> Changes, DslSyntaxTree? Kept) ResolveOnce(
        DomainSession session, bool keepTree) {
        var tree = DslSyntaxTree.Parse(Document);
        var source = new WeakReference<SourceText>(tree.Tokens[0].Source);
        var changes = new DomainResolver(tree, session).Resolve();
        return (source, changes, keepTree ? tree : null);
    }

    private static void CollectFully() {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [Test]
    public async Task Resolve_DoesNotPinTheDocumentsSourceText() {
        var session = SessionFor(Document);
        var (dropped, changes, _) = ResolveOnce(session, keepTree: false);
        var (kept, keptChanges, tree) = ResolveOnce(session, keepTree: true);
        CollectFully();

        await Assert.That(dropped.TryGetTarget(out _)).IsFalse();
        await Assert.That(kept.TryGetTarget(out _)).IsTrue();
        await Assert.That(changes.Count).IsEqualTo(keptChanges.Count);
        GC.KeepAlive(tree);
    }

    [Test]
    public async Task Resolve_ConcurrentOnSeparateTreesAndSessions_MatchesSequential() {
        var sequential = ResolverBaselineTests.Changes(Document);
        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => ResolverBaselineTests.Changes(Document))));

        await Assert.That(sequential.Count).IsGreaterThan(30);
        foreach (var result in results)
            await Assert.That(string.Join("\n", result)).IsEqualTo(string.Join("\n", sequential));
    }
}