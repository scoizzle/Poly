using System.Globalization;

using Poly.Interpretation.Vm;
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
/// interpretation pipeline resolved types (TypeAndMemberResolver) and classified
/// representations (ValueRepresentationAnalysis) but never rejected incompatible
/// operations — a Text member compared to a Number constant, a bool in arithmetic,
/// a non-Boolean operand to <c>not</c>. The VM then silently coerced garbage, and the
/// C# export failed at compile. This pass reports the class at VM-compile time, so the
/// runtime fails loud even for programmatically-constructed or MCP-driven expressions
/// that bypass the DSL authoring analyzer.
///
/// Compatibility is checked at the CLR-type-category level. Unknown and null operands
/// are skipped (the property bag is loosely typed; null comparisons are supported), and
/// enum operands accept string constants (runtime stores enum values as strings).
/// </summary>
internal sealed class SyntaxTypeCompatibilityAnalyzer : INodeAnalyzer {
    public const string Id = "SyntaxTypeCompatibility";
    public string PassName => Id;

    /// <summary>Diagnostic code used by <see cref="Interpreter.Compile"/> to fail loud.</summary>
    public const string DiagnosticCode = "VmTypeCompatibility";


    public void Analyze(AnalysisContext context, Node node) {
        switch (node) {
            case Equal eq:
                CheckComparison(context, eq.LeftHandValue, eq.RightHandValue);
                break;
            case NotEqual ne:
                CheckComparison(context, ne.LeftHandValue, ne.RightHandValue);
                break;
            case LessThan lt:
                CheckComparison(context, lt.LeftHandValue, lt.RightHandValue);
                break;
            case LessThanOrEqual lte:
                CheckComparison(context, lte.LeftHandValue, lte.RightHandValue);
                break;
            case GreaterThan gt:
                CheckComparison(context, gt.LeftHandValue, gt.RightHandValue);
                break;
            case GreaterThanOrEqual gte:
                CheckComparison(context, gte.LeftHandValue, gte.RightHandValue);
                break;
            case Add add:
                CheckArithmetic(context, add, add.LeftHandValue, add.RightHandValue, ArithmeticKind.Add);
                break;
            case Subtract sub:
                CheckArithmetic(context, sub, sub.LeftHandValue, sub.RightHandValue, ArithmeticKind.Subtract);
                break;
            case Multiply mul:
                CheckArithmetic(context, mul, mul.LeftHandValue, mul.RightHandValue, ArithmeticKind.Multiply);
                break;
            case Divide div:
                CheckArithmetic(context, div, div.LeftHandValue, div.RightHandValue, ArithmeticKind.Divide);
                break;
            case Modulo mod:
                CheckArithmetic(context, mod, mod.LeftHandValue, mod.RightHandValue, ArithmeticKind.Modulo);
                break;
            case TypeAs typeAs:
                CheckTypeAs(context, typeAs);
                break;
            case New created:
                CheckNew(context, created);
                break;
            case Not not:
                var operand = ClrTypeOf(context, not.Value);
                if (operand is not null && operand != typeof(bool) && CategoryOf(operand) is not (Cat.Unknown or Cat.Null))
                    Report(context, node, $"'not' requires a Boolean operand (got '{operand.Name}')");
                break;
            case And and:
                CheckBooleanOperands(context, and.LeftHandValue, and.RightHandValue);
                break;
            case Or or:
                CheckBooleanOperands(context, or.LeftHandValue, or.RightHandValue);
                break;
            case Assignment a:
                if (a.Destination is Member dest)
                    CheckAssign(context, a, dest);
                if (a.Destination is Variable v)
                    CheckVariableAssign(context, v, a);
                break;
            case Invoke inv:
                CheckInvokeTarget(context, inv);
                RewriteInvokeArgumentConversions(context, inv);
                break;
            case TypeCast tc:
                CheckTypeCast(context, tc);
                break;
            case ForEachLoop loop:
                CheckForEachLoop(context, loop);
                break;
        }

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
        var element = context.GetMetadata<ForEachElementTypeMetadata>(loop)?.ElementType?.GetRuntimeType();
        if (element is null || CanConvert(element, declared))
            return;
        Report(context, loop,
            $"cannot convert each element of type '{element.Name}' to the loop variable type '{declared.Name}'");
    }

    /// <summary>A declared <c>foreach (V x in …)</c> loop variable needs a per-item conversion
    /// from the element type to <c>V</c>. Only types that live in a scalar slot are supported
    /// (converted in slot space); a value element with a reference loop type, or a reference
    /// element with a different reference loop type, fails closed. <c>decimal</c>/<c>IntPtr</c>
    /// are <see cref="Cat.Number"/> but not slot scalars, so they are rejected too.</summary>
    private static bool CanConvert(Type from, Type to) {
        if (from == to)
            return true;
        return IsScalarSlot(from) && IsScalarSlot(to);
    }

    private static bool IsScalarSlot(Type t) =>
        t == typeof(double) || t == typeof(float) || t == typeof(bool)
        || (t.IsValueType && RingCodec.IsInline(t));

    private void CheckComparison(AnalysisContext context, Node leftNode, Node rightNode) {
        var left = ClrTypeOf(context, leftNode);
        var right = ClrTypeOf(context, rightNode);
        if (left is null || right is null) return;
        var lc = CategoryOf(left);
        var rc = CategoryOf(right);
        if (lc is Cat.Unknown or Cat.Null || rc is Cat.Unknown or Cat.Null) return;
        // A char compares with a numeric operand after promotion (`'a' > 96`).
        if (IsNumericLike(left) && IsNumericLike(right))
            return;
        if (!Compatible(lc, left, rc, right))
            Report(context, leftNode,
                $"comparison between incompatible types '{left.Name}' and '{right.Name}'");
    }

    private enum ArithmeticKind { Add, Subtract, Multiply, Divide, Modulo }

    private void CheckArithmetic(
        AnalysisContext context, Node parent, Node leftNode, Node rightNode, ArithmeticKind kind) {
        var left = ClrTypeOf(context, leftNode);
        var right = ClrTypeOf(context, rightNode);
        if (left is null || right is null) return;
        var lc = CategoryOf(left);
        var rc = CategoryOf(right);
        if (lc is Cat.Unknown or Cat.Null || rc is Cat.Unknown or Cat.Null) return;

        // Add is string concatenation when an operand is a string (`"a" + 1`, `'a' + "b"`).
        // `char + char` is numeric in C# (both are Cat.Text, so it is excluded here).
        if (kind is ArithmeticKind.Add && (left == typeof(string) || right == typeof(string))) {
            if (!RewriteStringConcat(context, parent, leftNode, rightNode))
                Report(context, leftNode, "string concatenation could not be resolved to String.Concat");
            return;
        }

        if (left == typeof(decimal) || right == typeof(decimal)) {
            if (!RewriteDecimalArithmetic(context, parent, leftNode, rightNode, kind))
                Report(context, leftNode,
                    $"decimal arithmetic could not be resolved ({kind})");
            return;
        }

        if ((lc is Cat.Date && rc is Cat.Number)
            && kind is ArithmeticKind.Add or ArithmeticKind.Subtract) {
            if (!RewriteDateOffset(context, parent, leftNode, rightNode, negate: kind is ArithmeticKind.Subtract))
                Report(context, leftNode,
                    $"temporal offset requires a date type with AddDays (got '{left.Name}')");
            return;
        }

        bool ok = IsNumericLike(left) && IsNumericLike(right);
        if (!ok)
            Report(context, leftNode,
                $"arithmetic operand is not numeric (got '{left.Name}' and '{right.Name}')");
    }

    /// <summary>True when the type takes part in arithmetic as a number. <c>char</c> is
    /// <see cref="Cat.Text"/> (string concatenation needs that) but promotes to <c>int</c>
    /// in arithmetic and comparison, as in C#.</summary>
    private static bool IsNumericLike(Type t) => t == typeof(char) || CategoryOf(t) is Cat.Number;

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
            var (kind, clr) = RepresentationOf(typeDef);
            context.SetMetadata(replacement, new ValueRepresentationMetadata(kind, clr));
            Replace(context, created, replacement);
            return;
        }
        var name = typeDef?.Name ?? created.Type.ToString();
        Report(context, created,
            $"no matching constructor for '{name}' with {created.Arguments.Length} argument(s)");
    }

    private void CheckBooleanOperands(AnalysisContext context, Node leftNode, Node rightNode) {
        var left = ClrTypeOf(context, leftNode);
        var right = ClrTypeOf(context, rightNode);
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
            if (context.GetMetadata<StoredLambdaMetadata>(invoke.Delegate) is { } stored) {
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
            var argType = context.GetResolvedType(invoke.Arguments[i]);
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
        if (IsNumericWidening(argType, paramType))
            return true;
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
        if (!type.TypeCategory.IsCollection)
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

    private static bool IsNumericWidening(ITypeDefinition from, ITypeDefinition to) {
        if (!from.TryGetRuntimeType(out var fromClr) || !to.TryGetRuntimeType(out var toClr))
            return false;
        var fromCode = Type.GetTypeCode(fromClr);
        var toCode = Type.GetTypeCode(toClr);
        // TypeCode identity alone is not widening: Object==Object for unrelated
        // classes (Uri vs StringBuilder) must not count as a plausible overload.
        if (fromCode == toCode)
            return NumericWidenRank(fromCode) is not null;
        return NumericWidenRank(fromCode) is int fr
            && NumericWidenRank(toCode) is int tr
            && fr <= tr;
    }

    private static int? NumericWidenRank(TypeCode code) => code switch {
        TypeCode.SByte or TypeCode.Byte => 1,
        TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Char => 2,
        TypeCode.Int32 or TypeCode.UInt32 => 3,
        TypeCode.Int64 or TypeCode.UInt64 => 4,
        TypeCode.Single => 5,
        TypeCode.Double => 6,
        TypeCode.Decimal => 7,
        _ => null
    };

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

    /// <summary>
    /// A local's type is recorded on its declaring <see cref="Variable"/> node, but
    /// every use (including each assignment destination) is a distinct node. Resolve
    /// a use to the declaration the scope validator bound it to, so the prior type of
    /// the <em>binding</em> — not of one assignment node — is what later writes check.
    /// </summary>
    private static Node BindingOf(AnalysisContext context, Variable variable) {
        var meta = context.GetMetadata<VariableAnalysisMetadata>(variable);
        if (meta is not null) {
            if (meta.VariableReferences.TryGetValue(variable, out var declaration)
                && declaration is not null)
                return declaration;
            if (meta.ParameterReferences.TryGetValue(variable, out var parameter))
                return parameter;
        }
        return variable;
    }

    /// <summary>
    /// True for the numeric types a slot can hold directly. Scalar numeric writes share
    /// one slot encoding (all <c>long</c>), so int↔long writes are fine; only a scalar
    /// widening into an IEEE slot needs a conversion, which the emitter performs.
    /// </summary>
    private static bool IsNumericType(Type? type) =>
        type is not null && NumericWidenRank(Type.GetTypeCode(type)) is not null;

    private void CheckVariableAssign(AnalysisContext context, Variable variable, Assignment assignment) {
        var binding = BindingOf(context, variable);
        var rhsMeta = context.GetMetadata<ValueRepresentationMetadata>(assignment.Value);
        if (rhsMeta is null)
            return;
        if (rhsMeta.Kind is ValueRepresentationKind.Void) {
            Report(context, assignment,
                $"cannot assign void to variable '{variable.Name}'");
            return;
        }

        var prior = context.GetMetadata<VariableAssignedTypeMetadata>(binding);
        // A parameter's declared type is its prior, even on the first write. It is
        // not stored on the parameter so every write is checked against the signature.
        if (prior is null
            && binding is Parameter parameter
            && context.GetResolvedType(parameter)?.GetRuntimeType() is { } parameterType
            && context.GetMetadata<ValueRepresentationMetadata>(parameter) is { } parameterMeta) {
            prior = new VariableAssignedTypeMetadata(parameterType, parameterMeta.Kind);
        }
        if (rhsMeta.Kind is ValueRepresentationKind.Unknown) {
            if (prior is null)
                context.SetMetadata(binding, new VariableAssignedTypeMetadata(null, rhsMeta.Kind));
            else if (prior.Kind is not ValueRepresentationKind.Unknown)
                Report(context, assignment,
                    $"cannot assign '{TypeLabel(rhsMeta.ClrType, rhsMeta.Kind)}' to variable '{variable.Name}' (incompatible with prior '{TypeLabel(prior.ClrType, prior.Kind)}')");
            return;
        }

        var rhsType = rhsMeta.ClrType;
        if (rhsType is not null && CategoryOf(rhsType) is Cat.Null)
            return;

        if (prior is null) {
            context.SetMetadata(binding, new VariableAssignedTypeMetadata(rhsType, rhsMeta.Kind));
            return;
        }

        // Scalar numeric writes share one slot encoding (all `long`), so int↔long are
        // fine by construction; the IEEE-slot widening exemption is further down.
        if (prior.Kind != rhsMeta.Kind) {
            Report(context, assignment,
                $"cannot assign '{TypeLabel(rhsType, rhsMeta.Kind)}' to variable '{variable.Name}' (incompatible with prior '{TypeLabel(prior.ClrType, prior.Kind)}')");
            return;
        }

        if (prior.ClrType is null || rhsType is null || prior.ClrType == rhsType)
            return;

        // char promotes implicitly to a wider numeric prior, as in C# — but only to
        // ushort/int/uint/long/ulong. `byte`/`sbyte`/`short` would truncate (C# rejects
        // them), and float/double take the IEEE branch below (which fails closed when the
        // destination is captured, because the capture store does not coerce).
        if (rhsType == typeof(char)
            && prior.ClrType is { } priorType
            && (priorType == typeof(ushort) || priorType == typeof(int) || priorType == typeof(uint)
                || priorType == typeof(long) || priorType == typeof(ulong)))
            return;

        var destDef = context.TypeDefinitions.GetTypeDefinition(prior.ClrType);
        var srcDef = context.TypeDefinitions.GetTypeDefinition(rhsType);
        if (destDef is not null && srcDef is not null && destDef.IsAssignableFrom(srcDef)) {
            RewriteAssignmentConversion(context, assignment, destDef, srcDef);
            return;
        }

        if (IsIeeeScalar(prior.ClrType) != IsIeeeScalar(rhsType)) {
            // A scalar widening into an IEEE slot is legal and converted on store
            // (`double x = 1; x = 2;`). The reverse (double → scalar) truncates where
            // C# rejects it, so it stays rejected. Scalar↔scalar writes are all the
            // same slot encoding and were always allowed.
            //
            // The conversion happens only on the plain variable store. A captured
            // variable is written through its closure cell, which the emitter does not
            // coerce, so the widening there would store the raw scalar and mis-read it
            // later; fail closed instead of accepting it.
            if (IsIeeeScalar(prior.ClrType) && IsNumericType(rhsType) && !IsCaptured(context, variable))
                return;
            Report(context, assignment,
                $"cannot assign '{rhsType.Name}' to variable '{variable.Name}' (incompatible with prior '{prior.ClrType.Name}')");
            return;
        }

        var pc = CategoryOf(prior.ClrType);
        var rc = CategoryOf(rhsType);
        if (pc is Cat.Unknown || rc is Cat.Unknown || !Compatible(pc, prior.ClrType, rc, rhsType, destThenSource: true)) {
            Report(context, assignment,
                $"cannot assign '{rhsType.Name}' to variable '{variable.Name}' (incompatible with prior '{prior.ClrType.Name}')");
        }
    }

    private static string TypeLabel(Type? type, ValueRepresentationKind kind) =>
        type?.Name ?? kind.ToString();

    /// <summary>True when the assignment destination is a captured binding: its writes
    /// go through a closure cell, which the emitter stores without a representation
    /// conversion (only the plain variable path coerces).</summary>
    private static bool IsCaptured(AnalysisContext context, Variable variable) =>
        context.GetMetadata<VariableAnalysisMetadata>(variable)?.CapturedVariables.Contains(variable) == true;

    private static bool IsIeeeScalar(Type type) =>
        type == typeof(float) || type == typeof(double);

    private void CheckAssign(AnalysisContext context, Assignment assignment, Member destination) {
        var target = ClrTypeOf(context, destination);
        var rhs = ClrTypeOf(context, assignment.Value);
        if (target is null || rhs is null) return;
        var destDef = context.TypeDefinitions.GetTypeDefinition(target);
        var srcDef = context.TypeDefinitions.GetTypeDefinition(rhs);
        if (destDef is not null && srcDef is not null && destDef.IsAssignableFrom(srcDef)) {
            RewriteAssignmentConversion(context, assignment, destDef, srcDef);
            return;
        }
        var tc = CategoryOf(target);
        var rc = CategoryOf(rhs);
        if (tc is Cat.Unknown or Cat.Null || rc is Cat.Unknown or Cat.Null) return;
        if (tc is Cat.Enum && rc is Cat.Text) return;
        if (!Compatible(tc, target, rc, rhs, destThenSource: true))
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
            if (RewriteOperatorConversion(context, typeCast, typeCast.Operand, destDef, srcDef, implicitOnly: false))
                return;
            if (destDef.IsAssignableFrom(srcDef))
                return;
            if (srcDef.IsAssignableFrom(destDef))
                return;
            var srcCat = source is not null ? CategoryOf(source) : Cat.Unknown;
            var destCat = dest is not null ? CategoryOf(dest)
                : destDef is IClrTypeDefinition cd ? CategoryOf(cd.RuntimeType) : Cat.Unknown;
            bool srcNumeric = source is not null && IsNumericLike(source);
            bool destNumeric = dest is not null && IsNumericLike(dest);
            if ((srcNumeric && destNumeric)
                || (srcCat is Cat.Enum && destNumeric)
                || (srcNumeric && destCat is Cat.Enum)) {
                if (TryRewriteNumericConvert(context, typeCast, typeCast.Operand, destDef, srcDef))
                    return;
            }
        }
        if (source is null || dest is null || source == dest)
            return;

        var sc = CategoryOf(source);
        var dc = CategoryOf(dest);
        if (sc is Cat.Unknown or Cat.Null || dc is Cat.Unknown or Cat.Null)
            return;
        if (sc is Cat.Number && dc is Cat.Number) {
            if (destDef is not null && srcDef is not null
                && TryRewriteNumericConvert(context, typeCast, typeCast.Operand, destDef, srcDef))
                return;
            Report(context, typeCast,
                $"cannot convert '{source.Name}' to '{dest.Name}'");
            return;
        }
        if (dc is Cat.Enum && sc is Cat.Number)
            return;
        if (sc is Cat.Enum && dc is Cat.Number) {
            if (destDef is not null && srcDef is not null
                && TryRewriteNumericConvert(context, typeCast, typeCast.Operand, destDef, srcDef))
                return;
            Report(context, typeCast,
                $"cannot convert '{source.Name}' to '{dest.Name}'");
            return;
        }

        Report(context, typeCast,
            $"cannot convert '{source.Name}' to '{dest.Name}'");
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
        Replace(context, assignment, assignment with { Value = converted });
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
        if (destDef is IClrTypeDefinition destClr
            && srcDef is IClrTypeDefinition srcClr
            && destClr.RuntimeType.IsAssignableFrom(srcClr.RuntimeType))
            return null;
        if (destDef.GetConversionFrom(srcDef) is not { } conversion)
            return null;
        if (implicitOnly && conversion.Kind is not ConversionOperatorKind.Implicit)
            return null;
        var method = conversion.Method;
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
            // scalar into the object argument (`"a" + x`, `x` an int).
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

    private static bool RewriteDecimalArithmetic(
        AnalysisContext context, Node parent, Node left, Node right, ArithmeticKind kind) {
        var decimalDef = context.TypeDefinitions.GetTypeDefinition(typeof(decimal));
        if (decimalDef is null)
            return false;
        var name = kind switch {
            ArithmeticKind.Add => "Add",
            ArithmeticKind.Subtract => "Subtract",
            ArithmeticKind.Multiply => "Multiply",
            ArithmeticKind.Divide => "Divide",
            ArithmeticKind.Modulo => "Remainder",
            _ => null
        };
        if (name is null)
            return false;
        var leftArg = CoerceTo(context, left, decimalDef) ?? left;
        var rightArg = CoerceTo(context, right, decimalDef) ?? right;
        var leftType = context.GetResolvedType(leftArg)
            ?? context.TypeDefinitions.GetTypeDefinition(typeof(decimal));
        var rightType = context.GetResolvedType(rightArg)
            ?? context.TypeDefinitions.GetTypeDefinition(typeof(decimal));
        if (leftType is null || rightType is null)
            return false;
        var method = decimalDef.FindMatchingMethodOverloads(name, [leftType, rightType]).FirstOrDefault()
            ?? decimalDef.FindMatchingMethodOverloads(name, [decimalDef, decimalDef]).FirstOrDefault();
        if (method is null)
            return false;
        var invoke = StampStaticInvoke(context, method, leftArg, rightArg);
        Replace(context, parent, invoke);
        return true;
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
            if (context.GetMetadata<ValueRepresentationMetadata>(offset) is { } vr)
                context.SetMetadata(amount, vr);
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
        if (ConvertMethodName(underlying) is not { } name)
            return false;
        var convertDef = context.TypeDefinitions.GetTypeDefinition(typeof(Convert));
        if (convertDef is null)
            return false;
        Node source = value;
        var sourceDef = srcDef;
        if (srcClr is not null && IsIeeeFloating(srcClr) && IsIntegerType(underlying)) {
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
        if (context.GetMetadata<ValueRepresentationMetadata>(replacement) is { } vr)
            context.SetMetadata(original, vr);
    }

    private static bool IsIeeeFloating(Type type) =>
        type == typeof(double) || type == typeof(float);

    private static bool IsIntegerType(Type type) =>
        type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte)
        || type == typeof(sbyte) || type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort)
        || type == typeof(char);

    private static Node? CoerceTo(AnalysisContext context, Node value, ITypeDefinition destDef) {
        var srcDef = context.GetResolvedType(value)
            ?? (ClrTypeOf(context, value) is { } clr
                ? context.TypeDefinitions.GetTypeDefinition(clr)
                : null);
        if (srcDef is null)
            return null;
        if (destDef is IClrTypeDefinition d && srcDef is IClrTypeDefinition s
            && d.RuntimeType.IsAssignableFrom(s.RuntimeType))
            return value;
        if (TryConversionInvoke(context, value, destDef, srcDef, implicitOnly: true) is { } conv)
            return conv;
        var destClr = destDef.GetRuntimeType() ?? (destDef as IClrTypeDefinition)?.RuntimeType;
        if (destClr is not null && ConvertMethodName(destClr) is { } name) {
            var convertDef = context.TypeDefinitions.GetTypeDefinition(typeof(Convert));
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

    private static string? ConvertMethodName(Type dest) {
        if (dest == typeof(double)) return "ToDouble";
        if (dest == typeof(float)) return "ToSingle";
        if (dest == typeof(decimal)) return "ToDecimal";
        if (dest == typeof(int)) return "ToInt32";
        if (dest == typeof(long)) return "ToInt64";
        if (dest == typeof(short)) return "ToInt16";
        if (dest == typeof(byte)) return "ToByte";
        if (dest == typeof(sbyte)) return "ToSByte";
        if (dest == typeof(uint)) return "ToUInt32";
        if (dest == typeof(ulong)) return "ToUInt64";
        if (dest == typeof(ushort)) return "ToUInt16";
        if (dest == typeof(char)) return "ToChar";
        if (dest == typeof(bool)) return "ToBoolean";
        return null;
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
        var (kind, clr) = RepresentationOf(method.MemberTypeDefinition);
        context.SetMetadata(invoke, new ValueRepresentationMetadata(kind, clr));
        context.SetMetadata(member, new ValueRepresentationMetadata(kind, clr));
    }

    private static (ValueRepresentationKind Kind, Type? ClrType) RepresentationOf(ITypeDefinition typeDef) {
        if (typeDef is not IClrTypeDefinition clrType)
            return (ValueRepresentationKind.HeapRef, null);
        var rt = clrType.RuntimeType;
        if (Nullable.GetUnderlyingType(rt) is not null)
            return (ValueRepresentationKind.HeapRef, rt);
        if (rt == typeof(bool))
            return (ValueRepresentationKind.StackScalar, rt);
        if (rt.IsValueType || rt.IsPrimitive) {
            return RingCodec.IsInline(rt)
                ? (ValueRepresentationKind.StackScalar, rt)
                : (ValueRepresentationKind.HeapRef, rt);
        }
        return (ValueRepresentationKind.HeapRef, rt);
    }

    private void Report(AnalysisContext context, Node node, string message) =>
        context.ReportError(node, message, DiagnosticCode);

    private enum Cat { Text, Number, Boolean, Date, Enum, Guid, Null, Unknown }

    private static Type? ClrTypeOf(AnalysisContext context, Node node) =>
        context.GetMetadata<ValueRepresentationMetadata>(node)?.ClrType;

    private static Cat CategoryOf(Type type) {
        if (type == typeof(bool)) return Cat.Boolean;
        if (type == typeof(string) || type == typeof(char)) return Cat.Text;
        if (type == typeof(DateTime) || type == typeof(DateTimeOffset)
            || type == typeof(DateOnly) || type == typeof(TimeOnly) || type == typeof(TimeSpan))
            return Cat.Date;
        if (type.IsEnum) return Cat.Enum;
        if (type == typeof(Guid)) return Cat.Guid;
        if (type.IsPrimitive || type == typeof(decimal)) return Cat.Number;
        if (type.IsValueType && Nullable.GetUnderlyingType(type) is { } underlying)
            return CategoryOf(underlying);
        return Cat.Unknown;
    }

    private static bool Compatible(Cat lc, Type left, Cat rc, Type right, bool destThenSource = false) {
        if (lc == rc && lc is not Cat.Date) return true;
        if (lc == rc && lc is Cat.Date)
            return left == right || (IsClrTimestamp(left) && IsClrTimestamp(right));
        if (lc is Cat.Enum && rc is Cat.Text) return true;
        if (rc is Cat.Enum && lc is Cat.Text) return true;
        return false;
    }

    private static bool IsClrTimestamp(Type type) =>
        type == typeof(DateTime) || type == typeof(DateTimeOffset);
}

/// <summary>First assignment's representation for a <see cref="Variable"/> in this analysis.
/// Later writes must be slot-compatible or <see cref="SyntaxTypeCompatibilityAnalyzer"/> errors.</summary>
internal sealed record VariableAssignedTypeMetadata(
    Type? ClrType,
    ValueRepresentationKind Kind
) : IAnalysisMetadata;