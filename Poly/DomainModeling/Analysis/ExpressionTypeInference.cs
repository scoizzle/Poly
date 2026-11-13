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
//
// T-05a types the forms master left Unresolved: arithmetic (the CLR operator's result), path-prefix and binder
// reads (the final property's type), owned access (the owned type's member) and sum (DomainTypeModel.SumResult).
// A newly typed primitive carries no domain name (lock #679 6841065713, D1), so it meets the family rules only and
// no verdict flips in T-05a that T-05b flips back; an enum, entity or value type keeps its name.

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
        var type = InferType(context, expr, props, parameters, enumTypes);
        if (type.IsResolved || expr is not (Add or Subtract or Multiply or Divide))
            return type;
        // Numeric operands with no CLR operator (Float with Decimal) keep the Number family this path gave them
        // before T-05a; T-07 makes the operator table the gate.
        var operands = expr.Children.OfType<DomainExpression>()
            .Select(c => InferValueType(context, c, props, parameters, enumTypes)).ToList();
        return operands.All(IsNumeric) ? NumberFamily : ExprType.Unresolved;
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

        // Text + Text is concatenation and yields Text.
        if (expr is Add add
            && Family(InferType(context, add.Left, props, parameters, enumTypes)) is TypeFamily.Text
            && Family(InferType(context, add.Right, props, parameters, enumTypes)) is TypeFamily.Text)
            return TextFamily;
        if (expr is Add or Subtract or Multiply or Divide)
            return InferArithmetic(context, expr, props, parameters, enumTypes);

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
            SumExpr sum => InferSum(context, sum, props),
            RelationshipNavigation navigation => InferNavigation(context, navigation, props, enumTypes),
            OwnedAccess owned => InferOwned(context, owned, props, enumTypes),
            MatchExpr match => InferMatch(context, match, props, parameters, enumTypes),
            FunctionCall call => FunctionAnalysis.Find(context, call.Name) is { } function
                ? ResolveName(context, function.ReturnType.TypeName, enumTypes)
                : ExprType.Unresolved,
            _ => ExprType.Unresolved,
        };
    }

    // ── the forms T-05a types ───────────────────────────────────

    /// <summary>
    /// The property map an expression on an entity reads, plus that entity's name and the binders in scope (a
    /// <c>for</c> binder or a subscription's peer binder, each to its entity type), so a path-prefix or binder read
    /// can be typed. Every other caller passes a plain map, and those forms stay Unresolved there.
    /// </summary>
    private sealed class EntityScope : Dictionary<string, string> {
        public EntityScope(Entity entity)
            : base(entity.Properties.ToDictionary(p => p.Name, p => p.Type.TypeName, StringComparer.Ordinal), StringComparer.Ordinal) {
            EntityName = entity.Name;
            Binders = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        private EntityScope(EntityScope outer, string binder, string entityTypeName) : base(outer, StringComparer.Ordinal) {
            EntityName = outer.EntityName;
            Binders = new Dictionary<string, string>(outer.Binders, StringComparer.Ordinal) { [binder] = entityTypeName };
        }

        public string EntityName { get; }

        public IReadOnlyDictionary<string, string> Binders { get; }

        public EntityScope WithBinder(string binder, string entityTypeName) => new(this, binder, entityTypeName);
    }

    /// <summary>The scope with <paramref name="binder"/> bound to <paramref name="entityTypeName"/>, when both are known.</summary>
    private static Dictionary<string, string> WithBinder(
        Dictionary<string, string> props, string? binder, string? entityTypeName) =>
        props is EntityScope scope && binder is not null && entityTypeName is not null
            ? scope.WithBinder(binder, entityTypeName)
            : props;

    /// <summary>A primitive type with its domain name dropped (D1); an enum, entity, value type or unresolved name keeps it.</summary>
    private static ExprType WithoutPrimitiveName(ExprType type) =>
        Family(type) is TypeFamily.Text or TypeFamily.Number or TypeFamily.Boolean or TypeFamily.Date
            or TypeFamily.Time or TypeFamily.Duration or TypeFamily.Guid
            ? type with { DomainName = null }
            : type;

    /// <summary>
    /// Numeric arithmetic is the CLR operator's result (the lookup the stamp makes; InferType never reads the stamp
    /// back). Date plus or minus a number keeps the date's type, as the arithmetic arm accepts it. Anything else,
    /// and numeric operands with no operator (Float with Decimal), stays Unresolved: the arithmetic arm reports a
    /// non-numeric operand.
    /// </summary>
    private ExprType InferArithmetic(
        AnalysisContext context, DomainExpression expr,
        Dictionary<string, string>? props, Dictionary<string, string>? parameters,
        Dictionary<string, EnumType> enumTypes) {
        var operands = expr.Children.OfType<DomainExpression>().ToList();
        var left = InferType(context, operands[0], props, parameters, enumTypes);
        var right = InferType(context, operands[1], props, parameters, enumTypes);
        if (IsNumeric(left) && IsNumeric(right)) {
            var kind = expr switch {
                Add => Introspection.OperatorKind.Add,
                Subtract => Introspection.OperatorKind.Subtract,
                Multiply => Introspection.OperatorKind.Multiply,
                _ => Introspection.OperatorKind.Divide,
            };
            return left.Definition!.GetOperator(kind, right.Definition) is { } resolution
                ? new ExprType(resolution.Result, Optional: false, DomainName: null)
                : ExprType.Unresolved;
        }
        if (expr is Add or Subtract && IsDate(left) && IsNumeric(right))
            return left with { DomainName = null, Optional = false };
        return ExprType.Unresolved;
    }

    /// <summary>The entity declared under <paramref name="name"/>, first declaration first (the provider's order).</summary>
    private static Entity? EntityNamed(AnalysisContext context, string? name) =>
        name is null
            ? null
            : context.GetTypeLookup()?.Domain.Types.OfType<Entity>()
                .FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.Ordinal));

    /// <summary>The target of <paramref name="entity"/>'s to-one navigation <paramref name="name"/>; null for a many or unknown one.</summary>
    private static string? ToOneTarget(Entity entity, string name) {
        var navigation = entity.Navigations.FirstOrDefault(n => string.Equals(n.Name, name, StringComparison.Ordinal));
        return navigation?.Cardinality is RelationshipCardinality.OneToOne or RelationshipCardinality.ManyToOne
            ? navigation.Target.TypeName
            : null;
    }

    /// <summary>The type of <paramref name="entity"/>'s property <paramref name="name"/>, with D1's naming.</summary>
    private static ExprType MemberType(
        AnalysisContext context, IEnumerable<Property> properties, string name, Dictionary<string, EnumType> enumTypes) =>
        properties.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal)) is { } property
            ? WithoutPrimitiveName(ResolveName(context, property.Type.TypeName, enumTypes))
            : ExprType.Unresolved;

    /// <summary>
    /// A path-prefix read (<c>part Qty</c>, to-one hops <c>loan book Title</c>) or a binder read (<c>line Qty</c>,
    /// a peer's <c>o Code</c>): the final property's type. A many hop, an unknown hop or property, or a scope with no
    /// entity stays Unresolved; the policy, effect and subscription passes report those shapes.
    /// </summary>
    private static ExprType InferNavigation(
        AnalysisContext context, RelationshipNavigation navigation,
        Dictionary<string, string>? props, Dictionary<string, EnumType> enumTypes) {
        if (props is not EntityScope scope) return ExprType.Unresolved;
        var target = scope.Binders.TryGetValue(navigation.RelationshipName, out var bound)
            ? bound
            : EntityNamed(context, scope.EntityName) is { } entity ? ToOneTarget(entity, navigation.RelationshipName) : null;
        var hop = navigation.TargetProperty;
        while (EntityNamed(context, target) is { } current) {
            switch (hop) {
                case PropertyAccess property:
                    return MemberType(context, current.Properties, property.Name, enumTypes);
                case RelationshipNavigation next:
                    target = ToOneTarget(current, next.RelationshipName);
                    hop = next.TargetProperty;
                    continue;
                default:
                    return ExprType.Unresolved;
            }
        }
        return ExprType.Unresolved;
    }

    /// <summary>
    /// An owned access <c>Owned Member</c> (IR only; lowered as <c>this.Owned.Member</c>): the member's type on the
    /// value type of the entity's <c>Owned</c> property, or on the target of its to-one navigation <c>Owned</c>.
    /// The value type is the first declared under that name, as the provider resolves it. Anything else stays
    /// Unresolved; <c>PolicyConstraintAnalyzer</c> reports an unknown owned name.
    /// </summary>
    private static ExprType InferOwned(
        AnalysisContext context, OwnedAccess owned,
        Dictionary<string, string>? props, Dictionary<string, EnumType> enumTypes) {
        if (props is not EntityScope scope
            || EntityNamed(context, scope.EntityName) is not { } entity
            || owned.Inner is not PropertyAccess member)
            return ExprType.Unresolved;
        if (entity.Properties.FirstOrDefault(p => string.Equals(p.Name, owned.OwnedName, StringComparison.Ordinal)) is { } property) {
            return context.GetTypeLookup()?.Domain.Types.OfType<Ontology.ValueType>()
                .FirstOrDefault(v => string.Equals(v.Name, property.Type.TypeName, StringComparison.Ordinal)) is { } valueType
                ? MemberType(context, valueType.Properties, member.Name, enumTypes)
                : ExprType.Unresolved;
        }
        return EntityNamed(context, ToOneTarget(entity, owned.OwnedName)) is { } target
            ? MemberType(context, target.Properties, member.Name, enumTypes)
            : ExprType.Unresolved;
    }

    /// <summary>
    /// <c>sum Rel P</c>: <see cref="DomainTypeModel.SumResult"/> of <c>P</c> on the relationship's target. The type is
    /// stated on the node as <see cref="TypedExpressionMetadata"/>; its readers are the analysis-and-lowering agreement
    /// tests (<c>AnalysisGateParityTests.Sum_UnderClrOverride_AnalysisAndLoweringAgree</c> and
    /// <c>Sum_WithoutOverride_AnalysisAndLoweringAgreeOnDecimal</c>), which compare it with lowering's seed; no
    /// production pass reads it (lowering recomputes <c>SumResult</c>). A sum this cannot type (no entity scope, an
    /// unknown relationship or property, a non-numeric property) is the Number family it was before T-05a;
    /// <c>PolicyConstraintAnalyzer</c> reports the bad shapes.
    /// </summary>
    private static ExprType InferSum(AnalysisContext context, SumExpr sum, Dictionary<string, string>? props) {
        if (props is EntityScope scope
            && EntityNamed(context, scope.EntityName) is { } entity
            && EntityNamed(context, entity.Navigations
                .FirstOrDefault(n => string.Equals(n.Name, sum.RelationshipName, StringComparison.Ordinal))?.Target.TypeName) is { } target
            && target.Properties.FirstOrDefault(p => string.Equals(p.Name, sum.PropertyName, StringComparison.Ordinal)) is { } property
            && ModelOf(context) is { } model
            && model.SumResult(model.OfProperty(property)) is { IsResolved: true } result) {
            context.SetMetadata(sum, new TypedExpressionMetadata(result));
            return result;
        }
        return NumberFamily;
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
    private static DomainTypeModel? ModelOf(AnalysisContext context) =>
        context.GetTypeLookup()?.Domain is { } domain ? DomainTypeModel.Of(context, domain) : null;

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
        if (enumTypes.TryGetValue(name, out var declaredEnum)) {
            if (model?.Resolve(name) is { IsResolved: true } enumType && Family(enumType) is TypeFamily.Enum)
                return enumType;
            // The model resolves a name first-wins, so an entity declared before an enum of the same name
            // hides it; the declared enum is still the enum (as it was before the move).
            if (context.GetTypeLookup()?.Domain is { } domain
                && new DomainTypeModel(new Domain(domain.Name, [declaredEnum]), RuntimeAnalysisCache.Session(domain).TypeMaps)
                    .Resolve(name) is { IsResolved: true } own)
                return own;
            return new ExprType(null, false, name);
        }
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