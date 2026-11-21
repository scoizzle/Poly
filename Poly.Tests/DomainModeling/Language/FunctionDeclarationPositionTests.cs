using Poly.DomainModeling.Language;
using Poly.Grammar;

namespace Poly.Tests.DomainModeling.Language;

/// <summary>
/// #638: functions ship at top level (<c>function F(x: Number): Number = x</c>). Writing one in the
/// entity position (<c>F: function …</c>) must point at the supported form, not claim the feature
/// is unsupported.
/// </summary>
public sealed class FunctionDeclarationPositionTests {
    [Test]
    public async Task EntityStyleFunction_IsRefusedWithTheTopLevelForm() {
        var ex = Assert.Throws<GrammarException>(
            () => new PolyDslParser("domain T\nF: function (x: Number): Number = x").Parse());

        await Assert.That(ex!.Line).IsEqualTo(2);
        await Assert.That(ex.Column).IsEqualTo(4);
        await Assert.That(ex.Detail).Contains("function F(x: Number): Number = x");
        await Assert.That(ex.Detail).DoesNotContain("use 'entity' instead");
    }
}