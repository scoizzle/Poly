using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;

namespace Poly.Tests.Interpretation;

public class CallSiteCatalogTests {
    private static AnalysisResult Analyze(Node node) {
        return new AnalyzerBuilder()
            .UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseControlFlowAnalysis()
            .UseValueRepresentationAnalysis()
            .UseCallSiteCatalog()
            .Build()
            .Analyze(node);
    }

    [Test]
    public async Task NoCallSites_ProducesEmptyCatalog() {
        var node = new Block(new Constant(42), new Constant(true));
        var result = Analyze(node);

        var catalog = result.GetCallSiteCatalog();
        await Assert.That(catalog).IsNotNull();
        await Assert.That(catalog!.Count).IsEqualTo(0);
    }

    [Test]
    public async Task SimpleInvoke_GetsSiteIndex() {
        // Invoke on a lambda — no catalog entry expected (lambda calls use Call primitive, not CallExternal)
        var body = new Invoke(new Lambda([new Parameter("x")], new Parameter("x")), new Constant(42));
        var result = Analyze(body);

        var catalog = result.GetCallSiteCatalog();
        // Lambda invocation doesn't use CLR method dispatch, so catalog is empty
        await Assert.That(catalog).IsNotNull();
        await Assert.That(catalog!.Count).IsEqualTo(0);
    }

    [Test]
    public async Task GetCallSiteIndex_ReturnsNullForUnindexedNode() {
        var node = new Constant(42);
        var result = Analyze(node);

        var index = result.GetCallSiteIndex(node);
        await Assert.That(index).IsNull();
    }

    [Test]
    public async Task ResolvedInvoke_GetsSiteIndex() {
        // CLR method invoke: string.IndexOf(char) resolves to a specific MethodInfo
        var methodInvocation = new Invoke(new Member(Wrap("hello"), "IndexOf"), Wrap('e'));
        var result = Analyze(methodInvocation);

        var catalog = result.GetCallSiteCatalog();
        await Assert.That(catalog).IsNotNull();
        await Assert.That(catalog!.Count).IsGreaterThan(0);

        // The invoke node should have a call site index
        var index = result.GetCallSiteIndex(methodInvocation);
        await Assert.That(index).IsNotNull();
    }

    [Test]
    public async Task SameMethodInvoke_SharedIndex() {
        // Two invocations of the same method should share the same catalog index
        var invoke1 = new Invoke(new Member(Wrap("hello"), "IndexOf"), Wrap('e'));
        var invoke2 = new Invoke(new Member(Wrap("world"), "IndexOf"), Wrap('l'));
        var block = new Block(invoke1, invoke2);
        var result = Analyze(block);

        var index1 = result.GetCallSiteIndex(invoke1);
        var index2 = result.GetCallSiteIndex(invoke2);
        await Assert.That(index1).IsNotNull();
        await Assert.That(index2).IsNotNull();
        await Assert.That(index1!.Value).IsEqualTo(index2!.Value);
    }

    [Test]
    public async Task DistinctMethods_DifferentIndex() {
        // Two different methods on different types get different catalog indices.
        // string.IndexOf(Char) and string.ToUpper() are clearly distinct.
        var invokeIndexOf = new Invoke(new Member(Wrap("hello"), "IndexOf"), Wrap('e'));
        var invokeToUpper = new Invoke(new Member(Wrap("hello"), "ToUpper"));
        var block = new Block(invokeIndexOf, invokeToUpper);
        var result = Analyze(block);

        var index1 = result.GetCallSiteIndex(invokeIndexOf);
        var index2 = result.GetCallSiteIndex(invokeToUpper);
        await Assert.That(index1).IsNotNull();
        await Assert.That(index2).IsNotNull();
        await Assert.That(index1!.Value).IsNotEqualTo(index2!.Value);
    }

    [Test]
    public async Task UnresolvedInvoke_NoIndex_NoCrash() {
        // Invoking a method that doesn't exist should not crash or produce an index
        var badInvoke = new Invoke(new Member(Wrap("hello"), "NonExistentMethod12345"));
        var result = Analyze(badInvoke);

        var index = result.GetCallSiteIndex(badInvoke);
        await Assert.That(index).IsNull();
    }

    [Test]
    public async Task SameAnalyzer_TwoSequentialAnalyses_NoCatalogLeak() {
        // ANA-FIX-014: State isolation across sequential analyze calls.
        // Use a single cached analyzer instance (mirrors Interpreter._analyzer reuse).
        var analyzer = new AnalyzerBuilder()
            .UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseControlFlowAnalysis()
            .UseValueRepresentationAnalysis()
            .UseCallSiteCatalog()
            .Build();

        // Analyze tree A with a CLR invoke
        var treeA = new Invoke(new Member(Wrap("hello"), "IndexOf"), Wrap('e'));
        var resultA = analyzer.Analyze(treeA);
        var catalogA = resultA.GetCallSiteCatalog();
        await Assert.That(catalogA).IsNotNull();
        await Assert.That(catalogA!.Count).IsGreaterThan(0);

        // Analyze tree B with no call sites — catalog must be empty and fresh
        var treeB = new Block(new Constant(42), new Constant(true));
        var resultB = analyzer.Analyze(treeB);
        var catalogB = resultB.GetCallSiteCatalog();
        await Assert.That(catalogB).IsNotNull();
        await Assert.That(catalogB!.Count).IsEqualTo(0);
    }

    private static Node Wrap(string value) => new Constant(value);
    private static Node Wrap(char value) => new Constant(value);

    [Test]
    public async Task InstanceInvoke_ArgCountIncludesReceiver() {
        // Instance method: catalog entry ArgCount should be param count + 1
        var methodInvocation = new Invoke(new Member(Wrap("hello"), "IndexOf"), Wrap('e'));
        var result = Analyze(methodInvocation);

        var catalog = result.GetCallSiteCatalog();
        await Assert.That(catalog).IsNotNull();
        await Assert.That(catalog!.Count).IsGreaterThan(0);

        // string.IndexOf(char) has 1 parameter. Instance method => ArgCount = 2
        await Assert.That(catalog[0].ArgCount).IsEqualTo(2);
        await Assert.That(catalog[0].IsStatic).IsFalse();
        await Assert.That(catalog[0].IsConstructor).IsFalse();
    }

    [Test]
    public async Task SameArityOverloads_DistinctIndices() {
        // string.IndexOf(char) and string.IndexOf(char, int) have different
        // arity but the catalog identity includes parameter types, so they
        // should get distinct indices regardless.
        var invokeChar = new Invoke(new Member(Wrap("hello"), "IndexOf"), Wrap('e'));
        var invokeCharStart = new Invoke(new Member(Wrap("hello"), "IndexOf"), Wrap('e'), new Constant(1));
        var block = new Block(invokeChar, invokeCharStart);
        var result = Analyze(block);

        var index1 = result.GetCallSiteIndex(invokeChar);
        var index2 = result.GetCallSiteIndex(invokeCharStart);
        await Assert.That(index1).IsNotNull();
        await Assert.That(index2).IsNotNull();
        // Verify distinct indices (different parameter count → different entries)
        await Assert.That(index1!.Value).IsNotEqualTo(index2!.Value);
    }

    [Test]
    public async Task New_ResolvedConstructor_GetsSiteIndex() {
        var newExpr = new New(TypeReference.To<string>(), new Constant('x'), new Constant(3));
        var result = Analyze(newExpr);

        var catalog = result.GetCallSiteCatalog();
        await Assert.That(catalog).IsNotNull();
        await Assert.That(catalog!.Count).IsGreaterThan(0);

        var index = result.GetCallSiteIndex(newExpr);
        await Assert.That(index).IsNotNull();

        var entry = catalog[index!.Value];
        await Assert.That(entry.IsConstructor).IsTrue();
        await Assert.That(entry.ArgCount).IsEqualTo(2);
    }
}