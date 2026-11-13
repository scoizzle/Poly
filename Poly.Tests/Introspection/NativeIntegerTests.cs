using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.Interpretation;
using Poly.Interpretation.Vm;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Tests.TestHelpers;

using RoslynSeverity = Microsoft.CodeAnalysis.DiagnosticSeverity;

namespace Poly.Tests.Introspection;

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
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator)
            .Where(static p => Path.GetFileName(p) is "System.Private.CoreLib.dll" or "System.Runtime.dll")
            .Select(static p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToArray();
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

/// <summary>
/// The native-integer grid (T-18b lock amendment 8): every operator over a native-integer form ({nint, nuint, nint?,
/// nuint?}) beside each of the fourteen numeric types and their nullables, both orders, and the unary minus and
/// complement. Each cell is analyzed, compiled and run on the interpreter over API-built parameters, and compared with
/// <see cref="CSharpOperatorOracle"/> in verdict, type and value, on a stress and a plain value per operand and, for a
/// nullable operand, on null.
/// </summary>
internal static class NativeIntegerGrid {
    public static readonly OperatorKind[] Kinds = [
        OperatorKind.Add, OperatorKind.Subtract, OperatorKind.Multiply, OperatorKind.Divide, OperatorKind.Modulo,
        OperatorKind.Equal, OperatorKind.NotEqual, OperatorKind.LessThan, OperatorKind.LessThanOrEqual,
        OperatorKind.GreaterThan, OperatorKind.GreaterThanOrEqual,
        OperatorKind.BitwiseAnd, OperatorKind.BitwiseOr, OperatorKind.BitwiseXor, OperatorKind.ShiftLeft, OperatorKind.ShiftRight
    ];

    public static readonly OperatorKind[] UnaryKinds = [OperatorKind.UnaryMinus, OperatorKind.BitwiseNot];

    /// <summary>Stress then plain per numeric type. Native: a wrap at the top, a negative, and a <c>nuint</c> at or above 2^63.</summary>
    public static readonly Dictionary<Type, (object Stress, object Plain)> Values = new() {
        [typeof(sbyte)] = (sbyte.MaxValue, (sbyte)7),
        [typeof(byte)] = (byte.MaxValue, (byte)7),
        [typeof(short)] = (short.MaxValue, (short)7),
        [typeof(ushort)] = (ushort.MaxValue, (ushort)7),
        [typeof(int)] = (int.MaxValue, 7),
        [typeof(uint)] = (uint.MaxValue, 7u),
        [typeof(long)] = (9007199254740993L, 7L),
        [typeof(ulong)] = (ulong.MaxValue, 7UL),
        [typeof(char)] = ('a', 'b'),
        [typeof(float)] = (16777216f, 1.5f),
        [typeof(double)] = (0.1d, 2.5d),
        [typeof(decimal)] = (0.1m, 0.2m),
        [typeof(nint)] = (nint.MaxValue, (nint)(-7)),
        [typeof(nuint)] = (unchecked((nuint)0x8000_0000_0000_0005UL), (nuint)7),
    };

    public sealed record Cell(OperatorKind Kind, Type Left, Type? Right, string Class, string Detail) {
        public string Key => CSharpOperatorOracle.Key(Kind, Left, Right);
        public string Line => $"{Key} | {Class} | {Detail}";
    }

    public static IEnumerable<(OperatorKind Kind, Type Left, Type? Right)> Shape() {
        var forms = CSharpOperatorOracle.Numeric.Concat(CSharpOperatorOracle.Numeric.Select(static t => typeof(Nullable<>).MakeGenericType(t))).ToArray();
        foreach (var kind in Kinds)
            foreach (var left in forms)
                foreach (var right in forms)
                    if (CSharpOperatorOracle.IsNativeForm(left) || CSharpOperatorOracle.IsNativeForm(right))
                        yield return (kind, left, right);
        foreach (var kind in UnaryKinds)
            foreach (var operand in forms.Where(CSharpOperatorOracle.IsNativeForm))
                yield return (kind, operand, null);
    }

    public static readonly Lazy<IReadOnlyList<Cell>> Cells = new(() => {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try {
            return [.. Shape().Select(static c => Evaluate(c.Kind, c.Left, c.Right))];
        }
        finally {
            CultureInfo.CurrentCulture = culture;
        }
    });

    public static Node Build(OperatorKind kind, Node left, Node? right) => kind switch {
        OperatorKind.Add => new Add(left, right!),
        OperatorKind.Subtract => new Subtract(left, right!),
        OperatorKind.Multiply => new Multiply(left, right!),
        OperatorKind.Divide => new Divide(left, right!),
        OperatorKind.Modulo => new Modulo(left, right!),
        OperatorKind.Equal => new Equal(left, right!),
        OperatorKind.NotEqual => new NotEqual(left, right!),
        OperatorKind.LessThan => new LessThan(left, right!),
        OperatorKind.LessThanOrEqual => new LessThanOrEqual(left, right!),
        OperatorKind.GreaterThan => new GreaterThan(left, right!),
        OperatorKind.GreaterThanOrEqual => new GreaterThanOrEqual(left, right!),
        OperatorKind.BitwiseAnd => new BitwiseAnd(left, right!),
        OperatorKind.BitwiseOr => new BitwiseOr(left, right!),
        OperatorKind.BitwiseXor => new BitwiseXor(left, right!),
        OperatorKind.ShiftLeft => new ShiftLeft(left, right!),
        OperatorKind.ShiftRight => new ShiftRight(left, right!),
        OperatorKind.UnaryMinus => new UnaryMinus(left),
        OperatorKind.BitwiseNot => new BitwiseNot(left),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    /// <summary>Stress beside plain, plain beside stress, then each nullable operand absent.</summary>
    public static IEnumerable<(string Name, object? Left, object? Right)> Arguments(Type left, Type? right) {
        var lv = Values[Nullable.GetUnderlyingType(left) ?? left];
        if (right is null) {
            yield return ("S", lv.Stress, null);
            yield return ("P", lv.Plain, null);
            if (Nullable.GetUnderlyingType(left) is not null)
                yield return ("N", null, null);
            yield break;
        }
        var rv = Values[Nullable.GetUnderlyingType(right) ?? right];
        yield return ("S1", lv.Stress, rv.Plain);
        yield return ("S2", lv.Plain, rv.Stress);
        if (Nullable.GetUnderlyingType(left) is not null)
            yield return ("NL", null, rv.Plain);
        if (Nullable.GetUnderlyingType(right) is not null)
            yield return ("NR", lv.Plain, null);
    }

    public static string Format(object? value) => value switch {
        null => "null",
        char c => $"char {(int)c}",
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "null"
    };

    /// <summary>The VM's result read as the type the program states: a scalar root is a 64-bit word.</summary>
    public static object? Read(ExecutionResult exec, Type? root) {
        var value = exec.Result.Value;
        // The interpreter answers a float root as the double its word holds.
        if (value is double widened && root == typeof(float))
            return (float)widened;
        if (value is not long word || root is null)
            return value;
        // A lifted root answers its present value as the boxed word, read as it is (as the pre-T-18a table was captured).
        if (Nullable.GetUnderlyingType(root) is not null)
            return value;
        // A non-nullable heap root (decimal) answered as a bare word is the interpreter's fallback for a handle that
        // names no heap object: never a value, so it can never pass as one.
        if (!Poly.Interpretation.Vm.Abi.LocalType.IsInline(root))
            return $"raw word {word} for a {root.Name}";
        return root switch {
            _ when root == typeof(bool) => word != 0,
            _ when root == typeof(double) => BitConverter.Int64BitsToDouble(word),
            _ when root == typeof(float) => (float)BitConverter.Int64BitsToDouble(word),
            _ when root == typeof(nint) => (nint)word,
            _ when root == typeof(nuint) => unchecked((nuint)(ulong)word),
            _ when root == typeof(ulong) => unchecked((ulong)word),
            _ when root == typeof(uint) => unchecked((uint)word),
            _ when root == typeof(int) => unchecked((int)word),
            _ when root == typeof(ushort) => unchecked((ushort)word),
            _ when root == typeof(short) => unchecked((short)word),
            _ when root == typeof(byte) => unchecked((byte)word),
            _ when root == typeof(sbyte) => unchecked((sbyte)word),
            _ when root == typeof(char) => unchecked((char)word),
            _ => word
        };
    }

    private static string Short(string text) => text.Length <= 90 ? text : text[..90];

    private static string Name(Type? type) => type is null ? "?" : CSharpOperatorOracle.Keyword(type);

    public static Cell Evaluate(OperatorKind kind, Type left, Type? right) {
        var oracle = CSharpOperatorOracle.Answer(kind, left, right);
        var a = new Parameter("a", new ClrTypeReference(left));
        var b = right is null ? null : new Parameter("b", new ClrTypeReference(right));
        var node = Build(kind, a, b);
        AnalysisResult analysis;
        try {
            analysis = Interpreter.Analyze(node, ClrTypeDefinitionRegistry.Shared);
        }
        catch (Exception ex) {
            return new(kind, left, right, oracle.Defined ? "defined: analysis throws" : "undefined: analysis throws", ex.GetType().Name);
        }
        var error = analysis.Diagnostics.FirstOrDefault(static d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error);
        if (error is not null)
            return new(kind, left, right, oracle.Defined ? "defined: rejected" : "undefined: rejected", Short(error.Message));
        VmProgram program;
        try {
            program = Interpreter.Compile(node, analysis);
        }
        catch (Exception ex) {
            return new(kind, left, right, oracle.Defined ? "defined: emit throws" : "undefined: emit throws", $"{ex.GetType().Name}: {Short(ex.Message)}");
        }
        var root = program.RootClrType;
        var runs = new List<string>();
        var wrong = false;
        var threw = false;
        var wordOnly = false;
        foreach (var (name, lv, rv) in Arguments(left, right)) {
            string actual;
            string? asCSharpType = null;
            try {
                using var exec = Interpreter.Execute(program, right is null ? new object?[] { lv } : new object?[] { lv, rv });
                actual = Format(Read(exec, root));
                if (oracle.Defined && oracle.Type != root)
                    asCSharpType = Format(Read(exec, oracle.Type));
            }
            catch (Exception ex) {
                actual = $"throws {ex.GetType().Name}";
                threw = true;
            }
            if (!oracle.Defined) {
                runs.Add($"{name}={actual}");
                continue;
            }
            var expected = CSharpOperatorOracle.Value(kind, left, right, lv, rv);
            var expectedText = expected.Throws is { } exception ? $"throws {exception}" : Format(expected.Value);
            if (actual == expectedText) {
                runs.Add($"{name}={actual}");
                continue;
            }
            // The word is C#'s value, read as the type the program states instead of C#'s (T-17's result typing).
            if (asCSharpType == expectedText) {
                wordOnly = true;
                runs.Add($"{name}: VM {actual} as {Name(root)}, {asCSharpType} as {Name(oracle.Type)}");
                continue;
            }
            wrong = true;
            runs.Add($"{name}: VM {actual}, C# {expectedText}");
        }
        var typeNote = oracle.Defined && root != oracle.Type ? $"type VM {Name(root)}, C# {Name(oracle.Type)}; " : $"type {Name(root)}; ";
        var detail = typeNote + string.Join("; ", runs);
        if (!oracle.Defined)
            return new(kind, left, right, "undefined: runs", detail);
        return new(kind, left, right,
            threw ? "defined: run throws" : wrong ? "defined: wrong value" : wordOnly ? "defined: right word, stated type differs" : "defined: runs right", detail);
    }
}

public class NativeIntegerTests {
    private static ITypeDefinition Clr(Type type) => ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type);

    private static Parameter Param(string name, Type type) => new(name, new ClrTypeReference(type));

    private static string PreT18aPath() =>
        Path.Combine(GoldenDomains.RepoRoot, "Poly.Tests", "Introspection", "NativeIntegerPreT18a.txt");

    /// <summary>The grid as it ran on the tree before T-18a (0d2cd4a8^1 = 77704337), captured there with this file.</summary>
    internal static IReadOnlyList<(string Key, string Class, string Detail)> PreT18a() =>
        [.. File.ReadAllLines(PreT18aPath()).Where(static l => l.Length > 0).Select(static line => {
            var parts = line.Split(" | ", 3, StringSplitOptions.None);
            return parts.Length == 3 ? (parts[0], parts[1], parts[2]) : throw new FormatException($"not a grid line: '{line}'");
        })];

    /// <summary>Set <c>POLY_NATIVE_OUT</c> to a path to write the grid there (the characterisation run on another tree).</summary>
    [Test]
    public async Task Grid_WritesItsTable_WhenAsked() {
        await Assert.That(IntPtr.Size).IsEqualTo(8);
        if (Environment.GetEnvironmentVariable("POLY_NATIVE_OUT") is not { Length: > 0 } output)
            return;
        var cells = NativeIntegerGrid.Cells.Value;
        await File.WriteAllTextAsync(output, string.Join("\n", cells.Select(static c => c.Line)) + "\n");
        var summary = cells.GroupBy(static c => c.Class).OrderBy(static g => g.Key, StringComparer.Ordinal).Select(static g => $"{g.Key}: {g.Count()}");
        await File.WriteAllTextAsync(output + ".summary", string.Join("\n", summary) + $"\ncells: {cells.Count}\noracle compile: {CSharpOperatorOracle.CompileTime}\n");
    }

    // ---- characterisation (T-18b step 1): master and the tree before T-18a ----

    /// <summary>The pre-T-18a table covers exactly the grid, one line per cell, in the grid's order.</summary>
    [Test]
    public async Task PreT18aTable_CoversTheGrid_AndRecordsWhatEachCellDidThere() {
        var table = PreT18a();
        await Assert.That(table.Select(static c => c.Key).ToArray()).IsEquivalentTo(NativeIntegerGrid.Shape().Select(static c => CSharpOperatorOracle.Key(c.Kind, c.Left, c.Right)).ToArray());
        await Assert.That(table.Count).IsEqualTo(3336);
        var classes = table.GroupBy(static c => c.Class).ToDictionary(static g => g.Key, static g => g.Count());
        await Assert.That(classes).IsEquivalentTo(PreT18aClasses);
    }

    /// <summary>
    /// What the grid did on 77704337. "Defined" is the oracle's verdict. Only the 981 cells that ran right there bind
    /// this slice (lock amendment 7): each must run right after it. The 64 whose word was right under a narrower stated
    /// type are T-17's typing; the others threw, were refused, or computed a wrong value there too.
    /// </summary>
    internal static readonly Dictionary<string, int> PreT18aClasses = new() {
        ["defined: emit throws"] = 881,
        ["defined: rejected"] = 88,
        ["defined: right word, stated type differs"] = 64,
        ["defined: run throws"] = 60,
        ["defined: runs right"] = 981,
        ["defined: wrong value"] = 124,
        ["undefined: emit throws"] = 641,
        ["undefined: runs"] = 497,
    };

    // ---- the four T-18a regressions (row T-18's red test) ----

    /// <summary>
    /// T-18a's four regressions: each ran before T-18a (<c>nuint &lt; nuint</c> read a value at or above 2^63 as
    /// negative there) and was rejected since (the characterisation commit pinned the messages). Each compiles and
    /// agrees with C# in verdict, static type and value, on every argument set of the grid.
    /// </summary>
    [Test]
    [Arguments(OperatorKind.LessThan, typeof(nint), typeof(nint))]
    [Arguments(OperatorKind.Add, typeof(nint), typeof(nint))]
    [Arguments(OperatorKind.Add, typeof(nint), typeof(int))]
    [Arguments(OperatorKind.LessThan, typeof(nuint), typeof(nuint))]
    public async Task RegressedByT18a_CompilesAndAgreesWithCSharp(OperatorKind kind, Type left, Type right) {
        await Assert.That(IntPtr.Size).IsEqualTo(8);
        var oracle = CSharpOperatorOracle.Answer(kind, left, right);
        await Assert.That(oracle.Defined).IsTrue();
        var node = NativeIntegerGrid.Build(kind, Param("a", left), Param("b", right));
        var analysis = Interpreter.Analyze(node);
        await Assert.That(analysis.Diagnostics.Where(static d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error).Select(static d => d.Message).ToArray()).IsEmpty();
        var program = Interpreter.Compile(node, analysis);
        await Assert.That(program.RootClrType).IsEqualTo(oracle.Type);
        foreach (var (_, lv, rv) in NativeIntegerGrid.Arguments(left, right)) {
            using var exec = Interpreter.Execute(program, lv, rv);
            await Assert.That(NativeIntegerGrid.Format(NativeIntegerGrid.Read(exec, program.RootClrType)))
                .IsEqualTo(NativeIntegerGrid.Format(CSharpOperatorOracle.Value(kind, left, right, lv, rv).Value));
        }
    }

    // ---- the provider (lock: design; amendments 4 and 5) ----

    /// <summary>The built-in operator answers for native integers; IntPtr's reflected op_* members stand down.</summary>
    [Test]
    public async Task Provider_NintPlusInt_IsTheBuiltInOperator_AndNintIsAPrimitive() {
        var nint = Clr(typeof(nint));
        var resolution = nint.GetOperator(OperatorKind.Add, Clr(typeof(int)));
        await Assert.That(resolution is { PlatformApplied: true }).IsTrue();
        await Assert.That(resolution!.Value.Result).IsSameReferenceAs(nint);
        await Assert.That(resolution.Value.Signature.Parameters.All(p => ReferenceEquals(p.ParameterTypeDefinition, nint))).IsTrue();
        await Assert.That(nint.PrimitiveType?.GetCSharpKeyword()).IsEqualTo("nint");
        await Assert.That(Clr(typeof(nuint)).PrimitiveType?.GetCSharpKeyword()).IsEqualTo("nuint");
        await Assert.That(nint.Kind).IsEqualTo(Poly.Introspection.TypeKind.Primitive);
        foreach (var kind in Enum.GetValues<OperatorKind>())
            await Assert.That(nint.GetOperators(kind).Any(static o => !o.PlatformApplied)).IsFalse().Because($"IntPtr's reflected {kind} stands down");
    }

    /// <summary>
    /// Built in first when the built-in answer is implicit, else declared (amendment 4): nint to long is the implicit
    /// built-in conversion C# 11 has, and (int)nint keeps IntPtr's declared op_Explicit method (T-16's Method form).
    /// </summary>
    [Test]
    public async Task Provider_NintToLongIsImplicitBuiltIn_IntFromNintKeepsTheDeclaredExplicit() {
        var toLong = Clr(typeof(long)).ResolveConversionFrom(Clr(typeof(nint)));
        await Assert.That(toLong?.Kind).IsEqualTo(ConversionOperatorKind.Implicit);
        await Assert.That(toLong!.Value.Method).IsNull();
        var toInt = Clr(typeof(int)).ResolveConversionFrom(Clr(typeof(nint)));
        await Assert.That(toInt?.Kind).IsEqualTo(ConversionOperatorKind.Explicit);
        await Assert.That(toInt!.Value.Method?.Name).IsEqualTo("op_Explicit");
        var toDecimal = Clr(typeof(decimal)).ResolveConversionFrom(Clr(typeof(nuint)));
        await Assert.That(toDecimal?.Kind).IsEqualTo(ConversionOperatorKind.Implicit);
    }

    /// <summary>Every conversion between a native integer and a numeric type, against C#.</summary>
    [Test]
    public async Task Provider_EveryNativeConversion_AgreesWithCSharp() {
        await Assert.That(IntPtr.Size).IsEqualTo(8);
        var mismatches = new List<string>();
        var (implicitCount, explicitCount) = (0, 0);
        foreach (var (from, to) in CSharpOperatorOracle.ConversionUniverse()) {
            var expected = CSharpOperatorOracle.Conversion(from, to);
            implicitCount += expected is ConversionOperatorKind.Implicit ? 1 : 0;
            explicitCount += expected is ConversionOperatorKind.Explicit ? 1 : 0;
            var actual = Clr(to).ResolveConversionFrom(Clr(from));
            if (actual?.Kind != expected)
                mismatches.Add($"{CSharpOperatorOracle.Keyword(from)} -> {CSharpOperatorOracle.Keyword(to)}: C# {expected?.ToString() ?? "none"}, provider {actual?.Kind.ToString() ?? "none"}");
            else if (actual is { Kind: ConversionOperatorKind.Implicit, Method: not null })
                mismatches.Add($"{CSharpOperatorOracle.Keyword(from)} -> {CSharpOperatorOracle.Keyword(to)}: an implicit native conversion is built in, provider states {actual.Value.Method.Name}");
        }
        await Assert.That(mismatches).IsEmpty();
        // The oracle itself: 50 ordered pairs; C# 11 has 18 implicit native conversions, every other pair is explicit.
        await Assert.That(implicitCount).IsEqualTo(18);
        await Assert.That(explicitCount).IsEqualTo(32);
    }

    /// <summary>Every operator over two non-nullable numeric operands with a native integer, against C#: verdict and result type.</summary>
    [Test]
    public async Task Provider_EveryNonLiftedNativeCell_AgreesWithCSharp_InVerdictAndResultType() {
        await Assert.That(IntPtr.Size).IsEqualTo(8);
        var mismatches = new List<string>();
        var defined = 0;
        foreach (var (kind, left, right) in CSharpOperatorOracle.Universe()) {
            if (!CSharpOperatorOracle.Numeric.Contains(left) || (right is not null && !CSharpOperatorOracle.Numeric.Contains(right)))
                continue;
            var oracle = CSharpOperatorOracle.Answer(kind, left, right);
            defined += oracle.Defined ? 1 : 0;
            var resolution = Clr(left).GetOperator(kind, right is null ? null : Clr(right));
            var actual = resolution?.Result.GetRuntimeType();
            if (actual != oracle.Type)
                mismatches.Add($"{CSharpOperatorOracle.Key(kind, left, right)}: C# {(oracle.Defined ? CSharpOperatorOracle.Keyword(oracle.Type!) : oracle.Errors)}, provider {(actual is null ? "none" : CSharpOperatorOracle.Keyword(actual))}");
        }
        await Assert.That(mismatches).IsEmpty();
        await Assert.That(defined).IsEqualTo(DefinedNonLiftedCells);
    }

    /// <summary>The oracle's own count, so an empty mismatch list cannot pass by finding no cell.</summary>
    private const int DefinedNonLiftedCells = 553;

    // ---- the grid (amendment 8) ----

    /// <summary>
    /// Cells refused under the fork's two conditions (wrong or throwing on 0d2cd4a8^1 too, and past the VM budget),
    /// each with its positioned message. The PR lists them.
    /// </summary>
    internal static readonly Dictionary<string, string> Refused = new(StringComparer.Ordinal);

    /// <summary>No defined native-integer cell computes a wrong value or throws: each runs right, keeps the right word under a stated type T-17 corrects, or is a listed refusal.</summary>
    [Test]
    public async Task Grid_EveryDefinedCell_RunsRight_OrIsAListedRefusal() {
        await Assert.That(IntPtr.Size).IsEqualTo(8);
        var cells = NativeIntegerGrid.Cells.Value;
        var problems = cells
            .Where(static c => c.Class.StartsWith("defined:", StringComparison.Ordinal))
            .Where(static c => c.Class is not ("defined: runs right" or "defined: right word, stated type differs"))
            .Where(c => !(c.Class == "defined: rejected" && Refused.TryGetValue(c.Key, out var message) && c.Detail == message))
            .Select(static c => c.Line)
            .ToArray();
        await Assert.That(problems).IsEmpty();
        await Assert.That(cells.Count).IsEqualTo(3336);
        await Assert.That(cells.Count(static c => c.Class == "defined: runs right")).IsEqualTo(RunsRight);
    }

    private const int RunsRight = 2174;

    /// <summary>
    /// The cells whose stated result type is not C#'s, with the right word. T-17 typed the 60 arithmetic ones as C#'s; the 24 that
    /// remain are bitwise cells of a small integer or char beside a native integer, typed as the left operand (T-22: required
    /// bitwise operands keep the left type). Pinned by count and key checksum.
    /// </summary>
    [Test]
    public async Task Grid_StatedTypeDiffers_IsT17s_PinnedByCountAndChecksum() {
        var cells = NativeIntegerGrid.Cells.Value.Where(static c => c.Class == "defined: right word, stated type differs").ToArray();
        await Assert.That((cells.Length, Poly.Tests.DomainModeling.Analysis.LedgerCheck.Checksum(cells.Select(static c => c.Key)))).IsEqualTo((24, "361d663ed250c4c0"));
    }

    /// <summary>Amendment 7: every cell that ran right on 0d2cd4a8^1 runs right now.</summary>
    [Test]
    public async Task Grid_EveryCellThatRanRightBeforeT18a_RunsRightNow() {
        var now = NativeIntegerGrid.Cells.Value.ToDictionary(static c => c.Key, StringComparer.Ordinal);
        var lost = PreT18a()
            .Where(static c => c.Class == "defined: runs right")
            .Where(c => now[c.Key].Class != "defined: runs right")
            .Select(c => $"{now[c.Key].Line}  (before T-18a: {c.Detail})")
            .ToArray();
        await Assert.That(lost).IsEmpty();
    }

    /// <summary>An undefined arithmetic or comparison cell is rejected; only bitwise, shift and unary-minus cells may be accepted (T-22).</summary>
    [Test]
    public async Task Grid_UndefinedArithmeticAndComparison_AreRejected() {
        var accepted = NativeIntegerGrid.Cells.Value
            .Where(static c => c.Class.StartsWith("undefined:", StringComparison.Ordinal) && c.Class != "undefined: rejected")
            .Where(static c => c.Kind is not (OperatorKind.BitwiseAnd or OperatorKind.BitwiseOr or OperatorKind.BitwiseXor
                or OperatorKind.ShiftLeft or OperatorKind.ShiftRight or OperatorKind.UnaryMinus or OperatorKind.BitwiseNot))
            .Select(static c => c.Line)
            .ToArray();
        await Assert.That(accepted).IsEmpty();
    }

    // ---- constants (amendment 2) and the writer ----

    /// <summary>
    /// Constant folding's cell table excludes IntPtr and UIntPtr: native constants are not folded, and the VM computes
    /// them, for every one of the grid's 16 binary kinds (a shift's count is an int constant) and for both native types.
    /// </summary>
    [Test]
    public async Task NativeConstants_DeclineToFold_AndRun() {
        await Assert.That(IntPtr.Size).IsEqualTo(8);
        var swept = 0;
        foreach (var native in CSharpOperatorOracle.Native) {
            foreach (var kind in NativeIntegerGrid.Kinds) {
                var shift = kind is OperatorKind.ShiftLeft or OperatorKind.ShiftRight;
                object left = native == typeof(nint) ? (nint)13 : (nuint)13;
                object right = shift ? 2 : native == typeof(nint) ? (nint)4 : (nuint)4;
                var node = NativeIntegerGrid.Build(kind, new Constant(left), new Constant(right));
                var analysis = Interpreter.Analyze(node);
                await Assert.That(analysis.Diagnostics.Where(static d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error).Select(static d => d.Message).ToArray()).IsEmpty().Because(kind.ToString());
                await Assert.That(analysis.GetNodeReplacement(node)).IsNull().Because($"{native.Name} {kind} is not folded");
                var program = Interpreter.Compile(node, analysis);
                using var exec = Interpreter.Execute(program);
                var expected = CSharpOperatorOracle.Value(kind, native, shift ? typeof(int) : native, left, right).Value;
                await Assert.That(NativeIntegerGrid.Format(NativeIntegerGrid.Read(exec, program.RootClrType))).IsEqualTo(NativeIntegerGrid.Format(expected)).Because($"{native.Name} {kind}");
                swept++;
            }
        }
        await Assert.That(swept).IsEqualTo(32);
    }

    /// <summary>The writer prints the C# keyword for a native-integer type reference, never the enum member name.</summary>
    [Test]
    public async Task Writer_PrintsTheNativeKeyword() {
        var primitive = typeof(nint).GetPrimitiveType();
        await Assert.That(primitive).IsNotNull();
        var printed = new Poly.Interpretation.CSharp.CSharpWriter().Write(new TypeCast(new Variable("x"), new PrimitiveTypeReference(primitive!.Value)));
        await Assert.That(printed).Contains("(nint)");
        await Assert.That(printed).DoesNotContain("NInt");
        await Assert.That(typeof(nuint).GetPrimitiveType()?.GetCSharpKeyword()).IsEqualTo("nuint");
    }

    // ---- fix round 1: casts, assignments and constants outside operators (review F1, F4) ----

    /// <summary>
    /// The probe the review asked for, on three trees (0d2cd4a8^1 = 77704337, master bea9bb3a, and the head before this
    /// fix): <c>(nint)int</c>, <c>(nuint)uint</c> and <c>nuint x; x + 1</c> ran right on 77704337 and were rejected on the
    /// head; <c>(long)nint</c> and <c>(ulong)nuint</c> ran right on all three. These cells are the bar: each runs right.
    /// </summary>
    [Test]
    [Arguments(typeof(int), typeof(nint))]
    [Arguments(typeof(uint), typeof(nuint))]
    [Arguments(typeof(nint), typeof(long))]
    [Arguments(typeof(nuint), typeof(ulong))]
    public async Task CastThatRanBeforeT18a_RunsRightNow(Type from, Type to) {
        var cell = NativeConversionGrid.EvaluateCast(from, to);
        await Assert.That(cell.Line).IsEqualTo($"{cell.Key} | runs right | {cell.Detail}");
    }

    /// <summary>Every explicit cast between a native integer and a numeric type (50 pairs), against C#'s <c>(to)a</c>, unchecked.</summary>
    [Test]
    public async Task Casts_EveryNativeCast_AgreesWithCSharp() {
        await Assert.That(IntPtr.Size).IsEqualTo(8);
        var cells = CSharpOperatorOracle.ConversionUniverse().Select(static p => NativeConversionGrid.EvaluateCast(p.From, p.To)).ToArray();
        await Assert.That(cells.Where(static c => c.Class != "runs right").Select(static c => c.Line).ToArray()).IsEmpty();
        await Assert.That(cells.Length).IsEqualTo(50);
    }

    /// <summary>
    /// An assignment converts implicitly or not at all: each of C#'s 18 implicit native conversions assigns into a
    /// variable whose prior type is the target and runs with C#'s value; each of the 32 explicit-only pairs is rejected.
    /// </summary>
    [Test]
    public async Task Assignments_ImplicitNativeConversions_Run_ExplicitOnesAreRejected() {
        await Assert.That(IntPtr.Size).IsEqualTo(8);
        var cells = CSharpOperatorOracle.ConversionUniverse().Select(static p => NativeConversionGrid.EvaluateAssign(p.From, p.To)).ToArray();
        var wrong = cells.Where(c => c.Class != (CSharpOperatorOracle.Conversion(c.From, c.To) is ConversionOperatorKind.Implicit ? "runs right" : "rejected"))
            .Select(static c => c.Line).ToArray();
        await Assert.That(wrong).IsEmpty();
        await Assert.That(cells.Count(static c => c.Class == "runs right")).IsEqualTo(18);
    }

    /// <summary>An <c>int</c> (and a <c>uint</c>) assigned to a native-integer lambda parameter, as C# assigns it.</summary>
    [Test]
    [Arguments(typeof(int), typeof(nint))]
    [Arguments(typeof(uint), typeof(nuint))]
    [Arguments(typeof(nint), typeof(long))]
    public async Task AssignToANativeParameter_RunsAsCSharp(Type from, Type to) {
        var body = new Block([new Assignment(new Variable("p"), Param("i", from)), new Variable("p")], []);
        var seed = Activator.CreateInstance(to)!;
        var node = new Invoke(new Lambda([Param("p", to)], body), new Constant(seed));
        var analysis = Interpreter.Analyze(node);
        await Assert.That(analysis.Diagnostics.Where(static d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error).Select(static d => d.Message).ToArray()).IsEmpty();
        var program = Interpreter.Compile(node, analysis);
        foreach (var value in new[] { NativeIntegerGrid.Values[from].Stress, NativeIntegerGrid.Values[from].Plain }) {
            using var exec = Interpreter.Execute(program, value);
            await Assert.That(NativeIntegerGrid.Format(NativeIntegerGrid.Read(exec, program.RootClrType)))
                .IsEqualTo(NativeIntegerGrid.Format(CSharpOperatorOracle.ConversionValue(from, to, value, implicitOnly: true).Value));
        }
    }

    /// <summary>
    /// A signed integral constant beside a native form (704 cells: 11 kinds, 4 native forms, 1 and -1 of sbyte, short,
    /// int and long, either side): the verdict is C#'s (10.2.11 lets a non-negative constant meet <c>nuint</c>, as it meets
    /// <c>ulong</c>), and a cell C# defines runs with its value.
    /// </summary>
    [Test]
    public async Task Constants_BesideANativeForm_AreJudgedAndRunAsCSharp() {
        await Assert.That(IntPtr.Size).IsEqualTo(8);
        var cells = CSharpOperatorOracle.ConstantUniverse().Select(static c => NativeConversionGrid.EvaluateConstant(c.Kind, c.Operand, c.Constant, c.ConstantLeft)).ToArray();
        await Assert.That(cells.Where(static c => c.Class is not ("runs right" or "right word, stated type differs" or "undefined: rejected")).Select(static c => c.Line).ToArray()).IsEmpty();
        await Assert.That(cells.Length).IsEqualTo(704);
        await Assert.That(cells.Count(static c => c.Class == "runs right")).IsEqualTo(DefinedConstantCells);
        // T-17 typed 78 of the 104 right-word cells as C#'s type. The 26 that remain are a `long` constant beside a native form: nuint beside it is judged by ConstantOperandRule as nuint where C# says ulong (T-26), and `x * 1L`, `x / 1L`, `x % 1L` fold to x typed as x (identity fold keeps the operand's type; found, not fixed).
        var stated = cells.Where(static c => c.Class == "right word, stated type differs").Select(static c => c.Key).ToArray();
        await Assert.That((stated.Length, Poly.Tests.DomainModeling.Analysis.LedgerCheck.Checksum(stated))).IsEqualTo(StatedTypeConstantCells);
    }

    private const int DefinedConstantCells = 414;

    private static readonly (int, string) StatedTypeConstantCells = (26, "ec6c6896f8c181cd");

    // ---- F2, F3, F5 ----

    /// <summary>A type-definition node's native-integer property resolves to the native type, not object.</summary>
    [Test]
    public async Task TypeDefinitionNode_NativeIntegerProperty_ResolvesToTheNativeType() {
        var holder = new TypeDefinitionNode("NativeHolder",
            Properties: [
                new PropertyDefinitionNode("Signed", new PrimitiveTypeReference(typeof(nint).GetPrimitiveType()!.Value), Getter: new PropertyGetterDefinitionNode()),
                new PropertyDefinitionNode("Unsigned", new PrimitiveTypeReference(typeof(nuint).GetPrimitiveType()!.Value), Getter: new PropertyGetterDefinitionNode())
            ]);
        var context = Poly.Analysis.AnalysisContext.CreateDefault();
        new Poly.Interpretation.Analysis.Semantics.TypeDefinitionNodeAnalyzer().Analyze(context, holder);
        var properties = context.TypeDefinitions.GetTypeDefinition("NativeHolder")!.Properties.ToDictionary(static p => p.Name, static p => p.MemberTypeDefinition.GetRuntimeType());
        await Assert.That(properties["Signed"]).IsEqualTo(typeof(nint));
        await Assert.That(properties["Unsigned"]).IsEqualTo(typeof(nuint));
    }

    /// <summary>A native-integer result reads through GetValue as the native type.</summary>
    [Test]
    public async Task GetValue_OfANativeResult_IsTheNativeValue() {
        using (var signed = Interpreter.Execute(Interpreter.Compile(new Add(Param("a", typeof(nint)), Param("b", typeof(nint)))), (nint)(-7), (nint)2))
            await Assert.That(signed.GetValue<nint>()).IsEqualTo((nint)(-5));
        var high = unchecked((nuint)0x8000_0000_0000_0005UL);
        using (var unsigned = Interpreter.Execute(Interpreter.Compile(new Add(Param("a", typeof(nuint)), Param("b", typeof(nuint)))), high, (nuint)2))
            await Assert.That(unsigned.GetValue<nuint>()).IsEqualTo(high + 2);
    }

    /// <summary>
    /// The edge cases C# throws on, with the exception C# throws: <c>MinValue / -1</c> and <c>MinValue % -1</c>, a zero
    /// divisor, and the unchecked <c>-MinValue</c>, for <c>nint</c>, <c>nint?</c> and (a zero divisor) <c>nuint</c>.
    /// </summary>
    [Test]
    [Arguments(OperatorKind.Divide, typeof(nint), typeof(nint), "min", "-1")]
    [Arguments(OperatorKind.Modulo, typeof(nint), typeof(nint), "min", "-1")]
    [Arguments(OperatorKind.Divide, typeof(nint), typeof(nint), "min", "0")]
    [Arguments(OperatorKind.Modulo, typeof(nint), typeof(nint), "min", "0")]
    [Arguments(OperatorKind.Divide, typeof(nint?), typeof(nint), "min", "-1")]
    [Arguments(OperatorKind.Modulo, typeof(nint), typeof(nint?), "min", "-1")]
    [Arguments(OperatorKind.Divide, typeof(nuint), typeof(nuint), "min", "0")]
    [Arguments(OperatorKind.Modulo, typeof(nuint), typeof(nuint), "min", "0")]
    [Arguments(OperatorKind.Divide, typeof(nint), typeof(int), "min", "-1")]
    [Arguments(OperatorKind.UnaryMinus, typeof(nint), null, "min", null)]
    [Arguments(OperatorKind.UnaryMinus, typeof(nint?), null, "min", null)]
    public async Task EdgeValues_ThrowOrWrapAsCSharpDoes(OperatorKind kind, Type left, Type? right, string leftValue, string? rightValue) {
        await Assert.That(IntPtr.Size).IsEqualTo(8);
        static object? Value(Type type, string text) {
            var underlying = Nullable.GetUnderlyingType(type) ?? type;
            return text == "min"
                ? underlying == typeof(nint) ? nint.MinValue : unchecked((nuint)0x8000_0000_0000_0000UL)
                : underlying == typeof(nint) ? (nint)long.Parse(text, CultureInfo.InvariantCulture)
                : underlying == typeof(nuint) ? (nuint)ulong.Parse(text, CultureInfo.InvariantCulture)
                : int.Parse(text, CultureInfo.InvariantCulture);
        }
        var l = Value(left, leftValue);
        var r = right is null ? null : Value(right, rightValue!);
        var expected = CSharpOperatorOracle.Value(kind, left, right, l, r);
        var node = NativeIntegerGrid.Build(kind, Param("a", left), right is null ? null : Param("b", right));
        var program = Interpreter.Compile(node);
        string actual;
        try {
            using var exec = Interpreter.Execute(program, right is null ? new object?[] { l } : new object?[] { l, r });
            actual = NativeIntegerGrid.Format(NativeIntegerGrid.Read(exec, program.RootClrType));
        }
        catch (Exception ex) {
            actual = $"throws {ex.GetType().Name}";
        }
        await Assert.That(actual).IsEqualTo(expected.Throws is { } thrown ? $"throws {thrown}" : NativeIntegerGrid.Format(expected.Value));
    }
}

/// <summary>Casts, assignments and constant operands over native integers, each against <see cref="CSharpOperatorOracle"/>.</summary>
internal static class NativeConversionGrid {
    public sealed record Cell(string Key, Type From, Type To, string Class, string Detail) {
        public string Line => $"{Key} | {Class} | {Detail}";
    }

    private static Parameter Param(string name, Type type) => new(name, new ClrTypeReference(type));

    private static string Errors(AnalysisResult analysis) =>
        string.Join("; ", analysis.Diagnostics.Where(static d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error).Select(static d => d.Message));

    /// <summary>Runs <paramref name="node"/> on each argument set and compares with C#'s answer for it.</summary>
    private static (string Class, string Detail) Run(Node node, IEnumerable<(object?[] Args, string Expected)> runs, Type? statedType) {
        AnalysisResult analysis;
        VmProgram program;
        try {
            analysis = Interpreter.Analyze(node, ClrTypeDefinitionRegistry.Shared);
            if (Errors(analysis) is { Length: > 0 } errors)
                return ("rejected", errors);
            program = Interpreter.Compile(node, analysis);
        }
        catch (Exception ex) {
            return ("throws", $"{ex.GetType().Name}: {ex.Message}");
        }
        var detail = new List<string>();
        var typeDiffers = statedType is not null && program.RootClrType != statedType;
        var (right, word) = (true, true);
        foreach (var (args, expected) in runs) {
            string actual;
            string? asStated = null;
            try {
                using var exec = Interpreter.Execute(program, args);
                actual = NativeIntegerGrid.Format(NativeIntegerGrid.Read(exec, program.RootClrType));
                if (typeDiffers)
                    asStated = NativeIntegerGrid.Format(NativeIntegerGrid.Read(exec, statedType));
            }
            catch (Exception ex) {
                actual = $"throws {ex.GetType().Name}";
            }
            right &= actual == expected;
            word &= actual == expected || asStated == expected;
            detail.Add(actual == expected ? actual : $"VM {actual}, C# {expected}");
        }
        // As in the operator grid: the word is C#'s value under C#'s type, and the stated type is the lowered analysis's (T-17).
        var cls = !word ? "wrong" : typeDiffers ? "right word, stated type differs" : right ? "runs right" : "wrong";
        return (cls, $"type {program.RootClrType?.Name}; {string.Join("; ", detail)}");
    }

    private static string Expected(CSharpOperatorOracle.ValueAnswer answer) =>
        answer.Throws is { } thrown ? $"throws {thrown}" : NativeIntegerGrid.Format(answer.Value);

    private static object[] ValuesOf(Type type) => [NativeIntegerGrid.Values[type].Stress, NativeIntegerGrid.Values[type].Plain];

    public static Cell EvaluateCast(Type from, Type to) {
        var node = new TypeCast(Param("a", from), new ClrTypeReference(to));
        var (cls, detail) = Run(node, ValuesOf(from).Select(v => (new object?[] { v }, Expected(CSharpOperatorOracle.ConversionValue(from, to, v, implicitOnly: false)))), to);
        return new($"({CSharpOperatorOracle.Keyword(to)}){CSharpOperatorOracle.Keyword(from)}", from, to, cls, detail);
    }

    /// <summary><c>v = seed; v = a; v</c>: the first write gives <c>v</c> the target type, the second converts.</summary>
    public static Cell EvaluateAssign(Type from, Type to) {
        var v = new Variable("v");
        var node = new Block([new Assignment(v, Param("seed", to)), new Assignment(v, Param("a", from)), v], [v]);
        var seed = NativeIntegerGrid.Values[to].Plain;
        var implicitly = CSharpOperatorOracle.Conversion(from, to) is ConversionOperatorKind.Implicit;
        var (cls, detail) = Run(node, ValuesOf(from).Select(a => (new object?[] { seed, a },
            implicitly ? Expected(CSharpOperatorOracle.ConversionValue(from, to, a, implicitOnly: true)) : "C# rejects")), to);
        return new($"{CSharpOperatorOracle.Keyword(to)} v = {CSharpOperatorOracle.Keyword(from)}", from, to, cls, detail);
    }

    public static Cell EvaluateConstant(OperatorKind kind, Type operand, object constant, bool constantLeft) {
        var answer = CSharpOperatorOracle.ConstantAnswer(kind, operand, constant, constantLeft);
        var node = constantLeft
            ? NativeIntegerGrid.Build(kind, new Constant(constant), Param("x", operand))
            : NativeIntegerGrid.Build(kind, Param("x", operand), new Constant(constant));
        var underlying = Nullable.GetUnderlyingType(operand) ?? operand;
        var values = ValuesOf(underlying).Cast<object?>().Concat(Nullable.GetUnderlyingType(operand) is null ? [] : new object?[] { null });
        var key = CSharpOperatorOracle.ConstantKey(kind, operand, constant, constantLeft);
        if (!answer.Defined) {
            var (verdict, why) = Run(node, [], null);
            return new(key, operand, operand, verdict == "rejected" ? "undefined: rejected" : $"undefined: {verdict}", why);
        }
        var (cls, detail) = Run(node, values.Select(x => (new object?[] { x }, Expected(Invoke(answer.Method!, x)))), answer.Type);
        return new(key, operand, operand, cls, detail);
    }

    private static CSharpOperatorOracle.ValueAnswer Invoke(MethodInfo method, object? argument) {
        try {
            return new(method.Invoke(null, [argument]), null);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is { } inner) {
            return new(null, inner.GetType().Name);
        }
    }
}