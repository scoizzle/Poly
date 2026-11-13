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

    private static readonly Lazy<Dictionary<(OperatorKind, Type, Type?), CellAnswer>> Compiled = new(Compile);

    public static TimeSpan CompileTime { get; private set; }

    /// <summary>The answer for one cell; a cell outside <see cref="Universe"/> is an error, never a default.</summary>
    public static CellAnswer Answer(OperatorKind kind, Type left, Type? right) =>
        Compiled.Value.TryGetValue((kind, left, right), out var answer)
            ? answer
            : throw new ArgumentOutOfRangeException(nameof(left), $"{Key(kind, left, right)} is not an oracle cell");

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

    private static Dictionary<(OperatorKind, Type, Type?), CellAnswer> Compile() {
        if (IntPtr.Size != 8)
            throw new PlatformNotSupportedException("the native-integer oracle runs on a 64-bit host only");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var cells = Universe().ToArray();
        const int header = 2;
        string Source(Func<int, bool> fails) {
            var text = new StringBuilder("#nullable disable\npublic static class Cells {\n");
            for (var i = 0; i < cells.Length; i++)
                text.Append(Method(i, cells[i].Kind, cells[i].Left, cells[i].Right, fails(i))).Append('\n');
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
            if (index < 0 || index >= cells.Length)
                throw new InvalidOperationException($"oracle diagnostic outside a cell: {diagnostic}");
            (errors.TryGetValue(index, out var list) ? list : errors[index] = []).Add(diagnostic.Id);
        }
        var model = compilation.GetSemanticModel(tree);
        var bodies = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
        if (bodies.Length != cells.Length)
            throw new InvalidOperationException($"oracle parsed {bodies.Length} methods for {cells.Length} cells");

        var emitTree = CSharpSyntaxTree.ParseText(Source(errors.ContainsKey), parse);
        using var pe = new MemoryStream();
        var emit = CSharpCompilation.Create("NativeIntegerOracleRun", [emitTree], references, options).Emit(pe);
        if (!emit.Success)
            throw new InvalidOperationException("oracle emit failed: " + string.Join("\n", emit.Diagnostics.Where(static d => d.Severity == RoslynSeverity.Error).Take(5)));
        pe.Position = 0;
        var type = new AssemblyLoadContext("NativeIntegerOracleRun", isCollectible: true).LoadFromStream(pe).GetType("Cells")!;

        var answers = new Dictionary<(OperatorKind, Type, Type?), CellAnswer>();
        for (var i = 0; i < cells.Length; i++) {
            var (kind, left, right) = cells[i];
            if (errors.TryGetValue(i, out var ids)) {
                answers[(kind, left, right)] = new(false, null, string.Join(",", ids.Distinct()), null);
                continue;
            }
            var expression = bodies[i].ExpressionBody!.Expression;
            var symbol = model.GetTypeInfo(expression).Type
                ?? throw new InvalidOperationException($"oracle has no type for {Key(kind, left, right)}");
            answers[(kind, left, right)] = new(true, ToClr(symbol), "", type.GetMethod($"M{i}")!);
        }
        watch.Stop();
        CompileTime = watch.Elapsed;
        return answers;
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
        if (value is not long word || root is null)
            return value;
        return root switch {
            _ when root == typeof(bool) => word != 0,
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

    /// <summary>
    /// T-18a's four regressions, on master: each ran before T-18a and is rejected now, with this message. Before T-18a
    /// <c>nuint &lt; nuint</c> ran but read a <c>nuint</c> at or above 2^63 as negative.
    /// </summary>
    [Test]
    [Arguments(OperatorKind.LessThan, typeof(nint), typeof(nint), "comparison between incompatible types 'IntPtr' and 'IntPtr': no LessThan operator for these types", "defined: runs right")]
    [Arguments(OperatorKind.Add, typeof(nint), typeof(nint), "arithmetic operand is not numeric (got 'IntPtr' and 'IntPtr'): no Add operator for these types", "defined: runs right")]
    [Arguments(OperatorKind.Add, typeof(nint), typeof(int), "arithmetic operand is not numeric (got 'IntPtr' and 'Int32')", "defined: runs right")]
    [Arguments(OperatorKind.LessThan, typeof(nuint), typeof(nuint), "comparison between incompatible types 'UIntPtr' and 'UIntPtr': no LessThan operator for these types", "defined: wrong value")]
    public async Task RegressedByT18a_IsRejectedOnMaster(OperatorKind kind, Type left, Type right, string message, string before) {
        var node = NativeIntegerGrid.Build(kind, Param("a", left), Param("b", right));
        var errors = Interpreter.Analyze(node).Diagnostics.Where(static d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error).Select(static d => d.Message).ToArray();
        await Assert.That(errors).IsEquivalentTo([message]);
        await Assert.That(PreT18a().Single(c => c.Key == CSharpOperatorOracle.Key(kind, left, right)).Class).IsEqualTo(before);
    }

    /// <summary>On master the provider reaches IntPtr through its reflected members only: nint + int is the declared op_Addition(IntPtr, Int32), and nint to long is IntPtr's explicit op_Explicit.</summary>
    [Test]
    public async Task Master_NintPlusInt_IsIntPtrsDeclaredOperator_AndNintToLongIsExplicit() {
        var resolution = Clr(typeof(nint)).GetOperator(OperatorKind.Add, Clr(typeof(int)));
        await Assert.That(resolution is { PlatformApplied: false }).IsTrue();
        await Assert.That(resolution!.Value.Signature.Name).IsEqualTo("op_Addition");
        var toLong = Clr(typeof(long)).ResolveConversionFrom(Clr(typeof(nint)));
        await Assert.That(toLong?.Kind).IsEqualTo(ConversionOperatorKind.Explicit);
        await Assert.That(toLong!.Value.Method).IsNotNull();
        await Assert.That(((ITypeDefinition)Clr(typeof(nint))).PrimitiveType).IsNull();
    }
}