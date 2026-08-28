using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Packs;

/// <summary>
/// Open-dispatch integration point (temporal pack): the ambient registries the pack
/// contributes to fail closed on duplicate owner, and unregistered pack IR falls to the
/// concern's fail-closed default. Core dispatch never names pack types — it routes through
/// these registries.
/// </summary>
public class TemporalDispatchRegistryTests {
    private sealed class DummyHandler : IExpressionDispatchHandler<string> {
        public Type ExpressionType => typeof(Now);
        public bool TryHandle(DomainExpression expression, Func<DomainExpression, string> route, out string result) {
            result = "handled";
            return expression is Now;
        }
    }

    private static async Task AssertDuplicateThrows(System.Action action, string fragment) {
        Exception? thrown = null;
        try {
            action();
        }
        catch (Exception ex) {
            thrown = ex;
        }
        await Assert.That(thrown).IsNotNull();
        await Assert.That(thrown!.Message).Contains(fragment);
    }

    [Test]
    public async Task Register_DuplicateOwner_Throws() {
        var registry = new ExpressionDispatchRegistry<string>();
        registry.Register(new DummyHandler());
        await AssertDuplicateThrows(() => registry.Register(new DummyHandler()),
            "Duplicate expression dispatch handler for 'Now'");
    }

    [Test]
    public async Task TryDispatch_UnknownExpression_ReturnsFalse() {
        var registry = new ExpressionDispatchRegistry<string>();
        var ok = registry.TryDispatch(DomainExpression.Property("X"), _ => "unused", out _);
        await Assert.That(ok).IsFalse();
    }

    [Test]
    public async Task TryDispatch_RegisteredExpression_Handles() {
        var registry = new ExpressionDispatchRegistry<string>();
        registry.Register(new DummyHandler());
        var ok = registry.TryDispatch(new Now(), _ => "unused", out var result);
        await Assert.That(ok).IsTrue();
        await Assert.That(result).IsEqualTo("handled");
    }

    private sealed class DummyTypeCheck : IExpressionTypeCheck {
        public Type ExpressionType => typeof(Now);
        public void Check(AnalysisContext context, DomainExpression expression, ExpressionTypeCheckScope scope) { }
    }

    private sealed class DummyDefaultResolver : IExpressionDefaultResolver {
        public Type ExpressionType => typeof(Now);
        public bool TryResolve(DomainExpression expression, string? propTypeName, out object? runtimeValue, out Node exportNode) {
            runtimeValue = null;
            exportNode = null!;
            return false;
        }
    }

    [Test]
    public async Task ExpressionTypeCheckRegistry_Duplicate_Throws() {
        var registry = new ExpressionTypeCheckRegistry();
        registry.Register(new DummyTypeCheck());
        await AssertDuplicateThrows(() => registry.Register(new DummyTypeCheck()),
            "Duplicate expression type check for 'Now'");
    }

    [Test]
    public async Task DefaultResolverRegistry_Duplicate_Throws() {
        var registry = new ExpressionDefaultResolverRegistry();
        registry.Register(new DummyDefaultResolver());
        await AssertDuplicateThrows(() => registry.Register(new DummyDefaultResolver()),
            "Duplicate expression default resolver for 'Now'");
    }
}