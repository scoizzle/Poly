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
// The representation (T-04): a whole-number literal, Count, Sum, arithmetic results and
// Text + Text carry NO domain name (DomainName == null) with a family definition; a decimal literal carries
// "Decimal"; a declared or allowlisted name carries that name. Compatible applies the name rule only when
// both names are non-null, exactly as before the move.
//
// T-05a types the forms master left Unresolved: arithmetic (the CLR operator's result), path-prefix and binder
// reads (the final property's type), owned access (the owned type's member) and sum (DomainTypeModel.SumResult).
// A newly typed primitive carries no domain name (lock #679 6841065713, D1), so it meets the family rules only and
// no verdict flips in T-05a that T-05b flips back; an enum, entity or value type keeps its name.
//
// T-05b types what still kept working only through leniency (a date difference, a bare enum member, the Guid
// keyword, a navigation whose target is an expression, a path rooted at an entity-typed parameter), and an
// expression it still cannot type is Unresolved: never compatible, recorded by its consumer for DMSEM006.

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

    private ExprType InferLiteralAware(
        AnalysisContext context,
        DomainExpression expr, string targetTypeName, Dictionary<string, EnumType> enumTypes,
        Dictionary<string, string>? props = null, Dictionary<string, string>? parameters = null) {
        // For the assign RHS / default check, a bare enum-member identifier (PropertyAccess)
        // is valid when the target is enum-typed and the name is a member.
        var target = ResolveName(context, targetTypeName, enumTypes);
        if (expr is PropertyAccess pa && Family(target) is TypeFamily.Enum
            && ResolvePropertyType(pa.Name, props, parameters) is null
            && enumTypes.TryGetValue(targetTypeName, out var enumType)
            && enumType.MemberNames.Contains(pa.Name, StringComparer.Ordinal))
            return target;
        if (expr is MatchExpr match)
            return InferMatch(context, match, props, parameters, enumTypes, target);
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
                : pa.Name is "Guid" ? ResolveName(context, "Guid", enumTypes)
                : ReferencedEntity(context, pa.Name, props) is { } entityName ? ResolveName(context, entityName, enumTypes)
                : ExprType.Unresolved,
            ParameterAccess pa => parameters?.TryGetValue(pa.Name, out var pt) == true
                ? ResolveName(context, pt, enumTypes)
                : ExprType.Unresolved,
            Literal { Value: null } => ExprType.NullLiteral,
            Literal { Value: string } => TextFamily,
            Literal { Value: bool } => BooleanFamily,
            Literal { Value: decimal } => DecimalLiteral,
            Literal { Value: long or int or double or float or short or byte } => NumberFamily,
            Exists or NotExists or Comparison or And or Or or Not => BooleanFamily,
            // A10: a quantifier or count is typed only where it has an entity to range over; a function body has none.
            AnyExpr or AllExpr or NoneExpr => QuantifiedEntity(context, expr, props) is not null ? BooleanFamily : ExprType.Unresolved,
            CountExpr => QuantifiedEntity(context, expr, props) is not null ? NumberFamily : ExprType.Unresolved,
            SumExpr sum => InferSum(context, sum, props),
            RelationshipNavigation navigation => InferNavigation(context, navigation, props, parameters, enumTypes),
            OwnedAccess owned => InferOwned(context, owned, props, enumTypes),
            MatchExpr match => InferMatch(context, match, props, parameters, enumTypes, hint: null),
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
    /// back). Date plus or minus a number keeps the date's type, as the arithmetic arm accepts it; a date minus a
    /// date of one granularity is a duration. Anything else, and numeric operands with no operator (Float with
    /// Decimal), stays Unresolved: the arithmetic arm reports a non-numeric operand, and a consumer of the result
    /// records it for DMSEM006.
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
        // A date difference of one granularity is a duration (the shape rule stays in WalkExpression: T-10).
        if (expr is Subtract && IsDate(left) && IsDate(right) && !MixedDateGranularity(left, right))
            return Typeless(typeof(TimeSpan));
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
    /// A path-prefix read (<c>part Qty</c>, to-one hops <c>loan book Title</c>), a binder read (<c>line Qty</c>, a peer's
    /// <c>o Code</c>) or a read rooted at an entity-typed parameter (<c>lead Name</c>): the final property's type. A hop
    /// whose target is an expression (<c>line Qty &gt; 1</c>, <c>advisor where Age &lt; mentor Age</c>) is that
    /// expression's type in the target entity's scope. A many hop, an unknown hop or property, or a scope with no entity
    /// stays Unresolved; the policy, effect and subscription passes report those shapes.
    /// </summary>
    private ExprType InferNavigation(
        AnalysisContext context, RelationshipNavigation navigation,
        Dictionary<string, string>? props, Dictionary<string, string>? parameters, Dictionary<string, EnumType> enumTypes) {
        var target = NavigationRoot(context, navigation.RelationshipName, props, parameters);
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
                    return WithoutPrimitiveName(InferType(context, hop, new EntityScope(current), null, enumTypes));
            }
        }
        return ExprType.Unresolved;
    }

    /// <summary>
    /// A bare name that is a binder in scope or the scope entity's to-one navigation, read as a value (an invoke
    /// argument <c>item: widget</c>): the entity it refers to. Null otherwise.
    /// </summary>
    private static string? ReferencedEntity(AnalysisContext context, string name, Dictionary<string, string>? props) =>
        props is not EntityScope scope
            ? null
            : scope.Binders.TryGetValue(name, out var bound)
                ? bound
                : EntityNamed(context, scope.EntityName) is { } entity ? ToOneTarget(entity, name) : null;

    /// <summary>
    /// The entity a navigation's first hop reaches: a binder in scope, the scope entity's to-one navigation, or an action
    /// parameter whose type is an entity. Null when none applies.
    /// </summary>
    private static string? NavigationRoot(
        AnalysisContext context, string name, Dictionary<string, string>? props, Dictionary<string, string>? parameters) {
        if (props is EntityScope scope) {
            if (scope.Binders.TryGetValue(name, out var bound)) return bound;
            if (EntityNamed(context, scope.EntityName) is { } entity && ToOneTarget(entity, name) is { } toOne) return toOne;
        }
        return parameters?.TryGetValue(name, out var parameterType) == true && EntityNamed(context, parameterType) is not null
            ? parameterType
            : null;
    }

    /// <summary>
    /// The entity a quantifier, <c>count</c> or <c>sum</c> ranges over: the target of the scope entity's relationship.
    /// Null without an entity scope (a function body) or for an unknown relationship (A10).
    /// </summary>
    private static Entity? QuantifiedEntity(AnalysisContext context, DomainExpression expr, Dictionary<string, string>? props) {
        var relationship = expr switch {
            AnyExpr a => a.RelationshipName,
            AllExpr a => a.RelationshipName,
            NoneExpr n => n.RelationshipName,
            CountExpr c => c.RelationshipName,
            SumExpr s => s.RelationshipName,
            _ => null,
        };
        return relationship is not null && props is EntityScope scope && EntityNamed(context, scope.EntityName) is { } entity
            ? EntityNamed(context, entity.Navigations
                .FirstOrDefault(n => string.Equals(n.Name, relationship, StringComparison.Ordinal))?.Target.TypeName)
            : null;
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
    /// unknown relationship or property, a non-numeric property) is Unresolved (T-05b, #846): the shared sum check
    /// reports a non-numeric or unknown property in every position, and a consumer records the rest for DMSEM006.
    /// </summary>
    private static ExprType InferSum(AnalysisContext context, SumExpr sum, Dictionary<string, string>? props) {
        if (QuantifiedEntity(context, sum, props) is { } target
            && target.Properties.FirstOrDefault(p => string.Equals(p.Name, sum.PropertyName, StringComparison.Ordinal)) is { } property
            && ModelOf(context) is { } model
            && model.SumResult(model.OfProperty(property)) is { IsResolved: true } result) {
            context.SetMetadata(sum, new TypedExpressionMetadata(result));
            return result;
        }
        return ExprType.Unresolved;
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
    /// Resolves a domain type name through the run's type model (<see cref="DomainTypeModel.Resolve"/>): the one
    /// resolver, so a pack override or a lowercase CLR spelling means here what it means to lowering (fork B). A name
    /// the model cannot resolve keeps its name with no definition: untyped, never compatible (T-05b).
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
        return model?.Resolve(name) is { IsResolved: true } modeled ? modeled : new ExprType(null, false, name);
    }
}