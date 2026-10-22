using Poly.Interpretation;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Shadowing must bind to the innermost declaration in the VM emitter, not
/// to whichever same-named variable happens to be found first. Each use is a
/// distinct <see cref="Variable"/> node (the reader makes one per identifier),
/// so the emitter's name fallback has to be scope-aware.
/// </summary>
public class ShadowedVariableVmTests {
    private static Node ReadBody(string body) =>
        CSharpLanguage.Read($"class C {{ void M() {{ {body} }} }}")
            .Types[0].Members.OfType<MethodDefinitionNode>().Single().Body!;

    private static long Run(string body) {
        using var exec = Interpreter.Execute(Interpreter.Compile(ReadBody(body)));
        return exec.GetValue<long>();
    }

    [Test]
    public async Task InnerShadowing_UsesInnerDeclaration() {
        // The outer s is a string; the inner s is an int. n = s + 1 must read
        // the inner s. The bug read the outer s handle and returned 2.
        await Assert.That(Run("""var s = "hi"; var n = 0; { var s = 5; n = s + 1; } n;"""))
            .IsEqualTo(6L);
    }

    [Test]
    public async Task TwoLevelShadowing_EachLevelBindsItsOwnDeclaration() {
        // 0 + 100 (innermost) = 100; + 10 (middle) = 110.
        await Assert.That(Run("""var s = 1; var n = 0; { var s = 10; { var s = 100; n = n + s; } n = n + s; } n;"""))
            .IsEqualTo(110L);
    }

    [Test]
    public async Task OuterVariable_UsedAfterInnerBlock_KeepsItsValue() {
        // n = 5 + 1 = 6 inside the block; after the block n + 100 (the outer s) = 106.
        await Assert.That(Run("""var s = 100; var n = 0; { var s = 5; n = s + 1; } n = n + s; n;"""))
            .IsEqualTo(106L);
    }

    [Test]
    public async Task AssigningHeapValueToScalarLocal_FailsClosed() {
        // n's slot was established as a long by `var n = 0`; storing the string
        // heap handle there must be rejected, not silently reinterpreted.
        var body = ReadBody("""var n = 0; n = "hi"; n;""");
        await Assert.That(() => Interpreter.Compile(body))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("cannot assign 'String' to variable 'n'");
    }
}