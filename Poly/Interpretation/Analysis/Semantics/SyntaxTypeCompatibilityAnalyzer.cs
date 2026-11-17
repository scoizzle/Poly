using System.Collections.Frozen;
using System.Globalization;

using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Interpretation.Analysis.Semantics;

public static class SyntaxTypeCompatibilityAnalyzerExtensions {
    extension(AnalyzerBuilder builder) {
        /// <summary>Adds the <see cref="SyntaxTypeCompatibilityAnalyzer"/> to the pipeline.</summary>
        public AnalyzerBuilder UseSyntaxTypeCompatibility() {
            builder.AddAnalyzer(new SyntaxTypeCompatibilityAnalyzer());
            return builder;
        }
    }
}

/// <summary>
/// Validate pack on the lowered Syntax AST: operation type compatibility. The
/// interpretation pipeline resolved types (TypeAndMemberResolver) but never
/// rejected incompatible operations — a Text member compared to a Number constant, a bool in arithmetic,
/// a non-Boolean operand to <c>not</c>. The VM then silently coerced garbage, and the
/// C# export failed at compile. This pass reports the class at VM-compile time, so the
/// runtime fails loud even for programmatically-constructed or MCP-driven expressions
/// that bypass the DSL authoring analyzer.
///
/// An arithmetic, comparison, bitwise, shift or unary-minus verdict is the type provider's operator for the
/// operands' type definitions (<c>GetOperator</c>, nullable unwrapped), after a short list of named rules the
/// provider cannot answer yet, each naming the row that removes it. Assignment, casts and Boolean
/// operands are still checked at the CLR-type-category level. Only a bare null literal and an operand
/// with no resolved type are skipped.
/// </summary>
internal sealed class SyntaxTypeCompatibilityAnalyzer : INodeAnalyzer {
    public const string Id = "SyntaxTypeCompatibility";
    public string PassName => Id;

    /// <summary>Diagnostic code used by <see cref="Interpreter.Compile"/> to fail loud.</summary>
    public const string DiagnosticCode = "VmTypeCompatibility";


    /// <summary>
    /// One check per node kind, keyed by the sealed node type. The keys are the kinds this analyzer judges; the operand
    /// coverage test reads them (lock amendment 11), so a kind cannot gain or lose a check without its rows.
    /// </summary>
    private static readonly FrozenDictionary<Type, Action<SyntaxTypeCompatibilityAnalyzer, AnalysisContext, Node>> Checks =
        new Dictionary<Type, Action<SyntaxTypeCompatibilityAnalyzer, AnalysisContext, Node>> {
            [typeof(Equal)] = static (a, c, n) => { var x = (Equal)n; a.CheckOperator(c, x, x.LeftHandValue, x.RightHandValue, OperatorKind.Equal); },
            [typeof(NotEqual)] = static (a, c, n) => { var x = (NotEqual)n; a.CheckOperator(c, x, x.LeftHandValue, x.RightHandValue, OperatorKind.NotEqual); },
            [typeof(LessThan)] = static (a, c, n) => { var x = (LessThan)n; a.CheckOperator(c, x, x.LeftHandValue, x.RightHandValue, OperatorKind.LessThan); },
            [typeof(LessThanOrEqual)] = static (a, c, n) => { var x = (LessThanOrEqual)n; a.CheckOperator(c, x, x.LeftHandValue, x.RightHandValue, OperatorKind.LessThanOrEqual); },
            [typeof(GreaterThan)] = static (a, c, n) => { var x = (GreaterThan)n; a.CheckOperator(c, x, x.LeftHandValue, x.RightHandValue, OperatorKind.GreaterThan); },
            [typeof(GreaterThanOrEqual)] = static (a, c, n) => { var x = (GreaterThanOrEqual)n; a.CheckOperator(c, x, x.LeftHandValue, x.RightHandValue, OperatorKind.GreaterThanOrEqual); },
            [typeof(Add)] = static (a, c, n) => { var x = (Add)n; a.CheckOperator(c, x, x.LeftHandValue, x.RightHandValue, OperatorKind.Add); },
            [typeof(Subtract)] = static (a, c, n) => { var x = (Subtract)n; a.CheckOperator(c, x, x.LeftHandValue, x.RightHandValue, OperatorKind.Subtract); },
            [typeof(Multiply)] = static (a, c, n) => { var x = (Multiply)n; a.CheckOperator(c, x, x.LeftHandValue, x.RightHandValue, OperatorKind.Multiply); },
            [typeof(Divide)] = static (a, c, n) => { var x = (Divide)n; a.CheckOperator(c, x, x.LeftHandValue, x.RightHandValue, OperatorKind.Divide); },
            [typeof(Modulo)] = static (a, c, n) => { var x = (Modulo)n; a.CheckOperator(c, x, x.LeftHandValue, x.RightHandValue, OperatorKind.Modulo); },
            [typeof(BitwiseAnd)] = static (a, c, n) => { var x = (BitwiseAnd)n; a.CheckOperator(c, x, x.LeftHandValue, x.RightHandValue, OperatorKind.BitwiseAnd); },
            [typeof(BitwiseOr)] = static (a, c, n) => { var x = (BitwiseOr)n; a.CheckOperator(c, x, x.LeftHandValue, x.RightHandValue, OperatorKind.BitwiseOr); },
            [typeof(BitwiseXor)] = static (a, c, n) => { var x = (BitwiseXor)n; a.CheckOperator(c, x, x.LeftHandValue, x.RightHandValue, OperatorKind.BitwiseXor); },
            [typeof(ShiftLeft)] = static (a, c, n) => { var x = (ShiftLeft)n; a.CheckOperator(c, x, x.LeftHandValue, x.RightHandValue, OperatorKind.ShiftLeft); },
            [typeof(ShiftRight)] = static (a, c, n) => { var x = (ShiftRight)n; a.CheckOperator(c, x, x.LeftHandValue, x.RightHandValue, OperatorKind.ShiftRight); },
            [typeof(UnaryMinus)] = static (a, c, n) => a.CheckUnaryOperator(c, ((UnaryMinus)n).Operand, OperatorKind.UnaryMinus),
            [typeof(BitwiseNot)] = static (a, c, n) => a.CheckUnaryOperator(c, ((BitwiseNot)n).Operand, OperatorKind.BitwiseNot),
            [typeof(TypeAs)] = static (a, c, n) => a.CheckTypeAs(c, (TypeAs)n),
            [typeof(New)] = static (a, c, n) => a.CheckNew(c, (New)n),
            [typeof(Not)] = static (a, c, n) => {
                var operand = DefinitionOf(c, ((Not)n).Value);
                if (operand is not null && operand.PrimitiveType is not PrimitiveType.Boolean
                    && CategoryOf(operand) is not (Cat.Unknown or Cat.Null))
                    a.Report(c, n, $"'not' requires a Boolean operand (got '{operand.Name}')");
            },
            [typeof(And)] = static (a, c, n) => { var x = (And)n; a.CheckBooleanOperands(c, x.LeftHandValue, x.RightHandValue); },
            [typeof(Or)] = static (a, c, n) => { var x = (Or)n; a.CheckBooleanOperands(c, x.LeftHandValue, x.RightHandValue); },
            [typeof(Assignment)] = static (a, c, n) => {
                var assignment = (Assignment)n;
                if (assignment.Destination is Member destination)
                    a.CheckAssign(c, assignment, destination);
                if (assignment.Destination is Variable variable)
                    a.CheckVariableAssign(c, variable, assignment);
            },
            [typeof(Invoke)] = static (a, c, n) => {
                a.CheckInvokeTarget(c, (Invoke)n);
                a.RewriteInvokeArgumentConversions(c, (Invoke)n);
            },
            [typeof(TypeCast)] = static (a, c, n) => a.CheckTypeCast(c, (TypeCast)n),
            [typeof(ForEachLoop)] = static (a, c, n) => a.CheckForEachLoop(c, (ForEachLoop)n),
        }.ToFrozenDictionary();

    /// <summary>The node kinds this analyzer has a check for.</summary>
    internal static IReadOnlyCollection<Type> CheckedKinds => Checks.Keys;

    public void Analyze(AnalysisContext context, Node node) {
        if (Checks.TryGetValue(node.GetType(), out var check))
            check(this, context, node);
        this.AnalyzeChildren(context, node);
    }

    /// <summary>A declared <c>foreach (V x in …)</c> loop variable requires a conversion from
    /// the collection's element type to <c>V</c> (C# allows an explicit one). When the
    /// element type is known and no conversion exists, the loop cannot execute soundly.</summary>
    private void CheckForEachLoop(AnalysisContext context, ForEachLoop loop) {
        if (loop.DeclaredType is null)
            return;
        var declaredDef = context.GetResolvedType(loop.DeclaredType);
        var declared = declaredDef?.GetRuntimeType();
        if (declared is null) {
            Report(context, loop, "cannot resolve the foreach loop variable's declared type");
            return;
        }
        var elementDef = context.GetMetadata<ForEachElementTypeMetadata>(loop)?.ElementType;
        var element = elementDef?.GetRuntimeType();
        if (element is null || declaredDef is null || CanConvert(elementDef!, declaredDef))
            return;
        Report(context, loop,
            $"cannot convert each element of type '{element.Name}' to the loop variable type '{declared.Name}'");
    }

    /// <summary>A declared <c>foreach (V x in …)</c> loop variable needs an implicit
    /// conversion from the element type to <c>V</c>, as in C#.</summary>
    private static bool CanConvert(ITypeDefinition from, ITypeDefinition to) => IsImplicitlyConvertible(from, to);

    /// <summary>True when the platform converts <paramref name="from"/> to <paramref name="to"/> implicitly: identity,
    /// reference/boxing, nullable lifting, or a built-in numeric conversion. The platform answers it
    /// (<see cref="ITypeDefinition.IsAssignableWithoutConversion"/>, <see cref="ITypeDefinition.ResolveConversionFrom"/>),
    /// so the analysis keeps no CLR type table of its own. This is a language rule, not a storage rule.</summary>
    internal static bool IsImplicitlyConvertible(ITypeDefinition from, ITypeDefinition to) =>
        to.IsAssignableWithoutConversion(from)
        || to.ResolveConversionFrom(from) is { Kind: ConversionOperatorKind.Implicit };


    /// <summary>
    /// An arithmetic, comparison, bitwise or shift node. The verdict is the type provider's operator for the operands'
    /// type definitions with their nullable unwrapped; a nullable operand is lifted by the VM. Only a bare null literal
    /// and an operand with no resolved type are not checked. The steps before the provider keep verdicts it cannot give
    /// yet, each named with the row that removes it; the order is part of the verdict.
    /// </summary>
    private void CheckOperator(AnalysisContext context, Node parent, Node leftNode, Node rightNode, OperatorKind kind) {
        if (OperandDefinition(context, leftNode) is not { } leftDefinition || OperandDefinition(context, rightNode) is not { } rightDefinition)
            return;
        var left = leftDefinition.GetRuntimeType();
        var right = rightDefinition.GetRuntimeType();
        var arithmetic = kind is OperatorKind.Add or OperatorKind.Subtract or OperatorKind.Multiply
            or OperatorKind.Divide or OperatorKind.Modulo;
        var comparison = kind is OperatorKind.Equal or OperatorKind.NotEqual or OperatorKind.LessThan
            or OperatorKind.LessThanOrEqual or OperatorKind.GreaterThan or OperatorKind.GreaterThanOrEqual;

        // Add is string concatenation when an operand is exactly a string (`"a" + 1`, `'a' + "b"`); `char + char` is
        // numeric. The Text + X policy is T-07's, the boxing conversion T-21's.
        if (kind is OperatorKind.Add && (left == typeof(string) || right == typeof(string))) {
            if (!RewriteStringConcat(context, parent, leftNode, rightNode))
                Report(context, leftNode, "string concatenation could not be resolved to String.Concat");
            return;
        }

        if (ConstantOperandRule.Applies(kind, context.GetEffectiveNode(leftNode), left, context.GetEffectiveNode(rightNode), right))
            return;

        // Date-offset fence (T-10): the interim DateOffsetRule, which the resolver reads for the offset's stamp too.
        if (DateOffsetRule.Applies(kind, leftDefinition, rightDefinition)) {
            if (!RewriteDateOffset(context, parent, leftNode, rightNode, negate: kind is OperatorKind.Subtract))
                Report(context, leftNode,
                    $"temporal offset requires a date type with AddDays (got '{leftDefinition.Name}')");
            return;
        }

        var (leftName, rightName) = (NameOf(leftDefinition), NameOf(rightDefinition));
        var incompatible = arithmetic ? $"arithmetic operand is not numeric (got '{leftName}' and '{rightName}')"
            : comparison ? $"comparison between incompatible types '{leftName}' and '{rightName}'"
            : $"operator {kind} is not defined for '{leftName}' and '{rightName}'";
        var noOperator = $"{incompatible}: no {kind} operator for these types";
        var leftUnderlying = Underlying(context, leftDefinition);
        var rightUnderlying = Underlying(context, rightDefinition);

        if (comparison) {
            if (EnumOperandRule(leftUnderlying, rightUnderlying) is { } sameEnum) {
                if (!sameEnum)
                    Report(context, leftNode, noOperator);
                return;
            }
            if (NullableDateRule(leftDefinition, rightDefinition)) {
                Report(context, leftNode, incompatible);
                return;
            }
            if (TextAndBooleanOrderingRule(kind, leftDefinition, rightDefinition))
                return;
        }

        if (leftUnderlying.GetOperator(kind, rightUnderlying) is not { } resolution) {
            if ((comparison && UndeclaredComparisonRule(kind, leftUnderlying, rightUnderlying))
                || EnumBitwiseRule.Of(kind, leftUnderlying, rightUnderlying) is not null)
                return;
            Report(context, leftNode, noOperator);
            return;
        }
        if (kind is OperatorKind.ShiftLeft or OperatorKind.ShiftRight) {
            StatePolicyShiftCount(context, parent, rightNode, rightDefinition, resolution);
            return;
        }
        if (!arithmetic)
            return;
        if (!VmExecutesDeclaredOperator(resolution)) {
            Report(context, leftNode, incompatible);
            return;
        }
        // Decimal's operators are declared members, invoked by name. Decimal.Add does not lift: rewriting
        // `decimal? + 1m` would turn null into 0, so a nullable operand keeps the operator for the VM to lift.
        if (resolution.Method is not null
            && Nullable.GetUnderlyingType(left!) is null && Nullable.GetUnderlyingType(right!) is null
            && !RewriteDecimalOperator(context, parent, leftNode, rightNode, kind, resolution))
            Report(context, leftNode, $"decimal arithmetic could not be resolved ({kind})");
    }

    /// <summary>
    /// A unary minus or complement: the provider's operator for the operand's definition, nullable unwrapped (lock
    /// amendment 6: <c>GetOperator</c> only; whether the VM computes a declared one is T-21's). Only a bare null literal and
    /// an operand with no resolved type are not checked.
    /// </summary>
    private void CheckUnaryOperator(AnalysisContext context, Node operandNode, OperatorKind kind) {
        if (OperandDefinition(context, operandNode) is not { } definition)
            return;
        var underlying = Underlying(context, definition);
        if (underlying.GetOperator(kind, null) is not null || EnumBitwiseRule.Of(kind, underlying, null) is not null)
            return;
        Report(context, operandNode, $"operator {kind} is not defined for '{NameOf(definition)}': no {kind} operator for this type");
    }

    /// <summary>
    /// Poly's shift-count policy (<see cref="ClrBuiltInRules.IsPolicyShiftCount"/>): a count of a wider integer type is
    /// converted to <c>int</c> unchecked, and the tree states it. The shift node is replaced by one whose count is that cast
    /// (lifted to <c>int?</c> for a nullable count), so the emitter shifts by an <c>int</c> and decides nothing.
    /// </summary>
    private static void StatePolicyShiftCount(
        AnalysisContext context, Node shift, Node count, ITypeDefinition countDefinition, ITypeOperator resolution) {
        var parameters = resolution.Parameters.ToArray();
        if (parameters.Length != 2 || parameters[1].ParameterTypeDefinition.PrimitiveType is not { } countType
            || !ClrBuiltInRules.IsPolicyShiftCount(countType))
            return;
        var target = countDefinition.GetRuntimeType() is { } runtime && Nullable.GetUnderlyingType(runtime) is not null ? typeof(int?) : typeof(int);
        var cast = new TypeCast(count, new ClrTypeReference(target));
        context.SetResolvedType(cast, context.TypeDefinitions.GetTypeDefinition(target)
            ?? throw new InvalidOperationException($"the type provider has no definition for '{target}'"));
        Node stated = shift switch {
            ShiftLeft left => left with { RightHandValue = cast },
            ShiftRight right => right with { RightHandValue = cast },
            _ => throw new InvalidOperationException($"'{shift.GetType().Name}' is not a shift")
        };
        if (context.GetResolvedType(shift) is { } shiftType)
            context.SetResolvedType(stated, shiftType);
        Replace(context, shift, stated);
    }

    /// <summary>
    /// The type definition an operand is judged by: its resolved type, or a constant's own. Null for a bare null literal or
    /// an unresolved operand. Interim (T-22b removes it, with the owner's ruling T-22a's PR asks for): a <c>void</c> operand
    /// (a void call) is not judged either; C# rejects it, and master computed it with the ring's zero.
    /// </summary>
    private static ITypeDefinition? OperandDefinition(AnalysisContext context, Node node) {
        var effective = context.GetEffectiveNode(node);
        if (effective is Constant { Value: null })
            return null;
        var definition = context.GetResolvedType(effective)
            ?? (effective is Constant { Value: { } value } ? context.TypeDefinitions.GetTypeDefinition(value.GetType()) : null);
        return definition?.GetRuntimeType() == typeof(void) ? null : definition;
    }

    /// <summary>The definition with a <c>Nullable&lt;T&gt;</c> peeled to <c>T</c>.</summary>
    private static ITypeDefinition Underlying(AnalysisContext context, ITypeDefinition definition) =>
        definition.GetRuntimeType() is { } runtime && Nullable.GetUnderlyingType(runtime) is { } underlying
            ? context.TypeDefinitions.GetTypeDefinition(underlying) ?? definition
            : definition;

    private static string NameOf(ITypeDefinition definition) => definition.GetRuntimeType()?.Name ?? definition.Name;

    private static bool IsEnum(ITypeDefinition definition) => definition.Kind is TypeKind.Enum;

    private static bool IsText(ITypeDefinition definition) =>
        definition.GetRuntimeType() is { } runtime && (runtime == typeof(string) || runtime == typeof(char));

    /// <summary>
    /// Enum comparisons, which the provider leaves undefined (T-24's deliberate none): two enums compare when they are
    /// the same type, and an enum compares with Text (its member name). A modeled enum counts as an enum. Null when
    /// neither applies. Removed by T-25; the Text allowance by T-07.
    /// </summary>
    private static bool? EnumOperandRule(ITypeDefinition left, ITypeDefinition right) {
        if (IsEnum(left) && IsEnum(right))
            return TypeDefinitionExtensions.IsSameType(left, right);
        if ((IsEnum(left) && IsText(right)) || (IsEnum(right) && IsText(left)))
            return true;
        return null;
    }

    /// <summary>
    /// Interim rule (T-07 removes it: the provider declares equality for every value type and the lowered module's modeled
    /// definitions declare their operators; owner ruling of 2026-11-13, value types have value equality). A comparison the
    /// provider answers none for, which master accepted because its scalar gate skipped the operands:
    /// <c>==</c> and <c>!=</c> between two operands of one CLR struct type that declares no equality operator; between two
    /// modeled definitions (a lowered entity or value type, whose generated C# type defines the equality but whose definition
    /// declares no <c>op_Equality</c>) of one type or of types related by inheritance; and ordering of two operands of one
    /// modeled type, which the domain analyzer accepts under T-07's ordering-legality rule.
    /// </summary>
    private static bool UndeclaredComparisonRule(OperatorKind kind, ITypeDefinition left, ITypeDefinition right) {
        var equality = kind is OperatorKind.Equal or OperatorKind.NotEqual;
        var same = TypeDefinitionExtensions.IsSameType(left, right);
        if (left is ClrTypeDefinition || right is ClrTypeDefinition)
            return equality && left.Kind is TypeKind.Struct && same;
        return equality ? same || left.IsAssignableFrom(right) || right.IsAssignableFrom(left) : same;
    }

    /// <summary>
    /// A veto, narrowed by T-21d: a nullable Date-category operand beside an operand of a DIFFERENT unwrapped type
    /// (<c>DateTime?</c> beside <c>DateTimeOffset</c>: the provider accepts it through an implicit conversion, C# compiles it,
    /// and the VM's nullable compare does not convert, so the build throws a raw expression error). The same unwrapped type
    /// (<c>DateOnly?</c> beside <c>DateOnly</c>) is C#'s lifted comparison and runs. Interim: removed with its cells in
    /// <c>KnownInterimRules</c> by row T-21b, when the VM converts in a nullable compare.
    /// </summary>
    private static bool NullableDateRule(ITypeDefinition left, ITypeDefinition right) =>
        (IsNullableDate(left) || IsNullableDate(right))
        && !TypeDefinitionExtensions.IsSameType(left.NullableOf ?? left, right.NullableOf ?? right);

    private static bool IsNullableDate(ITypeDefinition definition) =>
        definition.NullableOf is { } underlying && CategoryOf(underlying) is Cat.Date;

    /// <summary>
    /// Ordering of two exact <c>string</c> or two exact <c>bool</c> operands: the DSL accepts it and the interpreter
    /// orders Text (the emitter refuses Boolean ordering itself); the provider defines neither. Removed by T-07 (#631).
    /// </summary>
    private static bool TextAndBooleanOrderingRule(OperatorKind kind, ITypeDefinition left, ITypeDefinition right) {
        if (kind is not (OperatorKind.LessThan or OperatorKind.LessThanOrEqual or OperatorKind.GreaterThan or OperatorKind.GreaterThanOrEqual))
            return false;
        var (l, r) = (left.NullableOf ?? left, right.NullableOf ?? right);
        return TypeDefinitionExtensions.IsSameType(l, r)
            && l.PrimitiveType is PrimitiveType.String or PrimitiveType.Boolean;
    }

    /// <summary>
    /// Arithmetic the VM executes: an operator the platform applies, or one of decimal's declared members. Other
    /// declared arithmetic (temporal and Duration operators, IntPtr's) is refused. Removed by T-21 (the emitter executes
    /// or refuses declared arithmetic itself).
    /// </summary>
    private static bool VmExecutesDeclaredOperator(ITypeOperator resolution) =>
        resolution.Method is null
        || (resolution.DeclaringTypeDefinition.TryGetRuntimeType(out var declaring) && declaring == typeof(decimal));

    private void CheckTypeAs(AnalysisContext context, TypeAs typeAs) {
        var target = ClrTypeOf(context, typeAs)
            ?? (typeAs.TargetTypeReference is ClrTypeReference ctr ? ctr.RuntimeType : null);
        if (target is null)
            return;
        if (target.IsValueType)
            Report(context, typeAs,
                $"'as' cannot target value type '{target.Name}'");
    }

    private void CheckNew(AnalysisContext context, New created) {
        if (context.GetResolvedMember(created) is ITypeConstructor)
            return;
        var typeDef = context.GetResolvedType(created)
            ?? context.GetResolvedType(created.Type);
        if (created.Arguments.Length == 0
            && typeDef is IClrTypeDefinition { RuntimeType.IsValueType: true }) {
            var replacement = new Default(created.Type);
            context.SetResolvedType(replacement, typeDef);
            Replace(context, created, replacement);
            return;
        }
        var name = typeDef?.Name ?? created.Type.ToString();
        Report(context, created,
            $"no matching constructor for '{name}' with {created.Arguments.Length} argument(s)");
    }

    private void CheckBooleanOperands(AnalysisContext context, Node leftNode, Node rightNode) {
        var left = DefinitionOf(context, leftNode);
        var right = DefinitionOf(context, rightNode);
        if (left is null || right is null) return;
        var lc = CategoryOf(left);
        var rc = CategoryOf(right);
        if (lc is Cat.Unknown or Cat.Null || rc is Cat.Unknown or Cat.Null) return;
        if (lc is not Cat.Boolean || rc is not Cat.Boolean)
            Report(context, leftNode,
                $"'and'/'or' requires Boolean operands (got '{left.Name}' and '{right.Name}')");
    }

    private void CheckInvokeTarget(AnalysisContext context, Invoke invoke) {
        if (invoke.Delegate is Member member) {
            // Unmatched Member invoke must Error so Interpreter.Compile rejects (F12).
            // Preserve late-bind for numeric widening the overload scorer misses
            // (DateTime.AddDays(double) with a long arg) — only reject when no
            // same-name/arity candidate can accept the args via assign or widen.
            if (context.GetResolvedMember(invoke) is null
                && !HasPlausibleMemberOverload(context, invoke, member)) {
                var receiver = context.GetResolvedType(member.Value)?.Name ?? member.Value.GetType().Name;
                Report(context, invoke,
                    $"no matching member '{member.MemberName}' on '{receiver}' for invoke with {invoke.Arguments.Length} argument(s)");
            }
            return;
        }
        if (invoke.Delegate is Lambda lambda) {
            CheckInvokeArity(context, invoke, lambda, allowZeroArgsAsSetArgs: true);
            return;
        }
        if (invoke.Delegate is Variable or Parameter) {
            if (StoredLambdaBinding.TryGet(context, invoke.Delegate, out var stored)
                && stored is not null) {
                CheckInvokeArity(context, invoke, stored.Lambda, allowZeroArgsAsSetArgs: false);
                return;
            }
            Report(context, invoke,
                $"Invoke target must be a member, lambda, or stored closure, got {invoke.Delegate.GetType().Name}");
            return;
        }
        if (invoke.Delegate is TypeReference { TypeName: "nameof" }) {
            // Folded to a constant by the constant-folding pass; the callee is accepted here
            // because the analyzer walks the original node, not the replacement.
            return;
        }
        Report(context, invoke,
            $"Invoke target must be a member, lambda, or stored closure, got {invoke.Delegate.GetType().Name}");
    }

    private static bool HasPlausibleMemberOverload(
        AnalysisContext context, Invoke invoke, Member member) {
        var targetType = context.GetResolvedType(member.Value);
        if (targetType is null)
            return true; // unknown receiver — allow emit late-bind
        var argTypes = new ITypeDefinition[invoke.Arguments.Length];
        for (var i = 0; i < invoke.Arguments.Length; i++) {
            var argType = context.GetResolvedType(context.GetEffectiveNode(invoke.Arguments[i]));
            if (argType is null)
                return true; // incomplete — allow late-bind
            argTypes[i] = argType;
        }
        foreach (var candidate in targetType.Methods.WithName(member.MemberName)) {
            var parameters = candidate.Parameters?.ToArray() ?? [];
            if (!CallFits(parameters, invoke.Arguments.Length))
                continue;
            var ok = true;
            for (var i = 0; i < invoke.Arguments.Length; i++) {
                if (ArgumentMatches(context, parameters[i], argTypes[i], invoke.Arguments[i]))
                    continue;
                ok = false;
                break;
            }
            if (ok)
                return true;
        }
        return false;
    }

    internal static bool CallFits(IParameter[] parameters, int argCount) {
        if (argCount > parameters.Length)
            return false;
        for (var i = argCount; i < parameters.Length; i++) {
            if (!parameters[i].IsOptional)
                return false;
        }
        return true;
    }

    private static bool ArgumentMatches(
        AnalysisContext context, IParameter parameter, ITypeDefinition argType, Node arg) {
        var paramType = parameter.ParameterTypeDefinition;
        if (ReferenceEquals(paramType, argType) || paramType.IsAssignableFrom(argType))
            return true;
        var shape = arg as Lambda
            ?? (StoredLambdaBinding.TryGet(context, arg, out var storedShape)
                ? storedShape!.Lambda
                : null);
        if (shape is not null && MethodInvocationSemanticResolver.AcceptsLambdaArgument(context, shape, paramType))
            return true;
        if (paramType.ResolveConversionFrom(argType) is { Kind: ConversionOperatorKind.Implicit })
            return true;
        if (paramType.Kind == TypeKind.Enum && argType.TryGetRuntimeType(out var enumArg) && enumArg == typeof(string))
            return true; // projected enums are string-valued consts (BuildEnumTypeDef)
        if (arg is Constant { Value: null })
            return AcceptsNull(paramType);
        if (NullableValueAccepts(paramType, argType))
            return true;
        if (CollectionsMatch(paramType, argType))
            return true;
        return OptionalNullBoxMatches(context, parameter, paramType, argType, arg);
    }

    internal static bool AcceptsNull(ITypeDefinition type) {
        if (!type.TryGetRuntimeType(out var runtimeType))
            return true;
        if (!runtimeType.IsValueType)
            return true;
        return Nullable.GetUnderlyingType(runtimeType) is not null;
    }

    private static bool NullableValueAccepts(ITypeDefinition paramType, ITypeDefinition argType) {
        if (!paramType.TryGetRuntimeType(out var paramRuntime) || !argType.TryGetRuntimeType(out var argRuntime))
            return false;
        var underlying = Nullable.GetUnderlyingType(paramRuntime);
        return underlying is not null && (argRuntime == underlying || underlying.IsAssignableFrom(argRuntime));
    }

    private static bool CollectionsMatch(ITypeDefinition paramType, ITypeDefinition argType) {
        var paramElement = ElementOf(paramType);
        var argElement = ElementOf(argType);
        if (paramElement is null || argElement is null)
            return false;
        if (ReferenceEquals(paramElement, argElement))
            return true;
        if (string.Equals(paramElement.FullName, argElement.FullName, StringComparison.Ordinal)
            && paramElement.GenericParameters.Count() == argElement.GenericParameters.Count())
            return true;
        // C# generic collections are invariant. Element assignability would
        // accept IList<Derived> for IList<Base>.
        return false;
    }

    private static ITypeDefinition? ElementOf(ITypeDefinition type) {
        if (type is AstCollectionTypeDefinition collection)
            return collection.ElementType;
        if (type.TryGetRuntimeType(out var runtimeType) && runtimeType == typeof(string))
            return null;
        if (!type.IsCollectionShape)
            return null;
        var element = type.GetElementType();
        return element is not null && !IsUniversalObject(element) ? element : null;
    }

    private static bool IsUniversalObject(ITypeDefinition type) =>
        type.TryGetRuntimeType(out var runtimeType) && runtimeType == typeof(object);

    /// <summary>
    /// Optional AST reference params take a null-coalesce that resolves as object
    /// (dictionary fetch ?? null). The non-null branch must still be that parameter's type.
    /// </summary>
    private static bool OptionalNullBoxMatches(
        AnalysisContext context,
        IParameter parameter,
        ITypeDefinition paramType,
        ITypeDefinition argType,
        Node arg) {
        if (!parameter.IsOptional)
            return false;
        if (paramType is not (AstTypeDefinition or AstCollectionTypeDefinition))
            return false;
        if (!AcceptsNull(paramType))
            return false;
        if (!argType.TryGetRuntimeType(out var runtimeType) || runtimeType != typeof(object))
            return false;
        return arg is Conditional or Coalesce && NullBoxCarries(context, paramType, arg);
    }

    private static bool NullBoxCarries(AnalysisContext context, ITypeDefinition paramType, Node node) {
        switch (node) {
            case Constant { Value: null }:
                return true;
            case Constant:
                return false;
            case Coalesce coal:
                return NullBoxCarries(context, paramType, coal.LeftHandValue)
                    && NullBoxCarries(context, paramType, coal.RightHandValue);
            case Conditional cond:
                return NullBoxCarries(context, paramType, cond.IfTrue)
                    && NullBoxCarries(context, paramType, cond.IfFalse);
            case TypeCast cast:
                return CastMatches(context, paramType, cast);
            default:
                var resolved = context.GetResolvedType(node);
                if (resolved is null || IsUniversalObject(resolved))
                    return false;
                return ReferenceEquals(paramType, resolved) || paramType.IsAssignableFrom(resolved);
        }
    }

    private static bool CastMatches(AnalysisContext context, ITypeDefinition paramType, TypeCast cast) {
        var target = cast.TargetTypeReference;
        while (target is OptionalTypeReference optional)
            target = optional.InnerType;
        if (context.GetResolvedType(target) is { } resolved
            && !IsUniversalObject(resolved)
            && (ReferenceEquals(paramType, resolved) || paramType.IsAssignableFrom(resolved)))
            return true;
        if (target is NamedTypeReference named)
            return string.Equals(named.TypeName, paramType.Name, StringComparison.Ordinal)
                && (named.Namespace is null || string.Equals(named.Namespace, paramType.Namespace, StringComparison.Ordinal));
        if (target is PrimitiveTypeReference primitive)
            return paramType.TryGetRuntimeType(out var runtimeType) && primitive.PrimitiveId.GetClrType() == runtimeType;
        return false;
    }

    private void CheckInvokeArity(AnalysisContext context, Invoke invoke, Lambda lambda, bool allowZeroArgsAsSetArgs) {
        int nParams = lambda.Parameters.Count;
        int nArgs = invoke.Arguments.Length;
        if (allowZeroArgsAsSetArgs && nArgs == 0)
            return;
        if (nArgs == nParams)
            return;
        Report(context, invoke,
            $"lambda has {nParams} parameter(s) but invoke has {nArgs} argument(s)");
    }

    private void CheckVariableAssign(AnalysisContext context, Variable variable, Assignment assignment) {
        var binding = StoredLambdaBinding.BindingOf(context, variable);
        var isNullLiteral = assignment.Value is Constant { Value: null };
        var rhsType = isNullLiteral ? null : ClrTypeOf(context, assignment.Value);
        if (rhsType == typeof(void)) {
            Report(context, assignment,
                $"cannot assign void to variable '{variable.Name}'");
            return;
        }

        var prior = context.GetMetadata<VariableAssignedTypeMetadata>(binding);
        // A parameter's declared type is its prior, even on the first write. It is
        // not stored on the parameter so every write is checked against the signature.
        if (prior is null
            && binding is Parameter parameter
            && context.GetResolvedType(parameter)?.GetRuntimeType() is { } parameterType) {
            prior = new VariableAssignedTypeMetadata(parameterType);
        }

        // A null literal has no type; it is assignable to a reference or nullable prior.
        if (isNullLiteral) {
            if (prior?.ClrType is { } nullTarget
                && nullTarget.IsValueType
                && Nullable.GetUnderlyingType(nullTarget) is null) {
                Report(context, assignment,
                    $"cannot assign 'null' to variable '{variable.Name}' (incompatible with prior '{nullTarget.Name}')");
            }
            return;
        }

        if (rhsType is null) {
            if (prior is null)
                context.SetMetadata(binding, new VariableAssignedTypeMetadata(null));
            else
                Report(context, assignment,
                    $"cannot assign an unresolved value to variable '{variable.Name}' (incompatible with prior '{TypeLabel(prior.ClrType)}')");
            return;
        }

        if (prior is null) {
            context.SetMetadata(binding, new VariableAssignedTypeMetadata(rhsType));
            return;
        }

        var priorType = prior.ClrType;
        if (priorType is null) {
            Report(context, assignment,
                $"cannot assign '{rhsType.Name}' to variable '{variable.Name}' (incompatible with a prior write that has no resolved type)");
            return;
        }
        if (priorType == rhsType)
            return;

        var destDef = context.TypeDefinitions.GetTypeDefinition(priorType);
        var srcDef = context.TypeDefinitions.GetTypeDefinition(rhsType);
        // An implicit native-integer conversion has no method: the tree states it as a cast, which the emitter executes
        // through the word type (a nuint widening to double or decimal is unsigned; a decimal store is a handle).
        if (destDef is not null && srcDef is not null && !destDef.IsAssignableFrom(srcDef)
            && ClrBuiltInRules.NativeIntegerConversionFrom(destDef, srcDef) is ConversionOperatorKind.Implicit) {
            var cast = new TypeCast(assignment.Value, new ClrTypeReference(priorType));
            context.SetResolvedType(cast, destDef);
            var stated = assignment with { Value = cast };
            context.SetResolvedType(stated, destDef);
            Replace(context, assignment, stated);
            return;
        }
        bool convertible = rhsType == priorType
            || (destDef is not null && srcDef is not null
                && (IsImplicitlyConvertible(srcDef, destDef) || destDef.IsAssignableFrom(srcDef)));
        if (convertible) {
            if (destDef is not null && srcDef is not null)
                RewriteAssignmentConversion(context, assignment, destDef, srcDef);
            return;
        }

        Report(context, assignment,
            $"cannot assign '{rhsType.Name}' to variable '{variable.Name}' (incompatible with prior '{priorType.Name}')");
    }

    private static string TypeLabel(Type? type) => type?.Name ?? "unknown";

    private void CheckAssign(AnalysisContext context, Assignment assignment, Member destination) {
        var target = ClrTypeOf(context, destination);
        var rhs = ClrTypeOf(context, assignment.Value);
        if (target is null || rhs is null) return;
        var destDef = context.TypeDefinitions.GetTypeDefinition(target);
        var srcDef = context.TypeDefinitions.GetTypeDefinition(rhs);
        if (destDef is null || srcDef is null) return;
        if (destDef.IsAssignableFrom(srcDef)) {
            RewriteAssignmentConversion(context, assignment, destDef, srcDef);
            return;
        }
        var tc = CategoryOf(destDef);
        var rc = CategoryOf(srcDef);
        if (tc is Cat.Unknown or Cat.Null || rc is Cat.Unknown or Cat.Null) return;
        if (tc is Cat.Enum && rc is Cat.Text) return;
        if (!AssignCategoryRule(tc, destDef, rc, srcDef))
            Report(context, destination,
                $"cannot assign '{rhs.Name}' to '{destination.MemberName}' (type '{target.Name}')");
    }

    private void CheckTypeCast(AnalysisContext context, TypeCast typeCast) {
        var source = ClrTypeOf(context, typeCast.Operand);
        var dest = ClrTypeOf(context, typeCast);
        var destDef = typeCast.TargetTypeReference is ClrTypeReference destClr
                ? context.TypeDefinitions.GetTypeDefinition(destClr.RuntimeType)
                : typeCast.TargetTypeReference is PrimitiveTypeReference primRef
                    && primRef.PrimitiveId.GetClrType() is { } primClr
                    ? context.TypeDefinitions.GetTypeDefinition(primClr)
                : context.GetResolvedType(typeCast.TargetTypeReference)
                    ?? context.GetResolvedType(typeCast)
                    ?? (dest is not null ? context.TypeDefinitions.GetTypeDefinition(dest) : null);
        var srcDef = context.GetResolvedType(typeCast.Operand)
            ?? (source is not null ? context.TypeDefinitions.GetTypeDefinition(source) : null);
        if (destDef is not null && srcDef is not null) {
            // A native-integer cast is the platform's own conversion: the emitter converts through the word type, never
            // through IntPtr's declared operator or a Convert.To* overload pick.
            if (ClrBuiltInRules.NativeIntegerConversionFrom(destDef, srcDef) is not null)
                return;
            if (RewriteOperatorConversion(context, typeCast, typeCast.Operand, destDef, srcDef, implicitOnly: false))
                return;
            if (destDef.IsAssignableFrom(srcDef))
                return;
            if (srcDef.IsAssignableFrom(destDef))
                return;
            var srcCat = CategoryOf(srcDef);
            var destCat = CategoryOf(destDef);
            bool srcNumeric = IsNumericLike(srcDef);
            bool destNumeric = IsNumericLike(destDef);
            if ((srcNumeric && destNumeric)
                || (srcCat is Cat.Enum && destNumeric)
                || (srcNumeric && destCat is Cat.Enum)) {
                // Convert.ToInt16 throws. An unchecked C# cast wraps, and the emitter
                // compiles that cast. A checked cast keeps Convert.To*, which throws.
                if (UncheckedIntegerCast(typeCast, srcDef, destDef))
                    return;
                if (TryRewriteNumericConvert(context, typeCast, typeCast.Operand, destDef, srcDef))
                    return;
            }
        }
        if (srcDef is null || destDef is null)
            return;
        if (TypeDefinitionExtensions.IsSameType(srcDef, destDef))
            return;

        var sc = CategoryOf(srcDef);
        var dc = CategoryOf(destDef);
        if (sc is Cat.Unknown or Cat.Null || dc is Cat.Unknown or Cat.Null)
            return;
        if (sc is Cat.Number && dc is Cat.Number) {
            if (UncheckedIntegerCast(typeCast, srcDef, destDef))
                return;
            if (TryRewriteNumericConvert(context, typeCast, typeCast.Operand, destDef, srcDef))
                return;
            Report(context, typeCast,
                $"cannot convert '{srcDef.Name}' to '{destDef.Name}'");
            return;
        }
        if (dc is Cat.Enum && sc is Cat.Number)
            return;
        if (sc is Cat.Enum && dc is Cat.Number) {
            if (TryRewriteNumericConvert(context, typeCast, typeCast.Operand, destDef, srcDef))
                return;
            Report(context, typeCast,
                $"cannot convert '{srcDef.Name}' to '{destDef.Name}'");
            return;
        }

        Report(context, typeCast,
            $"cannot convert '{srcDef.Name}' to '{destDef.Name}'");
    }

    private void RewriteInvokeArgumentConversions(AnalysisContext context, Invoke invoke) {
        if (context.GetResolvedMember(invoke) is not ITypeMethod method)
            return;
        var parameters = method.Parameters.ToArray();
        if (parameters.Length == 0 || invoke.Arguments.Length == 0)
            return;
        var newArgs = new Node[invoke.Arguments.Length];
        var changed = false;
        for (var i = 0; i < invoke.Arguments.Length; i++) {
            var arg = invoke.Arguments[i];
            newArgs[i] = arg;
            if (i >= parameters.Length)
                continue;
            var destDef = parameters[i].ParameterTypeDefinition;
            var srcDef = context.GetResolvedType(arg)
                ?? (ClrTypeOf(context, arg) is { } clr
                    ? context.TypeDefinitions.GetTypeDefinition(clr)
                    : null);
            if (destDef is null || srcDef is null || !destDef.IsAssignableFrom(srcDef))
                continue;
            if (TryConversionInvoke(context, arg, destDef, srcDef, implicitOnly: true) is not { } converted)
                continue;
            newArgs[i] = converted;
            changed = true;
        }
        if (!changed)
            return;
        var rewritten = invoke with { Arguments = newArgs };
        context.SetResolvedMember(rewritten, method);
        Replace(context, invoke, rewritten);
    }

    private static void RewriteAssignmentConversion(
        AnalysisContext context,
        Assignment assignment,
        ITypeDefinition destDef,
        ITypeDefinition srcDef) {
        if (TryConversionInvoke(context, assignment.Value, destDef, srcDef, implicitOnly: true) is not { } converted)
            return;
        var rewritten = assignment with { Value = converted };
        context.SetResolvedType(rewritten, destDef);
        Replace(context, assignment, rewritten);
    }

    private static bool RewriteOperatorConversion(
        AnalysisContext context,
        Node original,
        Node value,
        ITypeDefinition destDef,
        ITypeDefinition srcDef,
        bool implicitOnly) {
        if (TryConversionInvoke(context, value, destDef, srcDef, implicitOnly) is not { } converted)
            return false;
        Replace(context, original, converted);
        return true;
    }

    private static Invoke? TryConversionInvoke(
        AnalysisContext context,
        Node value,
        ITypeDefinition destDef,
        ITypeDefinition srcDef,
        bool implicitOnly) {
        if (destDef.IsAssignableWithoutConversion(srcDef))
            return null;
        // A conversion with no method is applied by the platform: no node states it, so none is added here.
        if (destDef.ResolveConversionFrom(srcDef) is not { Method: { } method } conversion)
            return null;
        if (implicitOnly && conversion.Kind is not ConversionOperatorKind.Implicit)
            return null;
        var extras = method.Parameters.ToArray();
        Invoke invoke;
        if (method.IsStatic) {
            if (extras.Length != 1)
                return null;
            var typeRef = TypeRef(method.DeclaringTypeDefinition);
            var member = new Member(typeRef, method.Name);
            invoke = new Invoke(member, value);
            StampConversion(context, typeRef, member, invoke, method);
        }
        else {
            if (extras.Length != 0)
                return null;
            var member = new Member(value, method.Name);
            invoke = new Invoke(member);
            StampConversion(context, value, member, invoke, method);
        }
        return invoke;
    }

    private static bool RewriteStringConcat(
        AnalysisContext context, Node parent, Node left, Node right) {
        var stringDef = context.TypeDefinitions.GetTypeDefinition(typeof(string));
        if (stringDef is null)
            return false;
        var parts = new List<Node>();
        CollectStringConcatParts(context, left, parts);
        CollectStringConcatParts(context, right, parts);
        if (parts.Count < 2)
            return false;
        ITypeMethod? method;
        var allStrings = parts.All(p => ClrTypeOf(context, p) == typeof(string));
        if (!allStrings && parts.All(p => p is Constant)) {
            // Literal parts fold to one string constant (cheaper than a runtime call).
            var text = "";
            foreach (var p in parts)
                text += Convert.ToString(((Constant)p).Value, CultureInfo.InvariantCulture);
            Replace(context, parent, new Constant(text));
            return true;
        }

        if (parts.Count <= 4) {
            // A part that is not a string takes the object overload; the emitter boxes the
            // integer into the object argument (`"a" + x`, `x` an int).
            var objectDef = context.TypeDefinitions.GetTypeDefinition(typeof(object)) ?? stringDef;
            var argTypes = parts
                .Select(p => ClrTypeOf(context, p) == typeof(string) ? stringDef : objectDef)
                .ToArray();
            method = stringDef.FindMatchingMethodOverloads("Concat", argTypes).FirstOrDefault();
        }
        else {
            method = FindConcatEnumerable(stringDef, allStrings ? typeof(string) : typeof(object));
        }
        method ??= FindConcatEnumerable(stringDef, allStrings ? typeof(string) : typeof(object));
        if (method is null)
            return false;
        var invoke = StampStaticInvoke(context, method, [.. parts]);
        Replace(context, parent, invoke);
        return true;
    }

    private static void CollectStringConcatParts(AnalysisContext context, Node node, List<Node> parts) {
        var effective = context.GetNodeReplacement(node) ?? node;
        if (IsStringConcatInvoke(effective) && effective is Invoke concat) {
            foreach (var arg in concat.Arguments)
                CollectStringConcatParts(context, arg, parts);
            return;
        }
        if (effective is Add add
            && ClrTypeOf(context, add.LeftHandValue) is { } lt
            && ClrTypeOf(context, add.RightHandValue) is { } rt
            && (lt == typeof(string) || rt == typeof(string))) {
            CollectStringConcatParts(context, add.LeftHandValue, parts);
            CollectStringConcatParts(context, add.RightHandValue, parts);
            return;
        }
        // Prefer the folded replacement (e.g. `1 + 2` -> Constant(3)) so a literal chain
        // whose part is a folded add still counts as a constant.
        parts.Add(effective);
    }

    private static bool IsStringConcatInvoke(Node node) =>
        node is Invoke { Delegate: Member { MemberName: "Concat", Value: ClrTypeReference { RuntimeType: var t } } }
        && t == typeof(string);

    private static ITypeMethod? FindConcatEnumerable(ITypeDefinition stringDef, Type elementType) {
        var arrayType = elementType.MakeArrayType();
        var enumerableType = typeof(IEnumerable<>).MakeGenericType(elementType);
        ITypeMethod? enumerable = null;
        foreach (var method in stringDef.Methods) {
            if (!method.IsStatic || !string.Equals(method.Name, "Concat", StringComparison.Ordinal))
                continue;
            var parameters = method.Parameters.ToArray();
            if (parameters.Length != 1)
                continue;
            var paramType = parameters[0].ParameterTypeDefinition.GetRuntimeType();
            if (paramType == arrayType)
                return method;
            if (paramType == enumerableType)
                enumerable = method;
        }
        return enumerable;
    }

    /// <summary>
    /// Rewrites decimal arithmetic to the named member decimal declares (<c>Decimal.Add</c> for <c>op_Addition</c>), each
    /// operand converted to the operator's parameter as the provider states: identity or an intrinsic conversion inserts
    /// nothing, a declared conversion inserts its <c>op_Implicit</c> call.
    /// </summary>
    private static bool RewriteDecimalOperator(
        AnalysisContext context, Node parent, Node left, Node right, OperatorKind kind, ITypeOperator resolution) {
        var decimalDef = resolution.DeclaringTypeDefinition;
        var name = kind switch {
            OperatorKind.Add => "Add",
            OperatorKind.Subtract => "Subtract",
            OperatorKind.Multiply => "Multiply",
            OperatorKind.Divide => "Divide",
            OperatorKind.Modulo => "Remainder",
            _ => null
        };
        var parameters = resolution.Parameters.ToArray();
        if (name is null || parameters.Length != 2)
            return false;
        var leftArg = ConvertOperand(context, left, parameters[0].ParameterTypeDefinition) ?? left;
        var rightArg = ConvertOperand(context, right, parameters[1].ParameterTypeDefinition) ?? right;
        var method = decimalDef.FindMatchingMethodOverloads(name, [decimalDef, decimalDef]).FirstOrDefault();
        if (method is null)
            return false;
        var invoke = StampStaticInvoke(context, method, leftArg, rightArg);
        Replace(context, parent, invoke);
        return true;
    }

    /// <summary>The operand converted to <paramref name="parameter"/> as the provider states, or null when it states no implicit conversion.</summary>
    private static Node? ConvertOperand(AnalysisContext context, Node operand, ITypeDefinition parameter) {
        var source = context.GetResolvedType(operand)
            ?? (ClrTypeOf(context, operand) is { } clr
                ? context.TypeDefinitions.GetTypeDefinition(clr)
                : null);
        if (source is null)
            return null;
        if (TypeDefinitionExtensions.IsSameType(parameter, source))
            return operand;
        if (parameter.ResolveConversionFrom(source) is not { Kind: ConversionOperatorKind.Implicit } conversion)
            return null;
        return conversion.Method is null
            ? operand
            : TryConversionInvoke(context, operand, parameter, source, implicitOnly: true);
    }

    private static bool RewriteDateOffset(
        AnalysisContext context, Node parent, Node date, Node offset, bool negate) {
        var dateDef = context.GetResolvedType(date)
            ?? (ClrTypeOf(context, date) is { } clr
                ? context.TypeDefinitions.GetTypeDefinition(clr)
                : null);
        if (dateDef is null)
            return false;
        var method = dateDef.Methods.FirstOrDefault(m =>
            string.Equals(m.Name, "AddDays", StringComparison.Ordinal)
            && m.Parameters.Count() == 1);
        if (method is null)
            return false;
        var paramType = method.Parameters.First().ParameterTypeDefinition;
        Node amount = offset;
        if (negate) {
            amount = new UnaryMinus(offset);
            var offsetType = context.GetResolvedType(offset)
                ?? (ClrTypeOf(context, offset) is { } ot
                    ? context.TypeDefinitions.GetTypeDefinition(ot)
                    : null);
            if (offsetType is not null)
                context.SetResolvedType(amount, offsetType);
        }
        var coerced = CoerceTo(context, amount, paramType) ?? amount;
        var member = new Member(date, method.Name);
        var invoke = new Invoke(member, coerced);
        StampConversion(context, date, member, invoke, method);
        Replace(context, parent, invoke);
        return true;
    }

    private static bool TryRewriteNumericConvert(
        AnalysisContext context, Node original, Node value, ITypeDefinition destDef, ITypeDefinition srcDef) {
        var destClr = destDef.GetRuntimeType() ?? (destDef as IClrTypeDefinition)?.RuntimeType;
        var srcClr = srcDef.GetRuntimeType() ?? (srcDef as IClrTypeDefinition)?.RuntimeType;
        if (destClr is null)
            return false;
        var underlying = Nullable.GetUnderlyingType(destClr) ?? destClr;
        if (ClrConvertMethods.NameOf(underlying) is not { } name)
            return false;
        var convertDef = context.TypeDefinitions.GetTypeDefinition(typeof(Convert));
        if (convertDef is null)
            return false;
        Node source = value;
        var sourceDef = srcDef;
        var underlyingDefinition = destDef.NullableOf ?? destDef;
        if (srcClr is not null && IsIeeeFloating(srcDef) && IsIntegerType(underlyingDefinition)) {
            var doubleDef = context.TypeDefinitions.GetTypeDefinition(typeof(double));
            var mathDef = context.TypeDefinitions.GetTypeDefinition(typeof(Math));
            if (doubleDef is null || mathDef is null)
                return false;
            if (srcClr != typeof(double)) {
                var toDouble = convertDef.FindMatchingMethodOverloads("ToDouble", [srcDef]).FirstOrDefault();
                if (toDouble is null)
                    return false;
                source = StampStaticInvoke(context, toDouble, value);
                sourceDef = doubleDef;
            }
            var truncate = mathDef.FindMatchingMethodOverloads("Truncate", [doubleDef]).FirstOrDefault();
            if (truncate is null)
                return false;
            source = StampStaticInvoke(context, truncate, source);
            sourceDef = doubleDef;
        }
        var method = convertDef.FindMatchingMethodOverloads(name, [sourceDef]).FirstOrDefault();
        if (method is null)
            return false;
        var invoke = StampStaticInvoke(context, method, source);
        if (!underlying.IsValueType || destClr == underlying) {
            Replace(context, original, invoke);
            return true;
        }
        var nullableDef = context.TypeDefinitions.GetTypeDefinition(destClr);
        if (nullableDef is not null
            && TryConversionInvoke(context, invoke, nullableDef, method.MemberTypeDefinition, implicitOnly: true)
                is { } boxed) {
            Replace(context, original, boxed);
            return true;
        }
        Replace(context, original, invoke);
        return true;
    }

    private static void Replace(AnalysisContext context, Node original, Node replacement) {
        context.SetNodeReplacement(original, replacement);
        if (context.GetResolvedType(replacement) is { } resolved)
            context.SetResolvedType(original, resolved);
    }

    private static bool IsIeeeFloating(ITypeDefinition? definition) =>
        definition?.PrimitiveType is PrimitiveType.Float32 or PrimitiveType.Float64;

    /// <summary>An unchecked cast between integer widths. The emitter narrows it;
    /// <see cref="Convert"/> would throw instead of wrapping.</summary>
    private static bool UncheckedIntegerCast(TypeCast typeCast, ITypeDefinition? source, ITypeDefinition? dest) =>
        !typeCast.IsChecked && source is not null && dest is not null
        && IsIntegerType(source) && IsIntegerType(dest);

    /// <summary>An integer width, char included, and an enum (an enum is integral: its underlying type is an integer).
    /// The platform names the primitive, so this is not a CLR listing.</summary>
    private static bool IsIntegerType(ITypeDefinition? definition) =>
        definition is not null
        && (definition.PrimitiveType is { } primitive && (primitive.IsInteger() || primitive is PrimitiveType.Char)
            || definition.Kind == TypeKind.Enum);

    /// <summary>The Date-offset fence's amount conversion; its <c>Convert.To*</c> fallback is a private conversion rule. Removed by T-10.</summary>
    private static Node? CoerceTo(AnalysisContext context, Node value, ITypeDefinition destDef) {
        var srcDef = context.GetResolvedType(value)
            ?? (ClrTypeOf(context, value) is { } clr
                ? context.TypeDefinitions.GetTypeDefinition(clr)
                : null);
        if (srcDef is null)
            return null;
        if (destDef.IsAssignableWithoutConversion(srcDef))
            return value;
        if (TryConversionInvoke(context, value, destDef, srcDef, implicitOnly: true) is { } conv)
            return conv;
        if (ClrConvertMethods.NameOf(destDef) is { } name) {
            var convertDef = context.TypeDefinitions.GetTypeDefinition("System.Convert");
            var method = convertDef?.FindMatchingMethodOverloads(name, [srcDef]).FirstOrDefault();
            if (method is not null)
                return StampStaticInvoke(context, method, value);
        }
        return destDef.IsAssignableFrom(srcDef) ? value : null;
    }

    private static Invoke StampStaticInvoke(AnalysisContext context, ITypeMethod method, params Node[] args) {
        var typeRef = TypeRef(method.DeclaringTypeDefinition);
        var member = new Member(typeRef, method.Name);
        var invoke = new Invoke(member, args);
        StampConversion(context, typeRef, member, invoke, method);
        return invoke;
    }

    private static Node TypeRef(ITypeDefinition def) =>
        def is IClrTypeDefinition clr
            ? new ClrTypeReference(clr.RuntimeType)
            : new TypeDefinitionReference(def);

    private static void StampConversion(
        AnalysisContext context,
        Node typeOrInstance,
        Member member,
        Invoke invoke,
        ITypeMethod method) {
        context.SetResolvedType(typeOrInstance, method.DeclaringTypeDefinition);
        context.SetResolvedMember(member, method);
        context.SetResolvedMember(invoke, method);
    }

    private void Report(AnalysisContext context, Node node, string message) =>
        context.ReportError(node, message, DiagnosticCode);

    internal enum Cat { Text, Number, Boolean, Date, Enum, Guid, Null, Unknown }

    /// <summary>The definition a node's type resolves to, or null. Constants resolve through their value's type.</summary>
    private static ITypeDefinition? DefinitionOf(AnalysisContext context, Node node) {
        var effective = context.GetNodeReplacement(node) ?? node;
        return context.GetResolvedType(effective)
            ?? (effective is Constant { Value: not null } constant
                ? context.TypeDefinitions.GetClrTypeDefinition(constant.Value.GetType())
                : null);
    }

    private static Type? ClrTypeOf(AnalysisContext context, Node node) {
        var effective = context.GetNodeReplacement(node) ?? node;
        return context.GetResolvedType(effective)?.GetRuntimeType()
            ?? (effective is Constant { Value: not null } constant
                ? constant.Value.GetType()
                : null);
    }

    /// <summary>
    /// The category a definition belongs to, read from the platform's answers (<see cref="ITypeDefinition.PrimitiveType"/>,
    /// <see cref="ITypeDefinition.Kind"/> and <see cref="ITypeDefinition.NullableOf"/>) instead of a CLR runtime type.
    /// A host primitive the shared vocabulary does not name is <see cref="Cat.Unknown"/>: this table classifies, it does
    /// not decide what a host's type is.
    /// </summary>
    internal static Cat CategoryOf(ITypeDefinition definition) {
        ArgumentNullException.ThrowIfNull(definition);
        var underlying = definition.NullableOf ?? definition;
        return underlying.PrimitiveType switch {
            PrimitiveType.Boolean => Cat.Boolean,
            PrimitiveType.String or PrimitiveType.Char => Cat.Text,
            PrimitiveType.DateTime or PrimitiveType.DateTimeOffset or PrimitiveType.DateOnly
                or PrimitiveType.TimeOnly or PrimitiveType.TimeSpan => Cat.Date,
            PrimitiveType.Guid => Cat.Guid,
            PrimitiveType.Int8 or PrimitiveType.Int16 or PrimitiveType.Int32 or PrimitiveType.Int64
                or PrimitiveType.UInt8 or PrimitiveType.UInt16 or PrimitiveType.UInt32 or PrimitiveType.UInt64
                or PrimitiveType.Float32 or PrimitiveType.Float64 or PrimitiveType.Decimal
                or PrimitiveType.NInt or PrimitiveType.NUInt => Cat.Number,
            null when underlying.Kind == TypeKind.Enum => Cat.Enum,
            _ => Cat.Unknown
        };
    }

    /// <summary>True for the date, time and timestamp primitives (the Date category), nullable included.</summary>
    internal static bool IsDateCategory(ITypeDefinition definition) => CategoryOf(definition) is Cat.Date;

    internal static bool IsNumberCategory(ITypeDefinition definition) => CategoryOf(definition) is Cat.Number;

    /// <summary>A timestamp is exactly <c>DateTime</c> or <c>DateTimeOffset</c>; a nullable form is not one, as before.</summary>
    internal static bool IsTimestamp(ITypeDefinition definition) =>
        definition.PrimitiveType is PrimitiveType.DateTime or PrimitiveType.DateTimeOffset;

    /// <summary>A number for a cast: <c>char</c> is <see cref="Cat.Text"/> but converts as a number, and so does a number;
    /// a nullable <c>char</c> is not numeric, as before.</summary>
    internal static bool IsNumericLike(ITypeDefinition definition) =>
        definition.PrimitiveType is PrimitiveType.Char || CategoryOf(definition) is Cat.Number;

    /// <summary>Member assignment by CLR-type category, as before T-18 (no provider verdict yet). Removed by T-27.</summary>
    private static bool AssignCategoryRule(Cat lc, ITypeDefinition left, Cat rc, ITypeDefinition right) {
        if (lc == rc && lc is not Cat.Date) return true;
        if (lc == rc && lc is Cat.Date)
            return TypeDefinitionExtensions.IsSameType(left, right) || (IsTimestamp(left) && IsTimestamp(right));
        if (lc is Cat.Enum && rc is Cat.Text) return true;
        if (rc is Cat.Enum && lc is Cat.Text) return true;
        return false;
    }

    internal static bool IsClrTimestamp(Type type) =>
        type == typeof(DateTime) || type == typeof(DateTimeOffset);
}

/// <summary>
/// Interim rule (T-29 removes it when the provider defines the enum operators): C# defines <c>E &amp; E</c>,
/// <c>E | E</c>, <c>E ^ E</c> and <c>~E</c> for a CLR enum type <c>E</c>, with result <c>E</c> (§12.13.3, §12.9.5), and
/// master executed them, but the provider has none yet (T-24's deliberate none). The compatibility analysis accepts those
/// cells and the resolver types them <c>E</c>. Any other enum operand (an enum beside an integer or another enum, a shift,
/// a negation) is undefined, as in C#.
/// </summary>
internal static class EnumBitwiseRule {
    /// <summary>The enum definition a cell of this rule is typed by, or null. <paramref name="right"/> is null for <c>~E</c>.</summary>
    public static ITypeDefinition? Of(OperatorKind kind, ITypeDefinition left, ITypeDefinition? right) {
        if (left.GetRuntimeType() is not { IsEnum: true })
            return null;
        var applies = kind switch {
            OperatorKind.BitwiseAnd or OperatorKind.BitwiseOr or OperatorKind.BitwiseXor => right is not null && TypeDefinitionExtensions.IsSameType(left, right),
            OperatorKind.BitwiseNot => right is null,
            _ => false
        };
        return applies ? left : null;
    }
}

/// <summary>First assignment's representation for a <see cref="Variable"/> in this analysis.
/// Later writes must be type-compatible or <see cref="SyntaxTypeCompatibilityAnalyzer"/> errors.</summary>
internal sealed record VariableAssignedTypeMetadata(Type? ClrType) : IAnalysisMetadata;