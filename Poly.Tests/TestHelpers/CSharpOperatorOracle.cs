using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using Poly.Introspection;

using RoslynSeverity = Microsoft.CodeAnalysis.DiagnosticSeverity;

namespace Poly.Tests.TestHelpers;

/// <summary>
/// The oracle for native-integer cells (T-18b lock amendment 6): Roslyn, at C# 11, in ONE compilation of one static
/// method per cell, the operands as parameters (never literals, so no constant rule applies). The verdict is the
/// method's error diagnostics, the static type is the semantic model's type of the operator expression, and the value is
/// the emitted method invoked on the operand values. The runtime binder is never used: it binds <c>IntPtr</c> through
/// its declared members, not through the C# 11 native-integer rules. 64-bit only: the cells are evaluated where
/// <c>nint</c> is 64 bits wide, and every test that reads the oracle asserts that first.
/// </summary>
internal static class CSharpOperatorOracle {
    public sealed record CellAnswer(bool Defined, Type? Type, string Errors, MethodInfo? Method);

    public sealed record ValueAnswer(object? Value, string? Throws);

    public static readonly Type[] Native = [typeof(nint), typeof(nuint)];

    /// <summary>The fourteen numeric types C# 11 has, native integers included.</summary>
    public static readonly Type[] Numeric = [
        typeof(sbyte), typeof(byte), typeof(short), typeof(ushort), typeof(int), typeof(uint),
        typeof(long), typeof(ulong), typeof(char), typeof(float), typeof(double), typeof(decimal),
        typeof(nint), typeof(nuint)
    ];

    /// <summary>The operator ledger's CLR operands that are not numeric: a native integer beside each is a ledger cell.</summary>
    public static readonly Type[] LedgerOthers = [
        typeof(string), typeof(bool), typeof(DateOnly), typeof(TimeOnly), typeof(DateTime), typeof(TimeSpan), typeof(Guid)
    ];

    public static readonly OperatorKind[] Binary = [.. Enum.GetValues<OperatorKind>().Where(static k => OperatorShape.ArityOf(k) == 2)];

    public static readonly OperatorKind[] Unary = [.. Enum.GetValues<OperatorKind>().Where(static k => OperatorShape.ArityOf(k) == 1)];

    private static readonly Type[] Forms = [
        .. Numeric, .. Numeric.Select(static t => typeof(Nullable<>).MakeGenericType(t)), .. LedgerOthers
    ];

    public static bool IsNativeForm(Type type) => Native.Contains(Nullable.GetUnderlyingType(type) ?? type);

    /// <summary>Every cell the oracle answers: each operator with at least one native-integer operand form.</summary>
    public static IEnumerable<(OperatorKind Kind, Type Left, Type? Right)> Universe() {
        foreach (var kind in Binary)
            foreach (var left in Forms)
                foreach (var right in Forms)
                    if (IsNativeForm(left) || IsNativeForm(right))
                        yield return (kind, left, right);
        foreach (var kind in Unary)
            foreach (var operand in Forms.Where(IsNativeForm))
                yield return (kind, operand, null);
    }

    /// <summary>Every ordered pair of distinct numeric types with a native integer on a side.</summary>
    public static IEnumerable<(Type From, Type To)> ConversionUniverse() =>
        from source in Numeric
        from target in Numeric
        where source != target && (Native.Contains(source) || Native.Contains(target))
        select (source, target);

    /// <summary>The arithmetic and comparison kinds a constant operand meets (C# 10.2.11 applies to them).</summary>
    public static readonly OperatorKind[] ConstantKinds = [
        OperatorKind.Add, OperatorKind.Subtract, OperatorKind.Multiply, OperatorKind.Divide, OperatorKind.Modulo,
        OperatorKind.Equal, OperatorKind.NotEqual, OperatorKind.LessThan, OperatorKind.LessThanOrEqual,
        OperatorKind.GreaterThan, OperatorKind.GreaterThanOrEqual
    ];

    /// <summary>The signed integral constants a native operand may meet: 1 and -1 of sbyte, short, int and long.</summary>
    public static readonly object[] Constants = [(sbyte)1, (sbyte)(-1), (short)1, (short)(-1), 1, -1, 1L, -1L];

    /// <summary>Every constant cell: a native form beside a signed integral constant, the constant on either side.</summary>
    public static IEnumerable<(OperatorKind Kind, Type Operand, object Constant, bool ConstantLeft)> ConstantUniverse() =>
        from kind in ConstantKinds
        from operand in Forms.Where(IsNativeForm)
        from constant in Constants
        from constantLeft in new[] { false, true }
        select (kind, operand, constant, constantLeft);

    public static string ConstantKey(OperatorKind kind, Type operand, object constant, bool constantLeft) =>
        constantLeft ? $"({Keyword(constant.GetType())}){constant} {kind} {Keyword(operand)}" : $"{Keyword(operand)} {kind} ({Keyword(constant.GetType())}){constant}";

    private static string Literal(object constant) => $"(({Keyword(constant.GetType())})({constant}))";

    private sealed record Oracle(
        Dictionary<(OperatorKind, Type, Type?), CellAnswer> Operators,
        Dictionary<(Type, Type), (ConversionOperatorKind? Kind, MethodInfo? Implicit, MethodInfo? Cast)> Conversions,
        Dictionary<(OperatorKind, Type, object, bool), CellAnswer> ConstantCells);

    private static readonly Lazy<Oracle> Compiled = new(Compile);

    public static TimeSpan CompileTime { get; private set; }

    /// <summary>The answer for one cell; a cell outside <see cref="Universe"/> is an error, never a default.</summary>
    public static CellAnswer Answer(OperatorKind kind, Type left, Type? right) =>
        Compiled.Value.Operators.TryGetValue((kind, left, right), out var answer)
            ? answer
            : throw new ArgumentOutOfRangeException(nameof(left), $"{Key(kind, left, right)} is not an oracle cell");

    /// <summary>C#'s conversion from <paramref name="from"/> to <paramref name="to"/> (null: none); a pair outside <see cref="ConversionUniverse"/> is an error.</summary>
    public static ConversionOperatorKind? Conversion(Type from, Type to) =>
        Compiled.Value.Conversions.TryGetValue((from, to), out var answer)
            ? answer.Kind
            : throw new ArgumentOutOfRangeException(nameof(from), $"{Keyword(from)} to {Keyword(to)} is not an oracle conversion");

    /// <summary>The value of <c>(to)a</c>, or of <c>to r = a</c> when <paramref name="implicitOnly"/>; the conversion must exist in that form.</summary>
    public static ValueAnswer ConversionValue(Type from, Type to, object? value, bool implicitOnly) {
        var answer = Compiled.Value.Conversions.TryGetValue((from, to), out var found)
            ? found
            : throw new ArgumentOutOfRangeException(nameof(from), $"{Keyword(from)} to {Keyword(to)} is not an oracle conversion");
        var method = (implicitOnly ? answer.Implicit : answer.Cast)
            ?? throw new InvalidOperationException($"C# has no {(implicitOnly ? "implicit" : "explicit")} conversion from {Keyword(from)} to {Keyword(to)}");
        return Invoke(method, [value]);
    }

    /// <summary>The answer for a constant cell; a cell outside <see cref="ConstantUniverse"/> is an error.</summary>
    public static CellAnswer ConstantAnswer(OperatorKind kind, Type operand, object constant, bool constantLeft) =>
        Compiled.Value.ConstantCells.TryGetValue((kind, operand, constant, constantLeft), out var answer)
            ? answer
            : throw new ArgumentOutOfRangeException(nameof(operand), $"{ConstantKey(kind, operand, constant, constantLeft)} is not an oracle cell");

    private static ValueAnswer Invoke(MethodInfo method, object?[] arguments) {
        try {
            return new(method.Invoke(null, arguments), null);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is { } inner) {
            return new(null, inner.GetType().Name);
        }
    }

    /// <summary>The value C# computes for the cell on <paramref name="left"/> and <paramref name="right"/>.</summary>
    public static ValueAnswer Value(OperatorKind kind, Type leftType, Type? rightType, object? left, object? right) {
        var answer = Answer(kind, leftType, rightType);
        if (answer.Method is not { } method)
            throw new InvalidOperationException($"{Key(kind, leftType, rightType)} is not defined in C#: {answer.Errors}");
        try {
            return new(method.Invoke(null, rightType is null ? [left] : [left, right]), null);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is { } inner) {
            return new(null, inner.GetType().Name);
        }
    }

    public static string Key(OperatorKind kind, Type left, Type? right) =>
        right is null ? $"{kind} {Keyword(left)}" : $"{Keyword(left)} {kind} {Keyword(right)}";

    public static string Keyword(Type type) {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
            return Keyword(underlying) + "?";
        return type switch {
            _ when type == typeof(sbyte) => "sbyte",
            _ when type == typeof(byte) => "byte",
            _ when type == typeof(short) => "short",
            _ when type == typeof(ushort) => "ushort",
            _ when type == typeof(int) => "int",
            _ when type == typeof(uint) => "uint",
            _ when type == typeof(long) => "long",
            _ when type == typeof(ulong) => "ulong",
            _ when type == typeof(char) => "char",
            _ when type == typeof(float) => "float",
            _ when type == typeof(double) => "double",
            _ when type == typeof(decimal) => "decimal",
            _ when type == typeof(nint) => "nint",
            _ when type == typeof(nuint) => "nuint",
            _ when type == typeof(string) => "string",
            _ when type == typeof(bool) => "bool",
            _ => "global::" + type.FullName
        };
    }

    private static string Expression(OperatorKind kind) => kind switch {
        OperatorKind.Add => "a + b",
        OperatorKind.Subtract => "a - b",
        OperatorKind.Multiply => "a * b",
        OperatorKind.Divide => "a / b",
        OperatorKind.Modulo => "a % b",
        OperatorKind.Equal => "a == b",
        OperatorKind.NotEqual => "a != b",
        OperatorKind.LessThan => "a < b",
        OperatorKind.LessThanOrEqual => "a <= b",
        OperatorKind.GreaterThan => "a > b",
        OperatorKind.GreaterThanOrEqual => "a >= b",
        OperatorKind.And => "a && b",
        OperatorKind.Or => "a || b",
        OperatorKind.Not => "!a",
        OperatorKind.BitwiseAnd => "a & b",
        OperatorKind.BitwiseOr => "a | b",
        OperatorKind.BitwiseXor => "a ^ b",
        OperatorKind.ShiftLeft => "a << b",
        OperatorKind.ShiftRight => "a >> b",
        OperatorKind.UnaryMinus => "-a",
        OperatorKind.UnaryPlus => "+a",
        OperatorKind.BitwiseNot => "~a",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    private static string Method(int index, OperatorKind kind, Type left, Type? right, bool throwInstead) {
        var parameters = right is null ? $"{Keyword(left)} a" : $"{Keyword(left)} a, {Keyword(right)} b";
        var body = throwInstead ? "throw null" : Expression(kind);
        return $"public static object M{index}({parameters}) => {body};";
    }

    private static Oracle Compile() {
        if (IntPtr.Size != 8)
            throw new PlatformNotSupportedException("the native-integer oracle runs on a 64-bit host only");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var cells = Universe().ToArray();
        var pairs = ConversionUniverse().ToArray();
        var constants = ConstantUniverse().ToArray();
        // One method per line: the operator cells, an implicit and an explicit conversion method per pair, then the
        // constant cells (a parameter beside a literal constant: 10.2.11 is the point of those).
        const int header = 2;
        var conversionStart = cells.Length;
        var constantStart = conversionStart + (2 * pairs.Length);
        var lines = constantStart + constants.Length;
        string Source(Func<int, bool> fails) {
            var text = new StringBuilder("#nullable disable\npublic static class Cells {\n");
            for (var i = 0; i < cells.Length; i++)
                text.Append(Method(i, cells[i].Kind, cells[i].Left, cells[i].Right, fails(i))).Append('\n');
            for (var j = 0; j < pairs.Length; j++) {
                var (from, to) = pairs[j];
                var line = conversionStart + (2 * j);
                text.Append($"public static object I{j}({Keyword(from)} a) {(fails(line) ? "=> throw null;" : $"{{ {Keyword(to)} r = a; return r; }}")}\n");
                text.Append($"public static object E{j}({Keyword(from)} a) => {(fails(line + 1) ? "throw null" : $"({Keyword(to)})a")};\n");
            }
            for (var k = 0; k < constants.Length; k++) {
                var (kind, operand, constant, constantLeft) = constants[k];
                var expression = Expression(kind).Replace("a", "\u0001", StringComparison.Ordinal).Replace("b", "\u0002", StringComparison.Ordinal)
                    .Replace("\u0001", constantLeft ? Literal(constant) : "x", StringComparison.Ordinal)
                    .Replace("\u0002", constantLeft ? "x" : Literal(constant), StringComparison.Ordinal);
                text.Append($"public static object K{k}({Keyword(operand)} x) => {(fails(constantStart + k) ? "throw null" : expression)};\n");
            }
            return text.Append("}\n").ToString();
        }
        var references = RoslynReferences.Core;
        var parse = new CSharpParseOptions(LanguageVersion.CSharp11);
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);

        var tree = CSharpSyntaxTree.ParseText(Source(static _ => false), parse);
        var compilation = CSharpCompilation.Create("NativeIntegerOracle", [tree], references, options);
        var errors = new Dictionary<int, List<string>>();
        foreach (var diagnostic in compilation.GetDiagnostics().Where(static d => d.Severity == RoslynSeverity.Error)) {
            var index = diagnostic.Location.GetLineSpan().StartLinePosition.Line - header;
            if (index < 0 || index >= lines)
                throw new InvalidOperationException($"oracle diagnostic outside a cell: {diagnostic}");
            (errors.TryGetValue(index, out var list) ? list : errors[index] = []).Add(diagnostic.Id);
        }
        var model = compilation.GetSemanticModel(tree);
        var bodies = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
        if (bodies.Length != lines)
            throw new InvalidOperationException($"oracle parsed {bodies.Length} methods for {lines} lines");

        var emitTree = CSharpSyntaxTree.ParseText(Source(i => errors.ContainsKey(i)), parse);
        using var pe = new MemoryStream();
        var emit = CSharpCompilation.Create("NativeIntegerOracleRun", [emitTree], references, options).Emit(pe);
        if (!emit.Success)
            throw new InvalidOperationException("oracle emit failed: " + string.Join("\n", emit.Diagnostics.Where(static d => d.Severity == RoslynSeverity.Error).Take(5)));
        pe.Position = 0;
        var type = new AssemblyLoadContext("NativeIntegerOracleRun", isCollectible: true).LoadFromStream(pe).GetType("Cells")!;

        CellAnswer AnswerAt(int line, string name, string key) {
            if (errors.TryGetValue(line, out var ids))
                return new(false, null, string.Join(",", ids.Distinct()), null);
            var symbol = model.GetTypeInfo(bodies[line].ExpressionBody!.Expression).Type
                ?? throw new InvalidOperationException($"oracle has no type for {key}");
            return new(true, ToClr(symbol), "", type.GetMethod(name)!);
        }
        var answers = new Dictionary<(OperatorKind, Type, Type?), CellAnswer>();
        for (var i = 0; i < cells.Length; i++)
            answers[cells[i]] = AnswerAt(i, $"M{i}", Key(cells[i].Kind, cells[i].Left, cells[i].Right));
        var conversions = new Dictionary<(Type, Type), (ConversionOperatorKind?, MethodInfo?, MethodInfo?)>();
        for (var j = 0; j < pairs.Length; j++) {
            var implicitLine = conversionStart + (2 * j);
            var implicitOk = !errors.ContainsKey(implicitLine);
            var castOk = !errors.ContainsKey(implicitLine + 1);
            conversions[pairs[j]] = (
                implicitOk ? ConversionOperatorKind.Implicit : castOk ? ConversionOperatorKind.Explicit : null,
                implicitOk ? type.GetMethod($"I{j}") : null,
                castOk ? type.GetMethod($"E{j}") : null);
        }
        var constantCells = new Dictionary<(OperatorKind, Type, object, bool), CellAnswer>();
        for (var k = 0; k < constants.Length; k++)
            constantCells[constants[k]] = AnswerAt(constantStart + k, $"K{k}", ConstantKey(constants[k].Kind, constants[k].Operand, constants[k].Constant, constants[k].ConstantLeft));
        watch.Stop();
        CompileTime = watch.Elapsed;
        return new(answers, conversions, constantCells);
    }

    private static Type ToClr(ITypeSymbol symbol) {
        if (symbol is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            return typeof(Nullable<>).MakeGenericType(ToClr(nullable.TypeArguments[0]));
        var name = symbol.ContainingNamespace is { IsGlobalNamespace: false } ns ? $"{ns.ToDisplayString()}.{symbol.MetadataName}" : symbol.MetadataName;
        return typeof(object).Assembly.GetType(name)
            ?? throw new InvalidOperationException($"oracle type '{name}' has no CLR type");
    }
}