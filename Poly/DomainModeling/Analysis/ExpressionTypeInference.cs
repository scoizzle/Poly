using Poly.DomainModeling.Meaning;
using Poly.DomainModeling.Ontology;
using Poly.Introspection.CommonLanguageRuntime;

using Add = Poly.DomainModeling.Ontology.Add;
using And = Poly.DomainModeling.Ontology.And;
using Divide = Poly.DomainModeling.Ontology.Divide;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using Not = Poly.DomainModeling.Ontology.Not;
using Or = Poly.DomainModeling.Ontology.Or;
using Subtract = Poly.DomainModeling.Ontology.Subtract;

namespace Poly.DomainModeling.Analysis;

// Type inference: what an expression's type is, as an ExprType over the type model.
//
// The representation (T-04): a whole-number literal, Count, Sum, InferValueType arithmetic results and
// Text + Text carry NO domain name (DomainName == null) with a family definition; a decimal literal carries
// "Decimal"; a declared or allowlisted name carries that name. Compatible applies the name rule only when
// both names are non-null, exactly as before the move.

internal sealed partial class ExpressionTypeAnalyzer : INodeAnalyzer {
    // ── typeless family types ───────────────────────────────────

    private static ExprType Typeless(Type clrType) =>
        new(ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(clrType)
            ?? throw new InvalidOperationException($"No CLR definition for '{clrType}'."),
            Optional: false, DomainName: null);

    private static ExprType NumberFamily => Typeless(typeof(long));

    private static ExprType TextFamily => Typeless(typeof(string));

    private static ExprType BooleanFamily => Typeless(typeof(bool));

    private static ExprType DecimalLiteral => Named(typeof(decimal), "Decimal");

    private static ExprType Named(Type clrType, string name) => Typeless(clrType) with { DomainName = name };

    /// <summary>
    /// <see cref="InferType"/> plus the arithmetic result types it leaves Unknown: numeric
    /// arithmetic is Number, and Text + Text is Text. Used for call arguments and function bodies.
    /// </summary>
    private ExprType InferValueType(
        AnalysisContext context, DomainExpression expr,
        Dictionary<string, string>? props, Dictionary<string, string>? parameters,
        Dictionary<string, EnumType> enumTypes) {
        if (expr is Add or Subtract or Multiply or Divide) {
            var operands = expr.Children.OfType<DomainExpression>()
                .Select(c => InferValueType(context, c, props, parameters, enumTypes)).ToList();
            if (operands.All(IsNumeric))
                return NumberFamily;
            if (expr is Add && operands.All(o => Family(o) is TypeFamily.Text))
                return TextFamily;
            return ExprType.Unresolved;
        }
        return InferType(context, expr, props, parameters, enumTypes);
    }

    /// <summary>Infers the type of a binder-rooted argument expression: a binder-root property
    /// access, or arithmetic (numeric/numeric, date/number) over it. Non-scalar binder roots
    /// (nested path-prefix) resolve to Unknown.</summary>
    private static ExprType InferBinderExpressionType(
        AnalysisContext context, string? targetEntityName, string binderName,
        DomainExpression expr, Dictionary<string, EnumType> enumTypes) {
        switch (expr) {
            case RelationshipNavigation rn when string.Equals(rn.RelationshipName, binderName, StringComparison.Ordinal):
                if (rn.TargetProperty is not PropertyAccess pa) return ExprType.Unresolved;
                var propType = ResolveTargetPropType(context, targetEntityName, pa.Name);
                return propType is null ? ExprType.Unresolved : ResolveName(context, propType, enumTypes);
            case Add or Subtract or Multiply or Divide:
                var operandTypes = expr.Children.OfType<DomainExpression>()
                    .Select(c => InferBinderExpressionType(context, targetEntityName, binderName, c, enumTypes))
                    .ToList();
                if (operandTypes.Any(t => Family(t) is TypeFamily.Unknown)) return ExprType.Unresolved;
                if (operandTypes.All(IsNumeric)) return NumberFamily;
                if (operandTypes.Count == 2 && IsDate(operandTypes[0]) && IsNumeric(operandTypes[1]))
                    return operandTypes[0];
                // Mixed non-numeric binder prop + number (line Status + 1): surface the
                // non-numeric type so the param-type mismatch fires instead of passing.
                return operandTypes.First(t => !IsNumeric(t));
            case Literal { Value: long or int or double or float or decimal or short or byte }:
                return NumberFamily;
            case Literal { Value: string }:
                return TextFamily;
            default:
                return ExprType.Unresolved;
        }
    }

    private static string? ResolveTargetPropType(AnalysisContext context, string? targetEntityName, string propName) {
        if (targetEntityName is null) return null;
        var lookup = context.GetTypeLookup();
        var entity = lookup?.Domain?.Types.OfType<Entity>()
            .FirstOrDefault(e => string.Equals(e.Name, targetEntityName, StringComparison.Ordinal));
        return entity?.Properties.FirstOrDefault(p => string.Equals(p.Name, propName, StringComparison.Ordinal))?.Type.TypeName;
    }

    private ExprType InferLiteralAware(
        AnalysisContext context,
        DomainExpression expr, string targetTypeName, Dictionary<string, EnumType> enumTypes,
        Dictionary<string, string>? props = null, Dictionary<string, string>? parameters = null) {
        // For the assign RHS / default check, a bare enum-member identifier (PropertyAccess)
        // is valid when the target is enum-typed and the name is a member.
        if (expr is PropertyAccess pa && Family(ResolveName(context, targetTypeName, enumTypes)) is TypeFamily.Enum) {
            if (enumTypes.TryGetValue(targetTypeName, out var enumType)
                && enumType.MemberNames.Contains(pa.Name, StringComparer.Ordinal))
                return ResolveName(context, targetTypeName, enumTypes);
        }
        return InferType(context, expr, props, parameters, enumTypes);
    }

    private ExprType InferType(
        AnalysisContext context,
        DomainExpression expr,
        Dictionary<string, string>? props,
        Dictionary<string, string>? parameters,
        Dictionary<string, EnumType> enumTypes) {
        expr = RewriteLibraryIdent(expr);
        if (context.GetMetadata<TypedExpressionMetadata>(expr) is { Binary: null } stamped
            && stamped.Type.DomainName is not null)
            return ResolveName(context, stamped.Type.DomainName, enumTypes);

        if (_meaning.Inference.TryDispatch(
                expr,
                child => InferType(context, child, props, parameters, enumTypes).DomainName ?? "",
                out var inferredName)
            && !string.IsNullOrEmpty(inferredName)) {
            var inferred = ResolveName(context, inferredName, enumTypes);
            context.SetMetadata(expr, new TypedExpressionMetadata(inferred));
            return inferred;
        }

        // Text + Text is concatenation and yields Text; every other arithmetic result stays Unknown here.
        if (expr is Add add
            && Family(InferType(context, add.Left, props, parameters, enumTypes)) is TypeFamily.Text
            && Family(InferType(context, add.Right, props, parameters, enumTypes)) is TypeFamily.Text)
            return TextFamily;

        return expr switch {
            PropertyAccess pa => ResolvePropertyType(pa.Name, props, parameters) is { } pt
                ? ResolveName(context, pt, enumTypes)
                : ExprType.Unresolved,
            ParameterAccess pa => parameters?.TryGetValue(pa.Name, out var pt) == true
                ? ResolveName(context, pt, enumTypes)
                : ExprType.Unresolved,
            Literal { Value: null } => ExprType.NullLiteral,
            Literal { Value: string } => TextFamily,
            Literal { Value: bool } => BooleanFamily,
            Literal { Value: decimal } => DecimalLiteral,
            Literal { Value: long or int or double or float or short or byte } => NumberFamily,
            Exists or NotExists or AnyExpr or AllExpr or NoneExpr or Comparison or And or Or or Not => BooleanFamily,
            CountExpr => NumberFamily,
            SumExpr => NumberFamily,
            MatchExpr match => InferMatch(context, match, props, parameters, enumTypes),
            FunctionCall call => FunctionAnalysis.Find(context, call.Name) is { } function
                ? ResolveName(context, function.ReturnType.TypeName, enumTypes)
                : ExprType.Unresolved,
            _ => ExprType.Unresolved,
        };
    }

    private static string? ResolvePropertyType(string name, Dictionary<string, string>? props, Dictionary<string, string>? parameters) {
        if (props?.TryGetValue(name, out var pt) == true) return pt;
        if (parameters?.TryGetValue(name, out var ptype) == true) return ptype;
        return null;
    }

    // ── name resolution (the one resolver) ──────────────────────

    /// <summary>
    /// The domain type model of this run: stamped on the <see cref="Domain"/> node by <see cref="Analyze"/>;
    /// resolved lazily from the type lookup's domain for a walk that did not start at the Domain root, so
    /// there is one resolver. Null when there is no domain at all.
    /// </summary>
    private static DomainTypeModel? ModelOf(AnalysisContext context) {
        if (context.GetTypeLookup()?.Domain is not { } domain) return null;
        if (context.GetMetadata<DomainTypeModelMetadata>(domain) is { } stamped) return stamped.Model;
        var model = new DomainTypeModel(domain, RuntimeAnalysisCache.Session(domain).TypeMaps);
        context.SetMetadata(domain, new DomainTypeModelMetadata(model));
        return model;
    }

    /// <summary>
    /// Resolves a domain type name. Order is the order the category rules had: a declared enum, a declared
    /// primitive (typed by its semantics, never by the temporal map), the legacy allowlist, then the model's
    /// entity and value type definitions. Anything else keeps its name with no definition (named-unresolved):
    /// the lenient case, which includes the lowercase CLR spellings. T-05 removes the allowlist and the
    /// named-unresolved leniency.
    /// </summary>
    private static ExprType ResolveName(
        AnalysisContext context, string name, Dictionary<string, EnumType> enumTypes) {
        var model = ModelOf(context);
        if (enumTypes.ContainsKey(name))
            return model?.Resolve(name) is { IsResolved: true } enumType ? enumType : new ExprType(null, false, name);
        var declared = context.GetTypeLookup()?.Domain.Types.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));
        if (declared is Ontology.PrimitiveType primitive)
            return Named(ClrTypeOf(primitive.Semantics), name);
        if (LegacyClrType(name) is { } legacy)
            return Named(legacy, name);
        if (declared is Entity or Ontology.ValueType && model?.Resolve(name) is { IsResolved: true } modeled)
            return modeled;
        return new ExprType(null, false, name);
    }

    private static Type? LegacyClrType(string typeName) => typeName switch {
        "Text" or "String" => typeof(string),
        "Number" or "Int" or "Int64" => typeof(long),
        "Int32" => typeof(int),
        "Decimal" => typeof(decimal),
        "Float" or "Double" => typeof(double),
        "Boolean" or "Bool" => typeof(bool),
        "Uuid" or "Guid" => typeof(Guid),
        _ => null,
    };

    private static Type ClrTypeOf(PrimitiveSemantics semantics) => semantics switch {
        PrimitiveSemantics.Boolean => typeof(bool),
        PrimitiveSemantics.Number => typeof(long),
        PrimitiveSemantics.Decimal => typeof(decimal),
        PrimitiveSemantics.Text => typeof(string),
        PrimitiveSemantics.Uuid => typeof(Guid),
        PrimitiveSemantics.Binary => typeof(byte[]),
        PrimitiveSemantics.Date => typeof(DateOnly),
        // DateTime keeps the Date family; granularity is compared through the definition.
        PrimitiveSemantics.DateTime => typeof(DateTime),
        PrimitiveSemantics.Time => typeof(TimeOnly),
        PrimitiveSemantics.Duration => typeof(TimeSpan),
        _ => throw new InvalidOperationException($"Unmapped primitive semantics '{semantics}'."),
    };
}
