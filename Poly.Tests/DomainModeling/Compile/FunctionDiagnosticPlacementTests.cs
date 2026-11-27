using Poly.Compile;
using Poly.Packs.Product;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// #796: a diagnostic on a declared function is placed on the function's own declaration line, not at 1:1.
/// The <c>DslOutline</c> records every top-level declaration, <c>function</c> included, so the element path
/// of a function reaches a source span like an entity's does.
/// </summary>
public class FunctionDiagnosticPlacementTests {
    private const string UnknownReturnType = """
        domain P
        function F(): Money = 1
        """;

    private const string ImpureBody = """
        domain P
        function F(): Number = Qty
        """;

    private static CompileResult Compile(string text) =>
        new PolyCompiler(ProductCatalog.Catalog, new SessionOptions(ProductCatalog.Catalog, ProductCatalog.ImplicitIds))
            .Compile(text, "functions.poly");

    [Test]
    public async Task UnknownReturnType_IsPlacedOnTheFunctionDeclaration() {
        var error = Compile(UnknownReturnType).Diagnostics.Single(d => d.IsError);

        await Assert.That(error.Code).IsEqualTo("DMSEM006");
        await Assert.That(error.Placed).IsTrue();
        await Assert.That(error.Line).IsEqualTo(2);
        await Assert.That(error.Column).IsEqualTo(1);
        await Assert.That(error.Message).Contains("'Money'");
    }

    [Test]
    public async Task NegatedParameter_IsAPureFunctionBody() {
        var result = Compile("""
            domain P
            function Neg(x: Number): Number = -x
            """);

        await Assert.That(result.Diagnostics.Where(d => d.IsError).ToList()).IsEmpty()
            .Because("a negated parameter is arithmetic, which a pure function body may use");
    }

    [Test]
    public async Task ImpureBody_IsPlacedOnTheFunctionDeclaration() {
        var error = Compile(ImpureBody).Diagnostics.Single(d => d.Code == "DMFUN005");

        await Assert.That(error.Placed).IsTrue();
        await Assert.That(error.Line).IsEqualTo(2);
        await Assert.That(error.Column).IsEqualTo(1);
    }
}