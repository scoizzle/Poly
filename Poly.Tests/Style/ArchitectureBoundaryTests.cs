using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Text.RegularExpressions;

namespace Poly.Tests.Style;

/// <summary>
/// I8: the layers that state meaning do not name execution, reflection or CLR type facts. <c>Vm/**</c> is the host that
/// compiles to <c>System.Linq.Expressions</c>, and <c>CommonLanguageRuntime/**</c> is the host that reflects and names
/// runtime types; everything that states meaning asks the platform instead.
///
/// Two checks:
/// <list type="bullet">
/// <item>a namespace ban (<c>System.Linq.Expressions</c>, <c>System.Reflection</c>), matched as a substring so a
/// qualified name or an alias is caught;</item>
/// <item>a per-file, per-pattern budget for CLR type facts (<c>typeof(</c>, <c>RuntimeType</c>, <c>GetRuntimeType</c>,
/// <c>ClrTypeReference</c>, <c>Nullable.GetUnderlyingType</c>, and the <c>IsEnum</c>/<c>IsPrimitive</c>/<c>IsValueType</c>
/// predicates). The count must equal the budget exactly, so a reduction must lower the number here in the same change
/// and the budget only ever shrinks. Every entry names its removing issue.</item>
/// </list>
/// The scan strips comments and string literals first, so documentation and messages do not count. Known blind spot,
/// named in #972: bare <c>Type</c> parameters and returns are not matched yet.
/// </summary>
public class ArchitectureBoundaryTests {
    private static readonly string[] ScannedRoots = [
        "Poly/Ast",
        "Poly/Introspection",
        "Poly/Interpretation/Analysis"
    ];

    private static readonly string[] ForbiddenNamespaces = [
        "System.Linq.Expressions",
        "System.Reflection"
    ];

    /// <summary>Type-fact patterns the meaning layers may not add to. Word boundaries, so <c>RuntimeType</c> does not
    /// also match inside <c>GetRuntimeType</c>; <c>GetRuntimeType</c> counts <c>TryGetRuntimeType</c> too.</summary>
    private static readonly (string Name, Regex Pattern)[] TypeFacts = [
        ("typeof(", new Regex(@"\btypeof\s*\(", RegexOptions.Compiled)),
        ("GetRuntimeType", new Regex(@"\b(?:Try)?GetRuntimeType\b", RegexOptions.Compiled)),
        ("RuntimeType", new Regex(@"\bRuntimeType\b", RegexOptions.Compiled)),
        ("ClrTypeReference", new Regex(@"\bClrTypeReference\b", RegexOptions.Compiled)),
        ("Nullable.GetUnderlyingType", new Regex(@"\bNullable\.GetUnderlyingType\b", RegexOptions.Compiled)),
        ("CLR type predicate", new Regex(@"\.IsEnum\b|\.IsPrimitive\b|\.IsValueType\b", RegexOptions.Compiled))
    ];

    /// <summary>Analysis files that still name a reflection/expression fact, each with the row that removes it.</summary>
    private static readonly Dictionary<string, string> AllowedNamespaces = new(StringComparer.Ordinal) {
        ["Poly/Interpretation/Analysis/ConstantFolding/ConstantFoldingPass.cs"] =
            "invokes a declared conversion at analysis time (#954); removing row: one shared evaluator (I9, #972)",
        ["Poly/Interpretation/Analysis/Semantics/CallSiteCatalogPass.cs"] =
            "reads a delegate's parameter shape for the catalog; removing row: I9 moves the fence to the host (#972)",
        ["Poly/Interpretation/Analysis/Semantics/AwaitPattern.cs"] =
            "reads awaiter members by reflection; removing row: I9 moves the fence to the host (#972)"
    };

    /// <summary>Per-file, per-pattern budget. The measured count must equal the budget: a reduction lowers the number
    /// here in the same change, so the budget only shrinks.</summary>
    private static readonly Dictionary<string, (string RemovingRow, Dictionary<string, int> Max)> Budget = new(StringComparer.Ordinal) {
        ["Poly/Interpretation/Analysis/ConstantFolding/ConstantFoldingPass.cs"] = ("analysis-time invocation and CLR primitives (#954); removing row: I9 (#972)", new(StringComparer.Ordinal) { ["CLR type predicate"] = 1, ["GetRuntimeType"] = 4, ["typeof("] = 66 }),
        ["Poly/Interpretation/Analysis/Semantics/AvailableExpressionPass.cs"] = ("reads an array's runtime type; removing row: #999", new(StringComparer.Ordinal) { ["GetRuntimeType"] = 1 }),
        ["Poly/Interpretation/Analysis/Semantics/AwaitPattern.cs"] = ("awaiter reflection and CLR members; removing row: I9 (#972)", new(StringComparer.Ordinal) { ["GetRuntimeType"] = 1, ["typeof("] = 7 }),
        ["Poly/Interpretation/Analysis/Semantics/ClrTypeResolution.cs"] = ("resolves a CLR type reference; removing row: #999", new(StringComparer.Ordinal) { ["GetRuntimeType"] = 1 }),
        ["Poly/Interpretation/Analysis/Semantics/ConstantOperandRule.cs"] = ("the ulong and nuint operand rule, a numeric category stated twice; removing row: #954", new(StringComparer.Ordinal) { ["typeof("] = 4 }),
        ["Poly/Interpretation/Analysis/Semantics/IteratorLegalityAnalyzer.cs"] = ("CLR type facts; removing row: #999", new(StringComparer.Ordinal) { ["GetRuntimeType"] = 1, ["typeof("] = 2 }),
        ["Poly/Interpretation/Analysis/Semantics/LambdaReturnTypeAnalyzer.cs"] = ("CLR type facts; removing row: #999", new(StringComparer.Ordinal) { ["GetRuntimeType"] = 1, ["typeof("] = 1 }),
        ["Poly/Interpretation/Analysis/Semantics/SyntaxTypeCompatibilityAnalyzer.cs"] = ("the CLR category table and its type references (T-28); removing row: #954", new(StringComparer.Ordinal) { ["CLR type predicate"] = 5, ["GetRuntimeType"] = 22, ["Nullable.GetUnderlyingType"] = 8, ["RuntimeType"] = 3, ["typeof("] = 55 }),
        ["Poly/Interpretation/Analysis/Semantics/TypeAndMemberResolutionPass.cs"] = ("CLR type statements the pass still makes; removing row: #999", new(StringComparer.Ordinal) { ["CLR type predicate"] = 1, ["GetRuntimeType"] = 17, ["Nullable.GetUnderlyingType"] = 5, ["RuntimeType"] = 1, ["typeof("] = 31 }),
        ["Poly/Interpretation/Analysis/Semantics/TypeDefinitionNodeAnalyzer.cs"] = ("CLR type facts; removing row: #999", new(StringComparer.Ordinal) { ["CLR type predicate"] = 1, ["GetRuntimeType"] = 1, ["Nullable.GetUnderlyingType"] = 1, ["RuntimeType"] = 5, ["typeof("] = 10 }),
    };

    [Test]
    public async Task MeaningLayers_NameNoExecutionReflectionOrClrTypeFact() {
        var repo = GetRepoRoot();
        var namespaceOffenders = new List<string>();
        var seenAllowed = new HashSet<string>(StringComparer.Ordinal);
        var measured = new SortedDictionary<string, SortedDictionary<string, int>>(StringComparer.Ordinal);

        foreach (var relativeRoot in ScannedRoots) {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(repo, relativeRoot), "*.cs", SearchOption.AllDirectories)) {
                var relative = Path.GetRelativePath(repo, file).Replace('\\', '/');
                if (relative.Contains("/obj/") || relative.EndsWith(".g.cs", StringComparison.Ordinal))
                    continue;
                // The host folder is where the CLR keeps its own facts.
                if (relative.StartsWith("Poly/Introspection/CommonLanguageRuntime/", StringComparison.Ordinal))
                    continue;

                var text = await File.ReadAllTextAsync(file);
                if (ForbiddenNamespaces.Any(text.Contains)) {
                    if (AllowedNamespaces.ContainsKey(relative))
                        seenAllowed.Add(relative);
                    else
                        namespaceOffenders.Add(relative);
                }

                var code = StripCommentsAndStrings(text);
                foreach (var (name, pattern) in TypeFacts) {
                    var count = pattern.Matches(code).Count;
                    if (count == 0)
                        continue;
                    if (!measured.TryGetValue(relative, out var perFile))
                        measured[relative] = perFile = new SortedDictionary<string, int>(StringComparer.Ordinal);
                    perFile[name] = count;
                }
            }
        }

        await Assert.That(string.Join(", ", namespaceOffenders)).IsEmpty()
            .Because($"allowed exceptions, each with its removing row: {string.Join("; ", AllowedNamespaces.Select(entry => $"{entry.Key}: {entry.Value}"))}");
        await Assert.That(string.Join("", AllowedNamespaces.Keys.Except(seenAllowed))).IsEmpty()
            .Because("an allowlist entry that no longer offends must go with its row");

        var drift = new List<string>();
        foreach (var (file, perFile) in measured) {
            foreach (var (pattern, count) in perFile) {
                if (!Budget.TryGetValue(file, out var entry) || !entry.Max.TryGetValue(pattern, out var budgeted)) {
                    drift.Add($"{file} [{pattern}]: {count}, not in the budget");
                    continue;
                }
                if (count < budgeted)
                    drift.Add($"{file} [{pattern}]: {count} is below its budget {budgeted}; lower it");
                else if (count > budgeted)
                    drift.Add($"{file} [{pattern}]: {count} is over its budget {budgeted} ({entry.RemovingRow})");
            }
        }
        foreach (var (file, entry) in Budget) {
            foreach (var (pattern, budgeted) in entry.Max) {
                if (!measured.TryGetValue(file, out var actual) || !actual.ContainsKey(pattern))
                    drift.Add($"{file} [{pattern}]: budget {budgeted} but none measured; remove the entry");
            }
        }

        await Assert.That(string.Join("; ", drift)).IsEmpty()
            .Because("the budget is exact: a count may only go down by editing this file in the same change");
    }

    [Test]
    public async Task Introspection_NamesNoAst() {
        // #998 T13 (owner, 2026-11-19): Introspection is beneath the AST. A tree reference to a CLR type is the C#
        // surface's, and the declarations' node lookup is the analysis'; neither lives here.
        var repo = GetRepoRoot();
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(repo, "Poly/Introspection"), "*.cs", SearchOption.AllDirectories)) {
            var relative = Path.GetRelativePath(repo, file).Replace('\\', '/');
            if (relative.Contains("/obj/") || relative.EndsWith(".g.cs", StringComparison.Ordinal))
                continue;
            if (Regex.IsMatch(StripCommentsAndStrings(await File.ReadAllTextAsync(file)), @"\bPoly\.Ast\b"))
                offenders.Add(relative);
        }

        await Assert.That(string.Join(", ", offenders)).IsEmpty()
            .Because("Poly/Introspection names no Poly.Ast type");
    }

    [Test]
    public async Task Introspection_UsesNoAstType_InItsCompiledCode() {
        // The text scan cannot see an AST type the global usings import (Poly.Ast's Node, NodeId, NodeExtensions) or one
        // reached through var or a member's return type, so this reads what Introspection compiled to: every signature,
        // local and IL operand of every type declared in a Poly.Introspection namespace (#998 T13).
        var assembly = typeof(Poly.Introspection.ITypeSystem).Assembly;
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(static field => (OpCode)field.GetValue(null)!)
            .ToDictionary(static opcode => (ushort)opcode.Value);
        const BindingFlags declared = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static;
        var offenders = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var type in assembly.GetTypes().Where(IsIntrospection)) {
            void Flag(Type? used, string where) {
                if (NamesAst(used))
                    offenders.Add($"{type.FullName} {where}: {used}");
            }

            void FlagAttributes(IEnumerable<CustomAttributeData> attributes, string where) {
                foreach (var attribute in attributes) {
                    Flag(attribute.AttributeType, $"{where} attribute");
                    foreach (var argument in attribute.ConstructorArguments.Concat(attribute.NamedArguments.Select(static named => named.TypedValue)))
                        Flag(argument.Value as Type, $"{where} attribute argument");
                }
            }

            void FlagConstraints(IEnumerable<Type> parameters, string where) {
                foreach (var parameter in parameters.Where(static candidate => candidate.IsGenericParameter))
                    foreach (var constraint in parameter.GetGenericParameterConstraints())
                        Flag(constraint, $"{where} constraint on {parameter.Name}");
            }

            Flag(type.BaseType, "base");
            FlagAttributes(type.CustomAttributes, "type");
            FlagConstraints(type.GetGenericArguments(), "type");
            foreach (var implemented in type.GetInterfaces())
                Flag(implemented, "interface");
            foreach (var field in type.GetFields(declared)) {
                Flag(field.FieldType, $"field {field.Name}");
                FlagAttributes(field.CustomAttributes, $"field {field.Name}");
            }
            foreach (var property in type.GetProperties(declared))
                FlagAttributes(property.CustomAttributes, $"property {property.Name}");

            foreach (var method in type.GetMethods(declared).Cast<MethodBase>().Concat(type.GetConstructors(declared))) {
                if (method is MethodInfo info)
                    Flag(info.ReturnType, $"{method.Name} return");
                FlagAttributes(method.CustomAttributes, method.Name);
                if (method.IsGenericMethodDefinition)
                    FlagConstraints(method.GetGenericArguments(), method.Name);
                foreach (var parameter in method.GetParameters()) {
                    Flag(parameter.ParameterType, $"{method.Name} parameter {parameter.Name}");
                    FlagAttributes(parameter.CustomAttributes, $"{method.Name} parameter {parameter.Name}");
                }
                if (method.GetMethodBody() is not { } body)
                    continue;
                foreach (var local in body.LocalVariables)
                    Flag(local.LocalType, $"{method.Name} local");
                foreach (var clause in body.ExceptionHandlingClauses.Where(static clause => clause.Flags == ExceptionHandlingClauseOptions.Clause))
                    Flag(clause.CatchType, $"{method.Name} catch");

                var il = body.GetILAsByteArray() ?? [];
                var typeArguments = type.IsGenericType ? type.GetGenericArguments() : null;
                var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
                for (var at = 0; at < il.Length;) {
                    var code = il[at] == 0xFE ? (ushort)(0xFE00 | il[at + 1]) : il[at];
                    at += il[at] == 0xFE ? 2 : 1;
                    var opcode = opcodes[code];
                    if (opcode.OperandType is OperandType.InlineType or OperandType.InlineTok
                        or OperandType.InlineMethod or OperandType.InlineField) {
                        var member = type.Module.ResolveMember(BitConverter.ToInt32(il, at), typeArguments, methodArguments);
                        switch (member) {
                            case Type used:
                                Flag(used, $"{method.Name} uses");
                                break;
                            case FieldInfo used:
                                Flag(used.DeclaringType, $"{method.Name} reads {used.Name}");
                                Flag(used.FieldType, $"{method.Name} reads {used.Name}");
                                break;
                            case MethodBase used:
                                Flag(used.DeclaringType, $"{method.Name} calls {used.Name}");
                                if (used is MethodInfo called)
                                    Flag(called.ReturnType, $"{method.Name} calls {used.Name}");
                                foreach (var parameter in used.GetParameters())
                                    Flag(parameter.ParameterType, $"{method.Name} calls {used.Name}");
                                if (used.IsGenericMethod)
                                    foreach (var argument in used.GetGenericArguments())
                                        Flag(argument, $"{method.Name} calls {used.Name}");
                                break;
                        }
                    }
                    at += OperandSize(opcode.OperandType, il, at);
                }
            }
        }

        await Assert.That(string.Join("; ", offenders)).IsEmpty()
            .Because("Poly/Introspection's compiled code uses no Poly.Ast type");

        static bool IsIntrospection(Type type) {
            var outer = type;
            while (outer.DeclaringType is { } declaring)
                outer = declaring;
            return outer.Namespace is "Poly.Introspection" || outer.Namespace?.StartsWith("Poly.Introspection.", StringComparison.Ordinal) == true;
        }

        static bool NamesAst(Type? type) {
            if (type is null)
                return false;
            if (type.HasElementType)
                return NamesAst(type.GetElementType());
            if (type.Namespace is "Poly.Ast" || type.Namespace?.StartsWith("Poly.Ast.", StringComparison.Ordinal) == true)
                return true;
            return type.IsGenericType && !type.IsGenericTypeDefinition && type.GetGenericArguments().Any(NamesAst);
        }

        static int OperandSize(OperandType operand, byte[] il, int at) => operand switch {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, at),
            _ => 4,
        };
    }

    /// <summary>Removes <c>//</c>, <c>/* */</c>, string and char literals, so documentation and messages do not count.</summary>
    private static string StripCommentsAndStrings(string text) {
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++) {
            var current = text[i];
            if (current == '/' && i + 1 < text.Length && text[i + 1] == '/') {
                while (i < text.Length && text[i] != '\n') i++;
                builder.Append('\n');
                continue;
            }
            if (current == '/' && i + 1 < text.Length && text[i + 1] == '*') {
                i += 2;
                while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/')) i++;
                i++;
                builder.Append(' ');
                continue;
            }
            if (current == '"') {
                var verbatim = i > 0 && text[i - 1] == '@';
                i++;
                while (i < text.Length) {
                    if (!verbatim && text[i] == '\\') { i += 2; continue; }
                    if (text[i] == '"') {
                        if (verbatim && i + 1 < text.Length && text[i + 1] == '"') { i += 2; continue; }
                        break;
                    }
                    i++;
                }
                builder.Append("\"\"");
                continue;
            }
            if (current == '\'') {
                i++;
                while (i < text.Length && text[i] != '\'') {
                    if (text[i] == '\\') i++;
                    i++;
                }
                builder.Append("''");
                continue;
            }
            builder.Append(current);
        }
        return builder.ToString();
    }

    private static string GetRepoRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}