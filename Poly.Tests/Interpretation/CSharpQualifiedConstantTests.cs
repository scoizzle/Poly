using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Poly.Interpretation.CSharp;

namespace Poly.Tests.Interpretation;

/// <summary>
/// A constant whose value has no C# literal prints the qualified form it does have —
/// <c>long.MinValue</c>, <c>double.NaN</c> — and the reader folds that form back to the same
/// <see cref="Constant"/>. Printing is not enough on its own: <c>NodeFacts</c> claims
/// <c>Writer</c> for <see cref="Constant"/> as "prints it and the printed text re-reads to the
/// same kind", so the qualified form must compile and come back as a constant.
/// </summary>
public class CSharpQualifiedConstantTests {
    [Test]
    public async Task LongMinValue_PrintsTheQualifiedForm() {
        // long.MinValue has no digits-plus-suffix literal that the writer's own reader can read:
        // `-9223372036854775808L` is a negated POSITIVE literal, so the reader parses
        // 9223372036854775808 and overflows. The keyword form is the spelling a person writes and
        // the one the writer already uses for values with no literal (DoubleLiteral).
        await Assert.That(CSharpLanguage.Write(new Constant(long.MinValue)))
            .IsEqualTo("_ = long.MinValue;");
    }

    [Test]
    public async Task LongMinValue_PrintedTextCompiles() {
        // A real compile, not a parse: ParseText reports no semantic diagnostic at all (the IS0b
        // lesson), so only a full compilation can say the emitted spelling is assignable to a long.
        var errors = CompileErrors(InMethod(CSharpLanguage.Write(new Constant(long.MinValue))));
        await Assert.That(errors).IsEmpty().Because(string.Join("; ", errors));
    }

    [Test]
    public async Task LongMinValue_RoundTripsToTheSameConstant() {
        var printed = CSharpLanguage.Write(new Constant(long.MinValue));
        var unit = CSharpLanguage.Read(InMethod(printed));

        var constants = Constants(unit);
        await Assert.That(constants.Count).IsEqualTo(1);
        await Assert.That(constants[0].Value).IsTypeOf<long>();
        await Assert.That(constants[0].Value).IsEqualTo(long.MinValue);
        await Assert.That(CSharpLanguage.Write(unit)).Contains("long.MinValue")
            .Because("write(read(write(x))) must be stable");
    }

    [Test]
    public async Task LongMinValue_YieldsTheSameValue() {
        var result = CSharpScript.Run("long.MinValue");
        await Assert.That(result.Success).IsTrue().Because(string.Join("; ", result.Diagnostics));
        await Assert.That(result.Value).IsEqualTo("-9223372036854775808");
        await Assert.That(result.ValueType).IsEqualTo(nameof(Int64))
            .Because("the fold must keep the long type, not widen to double or ulong");
    }

    [Test]
    public async Task IntMinValue_UnchangedOrQualified() {
        // The C# spec special-cases `-2147483648` at the negated-literal position, so the plain
        // spelling is already legal and needs no qualified form. The compile below is the proof.
        var printed = CSharpLanguage.Write(new Constant(int.MinValue));
        await Assert.That(printed).IsEqualTo("_ = -2147483648;");

        var errors = CompileErrors(InMethod(printed));
        await Assert.That(errors).IsEmpty().Because(string.Join("; ", errors));
    }

    [Test]
    public async Task NaN_PrintsAndFolds() {
        // double.NaN prints today but the reader made it a Member, so the printed text did not
        // come back as a Constant; it folds now, by the same mechanism as long.MinValue.
        var printed = CSharpLanguage.Write(new Constant(double.NaN));
        await Assert.That(printed).IsEqualTo("_ = double.NaN;");

        var constants = Constants(CSharpLanguage.Read(InMethod(printed)));
        await Assert.That(constants.Count).IsEqualTo(1);
        await Assert.That(constants[0].Value).IsTypeOf<double>();
        await Assert.That(double.IsNaN((double)constants[0].Value!)).IsTrue();

        var result = CSharpScript.Run("double.NaN");
        await Assert.That(result.Success).IsTrue().Because(string.Join("; ", result.Diagnostics));
        await Assert.That(result.ValueType).IsEqualTo(nameof(Double));
    }

    [Test]
    public async Task EveryConstantKind_PrintedTextCompiles() {
        // The writer must never accept a value whose printed text fails to compile. These are the
        // shapes LiteralText and ConstantText reach, at the boundary values most likely to fall
        // outside a literal: only long.MinValue needed the qualified form.
        (string Label, object Value)[] constants = [
            ("long.MinValue", long.MinValue),
            ("long.MaxValue", long.MaxValue),
            ("long 0", 0L),
            ("int.MinValue", int.MinValue),
            ("int.MaxValue", int.MaxValue),
            ("short.MinValue", short.MinValue),
            ("short.MaxValue", short.MaxValue),
            ("sbyte.MinValue", sbyte.MinValue),
            ("sbyte.MaxValue", sbyte.MaxValue),
            ("byte 255", byte.MaxValue),
            ("ushort.MaxValue", ushort.MaxValue),
            ("uint.MaxValue", uint.MaxValue),
            ("ulong.MaxValue", ulong.MaxValue),
            ("decimal.MinValue", decimal.MinValue),
            ("decimal.MaxValue", decimal.MaxValue),
            ("double.NaN", double.NaN),
            ("double.PositiveInfinity", double.PositiveInfinity),
            ("double.NegativeInfinity", double.NegativeInfinity),
            ("double.MinValue", double.MinValue),
            ("double.MaxValue", double.MaxValue),
            ("float.NaN", float.NaN),
            ("float.PositiveInfinity", float.PositiveInfinity),
            ("float.NegativeInfinity", float.NegativeInfinity),
            ("float.MinValue", float.MinValue),
            ("float.MaxValue", float.MaxValue),
            ("bool", true),
            ("string", "text"),
            ("char", 'x'),
            ("new[] { long.MinValue }", new[] { long.MinValue }),
            ("new List<long> { long.MinValue }", new List<long> { long.MinValue }),
        ];

        var failures = new List<string>();
        foreach (var (label, value) in constants) {
            // The using is only so a List literal is judged on its element text rather than on
            // a missing import; `null` is left out because `_ = null;` cannot infer a discard
            // type (CS8183), which says nothing about the literal.
            var printed = CSharpLanguage.Write(new Constant(value));
            var errors = CompileErrors("using System.Collections.Generic;\n" + InMethod(printed));
            if (errors.Count > 0)
                failures.Add($"{label}: {printed} => {string.Join("; ", errors)}");
        }
        await Assert.That(failures).IsEmpty().Because(string.Join("\n", failures));
    }

    private static string InMethod(string statement) => $"class C {{ void M() {{ {statement} }} }}";

    private static List<Constant> Constants(CompilationUnitNode unit) {
        var found = new List<Constant>();
        foreach (var type in unit.Types)
            Walk(type);
        if (unit.TopLevelStatements is { } statements)
            foreach (var statement in statements)
                Walk(statement);
        return found;

        void Walk(Node node) {
            if (node is Constant constant && constant.Value is not null)
                found.Add(constant);
            foreach (var child in node.Children)
                if (child is not null)
                    Walk(child);
        }
    }

    private static IReadOnlyList<string> CompileErrors(string text) {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "QualifiedConstant",
            [CSharpSyntaxTree.ParseText(text)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return [.. compilation.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(d => $"{d.Id} {d.GetMessage()}")];
    }
}