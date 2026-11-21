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

    private static (Constant Minimum, Constant Maximum) Bounds(
        string clrType, object? minimum, object? maximum) {
        var args = Poly.Packs.Product.RangeAttributeArguments.Build(clrType, minimum, maximum);
        return ((Constant)args[0], (Constant)args[1]);
    }

    // ── The rule: one decision for every CLR numeric type ─────────────

    [Test]
    public async Task IntegralLongRange_EmitsIntegralConstructor() {
        var (minimum, maximum) = Bounds("long", 0.0, 100_000_000.0);
        await Assert.That(minimum.Value).IsTypeOf<int>();
        await Assert.That(maximum.Value).IsTypeOf<int>();
        await Assert.That((int)minimum.Value!).IsEqualTo(0);
        await Assert.That((int)maximum.Value!).IsEqualTo(100_000_000);
    }

    [Test]
    public async Task IntegralIntRange_EmitsIntegralConstructor() {
        var (minimum, maximum) = Bounds("int", 0.0, 100.0);
        await Assert.That((int)minimum.Value!).IsEqualTo(0);
        await Assert.That((int)maximum.Value!).IsEqualTo(100);
    }

    [Test]
    public async Task IntegralShortRange_EmitsIntegralConstructor() {
        var (minimum, maximum) = Bounds("short", -10.0, 20.0);
        await Assert.That(minimum.Value).IsTypeOf<int>();
        await Assert.That((int)minimum.Value!).IsEqualTo(-10);
        await Assert.That((int)maximum.Value!).IsEqualTo(20);
    }

    [Test]
    public async Task IntegralByteRange_EmitsIntegralConstructor() {
        var (minimum, maximum) = Bounds("byte", 0.0, 255.0);
        await Assert.That(minimum.Value).IsTypeOf<int>();
        await Assert.That((int)minimum.Value!).IsEqualTo(0);
        await Assert.That((int)maximum.Value!).IsEqualTo(255);
    }

    [Test]
    public async Task OpenMaximumOnByte_CapsAtByteMaxIntegrally() {
        var (minimum, maximum) = Bounds("byte", 0.0, null);
        await Assert.That(minimum.Value).IsTypeOf<int>();
        await Assert.That((int)minimum.Value!).IsEqualTo(0);
        await Assert.That((int)maximum.Value!).IsEqualTo(255);
    }

    [Test]
    public async Task OpenMaximumOnShort_CapsAtShortMaxIntegrally() {
        var (minimum, maximum) = Bounds("short", 0.0, null);
        await Assert.That((int)minimum.Value!).IsEqualTo(0);
        await Assert.That((int)maximum.Value!).IsEqualTo(short.MaxValue);
    }

    [Test]
    public async Task OpenMaximumOnInt_CapsAtIntMaxIntegrally() {
        var (minimum, maximum) = Bounds("int", 0.0, null);
        await Assert.That((int)minimum.Value!).IsEqualTo(0);
        await Assert.That((int)maximum.Value!).IsEqualTo(int.MaxValue);
    }

    [Test]
    public async Task OpenMaximumOnLong_UsesTypedConstructorAtLongMax() {
        // Issue #800: long.MaxValue is beyond int, so the typed constructor carries it exactly.
        // The value is the CLR type's real max — never double.MaxValue, which would claim a
        // reachable range the member does not have.
        var args = Poly.Packs.Product.RangeAttributeArguments.Build("long", 0L, null);
        await Assert.That(args.Count).IsEqualTo(3);
        await Assert.That(((Constant)args[1]).Value).IsEqualTo("0");
        await Assert.That(((Constant)args[2]).Value).IsEqualTo("9223372036854775807");
    }

    [Test]
    public async Task FractionalBounds_OnIntegralTarget_EmitsDoubleConstructor() {
        var (minimum, maximum) = Bounds("long", 0.5, 2.5);
        await Assert.That(minimum.Value).IsTypeOf<double>();
        await Assert.That((double)minimum.Value!).IsEqualTo(0.5);
        await Assert.That((double)maximum.Value!).IsEqualTo(2.5);
    }

    [Test]
    public async Task RangeBeyondInt_OnLong_UsesTypedConstructorExact() {
        // Issue #800: RangeAttribute has no (long, long), so an integral bound beyond int goes
        // through (Type, string, string). The double constructor rounded 2^53 + 1 down to 2^53
        // and long.MaxValue up to 2^63, which overflows when the attribute casts it back.
        var args = Poly.Packs.Product.RangeAttributeArguments.Build("long", 0.0, 5_000_000_000.0);
        await Assert.That(args.Count).IsEqualTo(3);
        await Assert.That(((Constant)args[1]).Value).IsEqualTo("0");
        await Assert.That(((Constant)args[2]).Value).IsEqualTo("5000000000");
        var printed = string.Concat(args.Select(a => new CSharpWriter().Write(a)));
        await Assert.That(printed).Contains("typeof(long)");
    }

    [Test]
    public async Task BoundAboveDoublePrecision_OnLong_IsExact() {
        // 2^53 + 1 is the first long a double cannot carry; long.MaxValue - 1 rounds up to 2^63.
        var abovePrecision = Poly.Packs.Product.RangeAttributeArguments.Build("long", 0L, 9_007_199_254_740_993L);
        await Assert.That(((Constant)abovePrecision[2]).Value).IsEqualTo("9007199254740993");

        var nearMax = Poly.Packs.Product.RangeAttributeArguments.Build("long", 0L, long.MaxValue - 1);
        await Assert.That(((Constant)nearMax[2]).Value).IsEqualTo("9223372036854775806");

        var max = Poly.Packs.Product.RangeAttributeArguments.Build("long", null, null);
        await Assert.That(((Constant)max[1]).Value).IsEqualTo("-9223372036854775808");
        await Assert.That(((Constant)max[2]).Value).IsEqualTo("9223372036854775807");
    }

    [Test]
    public async Task DoubleTarget_StaysDoubleConstructor() {
        // A double member's range is genuinely fractional-capable: doubling down on the integral
        // constructor would narrow validation to whole numbers the member accepts.
        var (minimum, maximum) = Bounds("double", 0.0, 100.0);
        await Assert.That(minimum.Value).IsTypeOf<double>();
        await Assert.That(maximum.Value).IsTypeOf<double>();
        await Assert.That((double)maximum.Value!).IsEqualTo(100.0);
    }

    [Test]
    public async Task DecimalTarget_UsesTypeofDecimalAndInvariantStrings() {
        var args = Poly.Packs.Product.RangeAttributeArguments.Build("decimal", 0.0, 100.0);
        await Assert.That(args.Count).IsEqualTo(3);
        var printed = string.Concat(args.Select(a => new CSharpWriter().Write(a)));
        await Assert.That(printed).Contains("typeof(decimal)");
        await Assert.That(printed).DoesNotContain("System.Decimal");
        await Assert.That(((Constant)args[1]).Value).IsEqualTo("0");
        await Assert.That(((Constant)args[2]).Value).IsEqualTo("100");
        await Assert.That(printed).DoesNotContain("0.0");
    }

    [Test]
    public async Task FloatTarget_StaysDoubleConstructor() {
        var (minimum, maximum) = Bounds("float", 0.0, 100.0);
        await Assert.That(minimum.Value).IsTypeOf<double>();
        await Assert.That(maximum.Value).IsTypeOf<double>();
        await Assert.That((double)maximum.Value!).IsEqualTo(100.0);
    }

    [Test]
    public async Task OpenDecimalRange_CapsAtDecimalBoundsAsStrings() {
        var args = Poly.Packs.Product.RangeAttributeArguments.Build("decimal", null, null);
        var minText = decimal.MinValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var maxText = decimal.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await Assert.That(((Constant)args[1]).Value).IsEqualTo(minText);
        await Assert.That(((Constant)args[2]).Value).IsEqualTo(maxText);
        var printed = string.Concat(args.Select(a => new CSharpWriter().Write(a)));
        await Assert.That(printed).Contains("typeof(decimal)");
        await Assert.That(printed).DoesNotContain("System.Decimal");
        await Assert.That(printed).DoesNotContain("(double)");
    }

    // ── End to end: the emitted attribute ────────────────────────────

    /// <summary>V-05b A1: the verified range now hands <c>Build</c> decimal endpoints. The attribute
    /// shape and text are unchanged for ordinary bounds, and a bound above 2^53 stays exact.</summary>
    [Test]
    public async Task VerifiedRangeDto_AttributeUnchanged() {
        var args = Poly.Packs.Product.RangeAttributeArguments.Build("decimal", 0m, 100m);
        await Assert.That(((Constant)args[1]).Value).IsEqualTo("0");
        await Assert.That(((Constant)args[2]).Value).IsEqualTo("100");

        var exact = Poly.Packs.Product.RangeAttributeArguments.Build("decimal", 9007199254740993m, null);
        await Assert.That(((Constant)exact[1]).Value).IsEqualTo("9007199254740993");
    }

    [Test]
    public async Task NumberRange_EmitsIntegralRangeAttribute() {
        var prog = await ProgramFor("""
            domain Demo
            Product: entity {
              ListPrice: Decimal range(0, 100000000)
            }
            """);

        await Assert.That(prog).Contains("[Range(typeof(decimal), \"0\", \"100000000\")]\n    public decimal? ListPrice { get; init; }");
        await Assert.That(prog).DoesNotContain("[Range(0.0, 100000000.0)]");
        await Assert.That(prog).DoesNotContain("System.Decimal");
    }

    [Test]
    public async Task NumberOpenRange_CapsAtLongMaxAsTypedString() {
        var prog = await ProgramFor("""
            domain Demo
            Item: entity { Qty: Number range(0, ) }
            """);

        await Assert.That(prog).Contains("[Range(typeof(long), \"0\", \"9223372036854775807\")]");
        await Assert.That(prog).DoesNotContain("E+308");
        await Assert.That(prog).DoesNotContain("9.223372036854776E+18");
    }

    [Test]
    public async Task NumberFractionalRange_FailsClosedAndNamesDecimal() {
        var result = CompilerWithHttpHost().Compile("""
            domain Demo
            Item: entity { Price: Number range(0.5, 2.5) }
            """, CompileMode.All, DbmsPack.Generic);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(string.Join(" ", result.Errors ?? [])).Contains("Decimal");
    }

    [Test]
    public async Task NumberRangeBeyondInt_EmitsTypedLongRangeAttribute() {
        // Issue #800: a long bound beyond int has no integral attribute constructor, so the typed
        // constructor carries the exact text instead of the double's rounded value.
        var prog = await ProgramFor("""
            domain Demo
            Item: entity { Big: Number range(0, 5000000000) }
            """);

        await Assert.That(prog).Contains("[Range(typeof(long), \"0\", \"5000000000\")]\n    public long? Big { get; init; }");
    }

    [Test]
    public async Task LongMinValue_PrintsTheQualifiedForm_FormerlyAKnownLimit() {
        // This was a real limit and is now closed: long.MinValue has no digits-plus-suffix
        // literal, so the writer prints the qualified form — the same answer DoubleLiteral
        // already gives for values with no literal — and the reader folds it back to the same
        // constant. `-9223372036854775808L` is a negated POSITIVE literal that does not re-read:
        // 9223372036854775808 is outside a long, and the reader overflowed on the text the
        // writer had just produced. Compile proof:
        // CSharpQualifiedConstantTests.LongMinValue_PrintedTextCompiles;
        // round-trip proof: CSharpQualifiedConstantTests.LongMinValue_RoundTripsToTheSameConstant.
        var printed = new CSharpWriter().Write(new Constant(long.MinValue));
        await Assert.That(printed).IsEqualTo("_ = long.MinValue;");

        var reread = CSharpScript.Run("long.MinValue");
        await Assert.That(reread.Success).IsTrue().Because(string.Join("; ", reread.Diagnostics));
        await Assert.That(reread.Value).IsEqualTo("-9223372036854775808");
        await Assert.That(reread.ValueType).IsEqualTo(nameof(Int64));
    }
}