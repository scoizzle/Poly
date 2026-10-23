using Poly.Ast.Nodes;
using Poly.DomainModeling.Libraries.Http;
using Poly.Interpretation.CSharp;

using CompileMode = Poly.DslCompiler.CompileMode;
using Compiler = Poly.DslCompiler.DslCompiler;
using DbmsPack = Poly.DslCompiler.DbmsPack;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>Issue #281: <c>[Range]</c> must pick the attribute constructor the constraint calls
/// for instead of always binding <c>RangeAttribute(double, double)</c>.</summary>
public class RangeAttributeArgumentsTests {
    private static Compiler CompilerWithHttpHost() =>
        new Compiler().Load(new HttpLibrary());

    private static async Task<string> ProgramFor(string poly) {
        var result = CompilerWithHttpHost().Compile(poly, CompileMode.All, DbmsPack.Generic);
        await Assert.That(result.Success).IsTrue();
        return result.Files!.Single(f => f.FileName == "Program.cs").Source;
    }

    // ── The rule: one decision for every CLR numeric type ─────────────

    [Test]
    public async Task IntegralLongRange_EmitsIntegralConstructor() {
        var (minimum, maximum) = Poly.DslCompiler.RangeAttributeArguments.Build("long", 0.0, 100_000_000.0);
        await Assert.That(minimum.Value).IsTypeOf<int>();
        await Assert.That(maximum.Value).IsTypeOf<int>();
        await Assert.That((int)minimum.Value!).IsEqualTo(0);
        await Assert.That((int)maximum.Value!).IsEqualTo(100_000_000);
    }

    [Test]
    public async Task IntegralIntRange_EmitsIntegralConstructor() {
        var (minimum, maximum) = Poly.DslCompiler.RangeAttributeArguments.Build("int", 0.0, 100.0);
        await Assert.That((int)minimum.Value!).IsEqualTo(0);
        await Assert.That((int)maximum.Value!).IsEqualTo(100);
    }

    [Test]
    public async Task IntegralShortRange_EmitsIntegralConstructor() {
        var (minimum, maximum) = Poly.DslCompiler.RangeAttributeArguments.Build("short", -10.0, 20.0);
        await Assert.That(minimum.Value).IsTypeOf<int>();
        await Assert.That((int)minimum.Value!).IsEqualTo(-10);
        await Assert.That((int)maximum.Value!).IsEqualTo(20);
    }

    [Test]
    public async Task IntegralByteRange_EmitsIntegralConstructor() {
        var (minimum, maximum) = Poly.DslCompiler.RangeAttributeArguments.Build("byte", 0.0, 255.0);
        await Assert.That(minimum.Value).IsTypeOf<int>();
        await Assert.That((int)minimum.Value!).IsEqualTo(0);
        await Assert.That((int)maximum.Value!).IsEqualTo(255);
    }

    [Test]
    public async Task OpenMaximumOnByte_CapsAtByteMaxIntegrally() {
        var (minimum, maximum) = Poly.DslCompiler.RangeAttributeArguments.Build("byte", 0.0, null);
        await Assert.That(minimum.Value).IsTypeOf<int>();
        await Assert.That((int)minimum.Value!).IsEqualTo(0);
        await Assert.That((int)maximum.Value!).IsEqualTo(255);
    }

    [Test]
    public async Task OpenMaximumOnShort_CapsAtShortMaxIntegrally() {
        var (minimum, maximum) = Poly.DslCompiler.RangeAttributeArguments.Build("short", 0.0, null);
        await Assert.That((int)minimum.Value!).IsEqualTo(0);
        await Assert.That((int)maximum.Value!).IsEqualTo(short.MaxValue);
    }

    [Test]
    public async Task OpenMaximumOnInt_CapsAtIntMaxIntegrally() {
        var (minimum, maximum) = Poly.DslCompiler.RangeAttributeArguments.Build("int", 0.0, null);
        await Assert.That((int)minimum.Value!).IsEqualTo(0);
        await Assert.That((int)maximum.Value!).IsEqualTo(int.MaxValue);
    }

    [Test]
    public async Task OpenMaximumOnLong_UsesLongMaxNotDoubleMax() {
        // Deliberately the double constructor: long.MaxValue is beyond int, and RangeAttribute
        // has no (long, long). The bound is still the CLR type's real max — never double.MaxValue,
        // which would claim a reachable range the member does not have.
        var (minimum, maximum) = Poly.DslCompiler.RangeAttributeArguments.Build("long", 0.0, null);
        await Assert.That(minimum.Value).IsTypeOf<double>();
        await Assert.That((double)minimum.Value!).IsEqualTo(0.0);
        await Assert.That(maximum.Value).IsTypeOf<double>();
        await Assert.That((double)maximum.Value!).IsEqualTo((double)long.MaxValue);
        await Assert.That((double)maximum.Value!).IsNotEqualTo(double.MaxValue);
    }

    [Test]
    public async Task FractionalBounds_OnIntegralTarget_EmitsDoubleConstructor() {
        var (minimum, maximum) = Poly.DslCompiler.RangeAttributeArguments.Build("long", 0.5, 2.5);
        await Assert.That(minimum.Value).IsTypeOf<double>();
        await Assert.That((double)minimum.Value!).IsEqualTo(0.5);
        await Assert.That((double)maximum.Value!).IsEqualTo(2.5);
    }

    [Test]
    public async Task RangeBeyondInt_OnLong_EmitsDoubleConstructorDeliberately() {
        // Deliberately double: RangeAttribute has no (long, long), so an integral but beyond-int
        // bound can only be carried by the double constructor. Truncating the constraint to int
        // would falsely reject valid values, which is worse than the double's sub-2^53 precision.
        var (minimum, maximum) = Poly.DslCompiler.RangeAttributeArguments.Build("long", 0.0, 5_000_000_000.0);
        await Assert.That(minimum.Value).IsTypeOf<double>();
        await Assert.That((double)minimum.Value!).IsEqualTo(0.0);
        await Assert.That(maximum.Value).IsTypeOf<double>();
        await Assert.That((double)maximum.Value!).IsEqualTo(5_000_000_000.0);
    }

    [Test]
    public async Task DoubleTarget_StaysDoubleConstructor() {
        // A double member's range is genuinely fractional-capable: doubling down on the integral
        // constructor would narrow validation to whole numbers the member accepts.
        var (minimum, maximum) = Poly.DslCompiler.RangeAttributeArguments.Build("double", 0.0, 100.0);
        await Assert.That(minimum.Value).IsTypeOf<double>();
        await Assert.That(maximum.Value).IsTypeOf<double>();
        await Assert.That((double)maximum.Value!).IsEqualTo(100.0);
    }

    [Test]
    public async Task DecimalTarget_StaysDoubleConstructor() {
        var (minimum, maximum) = Poly.DslCompiler.RangeAttributeArguments.Build("decimal", 0.0, 100.0);
        await Assert.That(minimum.Value).IsTypeOf<double>();
        await Assert.That(maximum.Value).IsTypeOf<double>();
        await Assert.That((double)maximum.Value!).IsEqualTo(100.0);
    }

    [Test]
    public async Task FloatTarget_StaysDoubleConstructor() {
        var (minimum, maximum) = Poly.DslCompiler.RangeAttributeArguments.Build("float", 0.0, 100.0);
        await Assert.That(minimum.Value).IsTypeOf<double>();
        await Assert.That(maximum.Value).IsTypeOf<double>();
        await Assert.That((double)maximum.Value!).IsEqualTo(100.0);
    }

    [Test]
    public async Task OpenDecimalRange_CapsAtDecimalBoundsAsDouble() {
        var (minimum, maximum) = Poly.DslCompiler.RangeAttributeArguments.Build("decimal", null, null);
        await Assert.That((double)minimum.Value!).IsEqualTo((double)decimal.MinValue);
        await Assert.That((double)maximum.Value!).IsEqualTo((double)decimal.MaxValue);
    }

    // ── End to end: the emitted attribute ────────────────────────────

    [Test]
    public async Task NumberRange_EmitsIntegralRangeAttribute() {
        var prog = await ProgramFor("""
            domain Demo
            Product: entity {
              ListPrice: Number range(0, 100000000)
            }
            """);

        await Assert.That(prog).Contains("[Range(0, 100000000)]\n    public long ListPrice { get; init; }");
        await Assert.That(prog).DoesNotContain("[Range(0.0, 100000000.0)]");
    }

    [Test]
    public async Task NumberOpenRange_CapsAtLongMaxAsDouble() {
        var prog = await ProgramFor("""
            domain Demo
            Item: entity { Qty: Number range(0, ) }
            """);

        await Assert.That(prog).Contains("[Range(0.0, 9.223372036854776E+18)]");
        await Assert.That(prog).DoesNotContain("E+308");
    }

    [Test]
    public async Task NumberFractionalRange_EmitsDoubleRangeAttribute() {
        var prog = await ProgramFor("""
            domain Demo
            Item: entity { Price: Number range(0.5, 2.5) }
            """);

        await Assert.That(prog).Contains("[Range(0.5, 2.5)]\n    public long Price { get; init; }");
    }

    [Test]
    public async Task NumberRangeBeyondInt_EmitsDoubleRangeAttributeDeliberately() {
        // Deliberately double: a long bound beyond int has no integral attribute constructor.
        var prog = await ProgramFor("""
            domain Demo
            Item: entity { Big: Number range(0, 5000000000) }
            """);

        await Assert.That(prog).Contains("[Range(0.0, 5000000000.0)]\n    public long Big { get; init; }");
    }

    [Test]
    public async Task LongMinValue_HasNoPrintableCSharpLiteral_KnownLimit() {
        // Known limit recorded by issue #281, not fixed here: long.MinValue has no C# literal.
        // `-9223372036854775808L` is a negated POSITIVE literal, and 9223372036854775808 does not
        // fit a long, so this printed text does not compile. The range rule never reaches it
        // (a beyond-int bound takes the double constructor); a long-typed Constant that did would.
        var printed = new CSharpWriter().Write(new Constant(long.MinValue));
        await Assert.That(printed).IsEqualTo("_ = -9223372036854775808L;");
    }
}