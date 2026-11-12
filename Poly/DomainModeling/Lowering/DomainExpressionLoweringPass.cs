using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Dispatch;
using Poly.DomainModeling.Meaning;
using Poly.DomainModeling.Ontology;

using SN = Poly.Ast.Nodes;

namespace Poly.DomainModeling.Lowering;

/// <summary>
/// Lowers a DomainExpression tree into the shared Syntax AST
/// (<see cref="Syntax.Nodes"/>), which the language VM executes and
/// CSharpWriter prints.
/// </summary>
/// <remarks>
/// Domain-specific nodes (OwnedAccess, RelationshipNavigation)
/// are structurally unfolded into nested Member chains.
/// Existence queries (Exists/NotExists) become null comparisons.
/// Arithmetic, boolean, and comparison nodes map 1:1 to their Syntax AST
/// counterparts. Filtered quantifiers become foreach loops whose statements
/// sit immediately before the statement that reads the result.
/// </remarks>
public sealed partial class DomainExpressionLoweringPass : DomainExpressionDispatch<LoweredExpression> {
    private readonly LoweringContext _context;
    private readonly IReadOnlyDictionary<string, Node> _parameters;
    private readonly HashSet<string>? _actionParameterNames;
    private readonly Func<string, string>? _navigationNameResolver;
    private readonly Func<string, bool>? _isCollectionNavigation;
    private readonly Func<string, string?>? _propertyTypeResolver;
    private readonly Domain _domain;
    private readonly INodeMetadataProvider _analysis;
    private readonly ExpressionMeaning _meaning;
    private readonly ExpressionFormRegistry? _forms;
    private Node _currentSubject = null!;
    private readonly LocalNames _names;
    private IReadOnlyList<string> _activeCalls = [];

    /// <summary>
    /// Creates a pass using context from a <see cref="LoweringContext"/>.
    /// The instance root is the <c>subject</c> passed to <see cref="LowerExpression"/>
    /// and <see cref="Lower"/> (<see cref="LoweringContext.Subject"/> is not read
    /// by this pass). Names in
    /// <see cref="LoweringContext.ActionParameterNames"/> render as bare parameters
    /// instead of <c>this.name</c>.
    /// <see cref="LoweringContext.NavigationNameResolver"/> maps DSL relationship
    /// names to generated member names (pascal-cased navs).
    /// <see cref="LoweringContext.SourceEntityName"/> is the entity used to resolve
    /// relationship targets and enum literals. For path-prefix hops and quantifier
    /// bodies, only that name and the enum map switch to the target entity; the
    /// property-type, navigation-name and collection resolvers stay scoped to the
    /// source entity, and binder roots keep the source name and enum map.
    /// </summary>
    public DomainExpressionLoweringPass(LoweringContext context) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Analysis);
        ArgumentNullException.ThrowIfNull(context.Domain);
        _context = context.Names is null ? context with { Names = new LocalNames() } : context;
        _names = _context.Names!;
        _parameters = context.Parameters ?? new Dictionary<string, Node>();
        _actionParameterNames = context.ActionParameterNames;
        _navigationNameResolver = context.NavigationNameResolver;
        _isCollectionNavigation = context.IsCollectionNavigation;
        _propertyTypeResolver = context.PropertyTypeResolver;
        _domain = context.Domain;
        _analysis = context.Analysis;
        _meaning = context.Meaning ?? ExpressionMeaning.Empty;
        _forms = context.Forms;
    }

    private ExpressionMeaning EffectiveMeaning {
        get {
            if (_meaning.Lowering.Handlers.Count > 0)
                return _meaning;
            return RuntimeAnalysisCache.MeaningFor(_domain);
        }
    }

    private ExpressionFormRegistry EffectiveForms =>
        _forms ?? RuntimeAnalysisCache.FormsFor(_domain);

    /// <summary>
    /// Lowers <paramref name="expression"/> to statements plus a value, using
    /// <paramref name="subject"/> as the current-instance root. Callers that emit
    /// a surrounding statement place the statements immediately before it via
    /// <see cref="LoweredExpression.Before"/>.
    /// </summary>
    public LoweredExpression LowerExpression(DomainExpression expression, Node subject) {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(subject);
        _currentSubject = subject;
        return Route(expression);
    }

    /// <summary>
    /// Lowers the expression to a single Syntax node for callers that compile it
    /// on its own (policy Evaluate, VM). When the expression produced statements,
    /// they wrap the value in a Block.
    /// </summary>
    public Node Lower(DomainExpression expression, Node subject) {
        var lowered = LowerExpression(expression, subject);
        return lowered.Statements.Count == 0
            ? lowered.Value
            : new Block([.. lowered.Statements, lowered.Value], lowered.Variables);
    }

    protected override LoweredExpression Default() => throw new NotSupportedException(
        $"DomainExpression node type is not supported");

    /// <summary>Pascal-cases a relationship hop name the resolver did not map
    /// (nested navs on target entities); uses the resolver's mapping when present.</summary>
    private string ResolveNavName(string name) {
        var resolved = _navigationNameResolver?.Invoke(name) ?? name;
        return resolved == name ? DomainToCSharpExporter.ToPascalCase(name) : resolved;
    }

    private static bool IsPathPrefixPredicate(DomainExpression expr) => expr switch {
        Ontology.Comparison or Ontology.And or Ontology.Or or Ontology.Not
            or Ontology.Exists or Ontology.NotExists
            or Ontology.AnyExpr or Ontology.AllExpr or Ontology.NoneExpr => true,
        RelationshipNavigation inner => IsPathPrefixPredicate(inner.TargetProperty),
        _ => false,
    };

    private LoweredExpression Route(DomainExpression expr, Node subject) {
        var saved = _currentSubject;
        _currentSubject = subject;
        try { return Route(expr); }
        finally { _currentSubject = saved; }
    }

    private bool IsCollectionNav(string name) =>
        _isCollectionNavigation?.Invoke(name) == true;

    private string? ResolveRelationshipTarget(string relationshipName) {
        if (_context.SourceEntityName is null)
            return null;
        var source = _domain.Types.OfType<Entity>().FirstOrDefault(e =>
            string.Equals(e.Name, _context.SourceEntityName, StringComparison.Ordinal));
        var rel = source?.Navigations.FirstOrDefault(n =>
            string.Equals(n.Name, relationshipName, StringComparison.Ordinal));
        return rel?.Target.TypeName;
    }

    /// <summary>
    /// Lowers <paramref name="expression"/> against <paramref name="subject"/> using
    /// <paramref name="entityName"/> as the entity whose members the subject exposes
    /// (that entity's SourceEntityName and enum map). Path-prefix hops and quantifier
    /// bodies share this so nested resolution sees the target. Shares this pass's
    /// local-name generator.
    /// </summary>
    private LoweredExpression LowerAgainstEntity(
        string? entityName, DomainExpression expression, Node subject) {
        IReadOnlyDictionary<string, string>? enums = _context.EnumPropertyNames;
        if (entityName is not null) {
            var targetEntity = _domain.Types.OfType<Entity>().FirstOrDefault(e =>
                string.Equals(e.Name, entityName, StringComparison.Ordinal));
            if (targetEntity is not null)
                enums = DomainToCSharpExporter.GetEnumPropertyNames(
                    targetEntity, _domain, _analysis);
        }
        var nested = new DomainExpressionLoweringPass(_context with {
            EnumPropertyNames = enums,
            SourceEntityName = entityName ?? _context.SourceEntityName
        });
        return nested.LowerExpression(expression, subject);
    }

    /// <summary>
    /// Lowers <paramref name="value"/> into <paramref name="target"/>'s type — the one
    /// place a value-to-target-type decision is made for an expression. Callers use it
    /// *instead of* <see cref="LowerExpression"/>, never after it. Order: a
    /// <see cref="MatchExpr"/> builds its chain against the target; a clock/session fold
    /// adapts to the target; an enum member (string literal or bare identifier) becomes
    /// qualified member access unless it shadows a property or a parameter; anything
    /// else lowers normally and is adapted at the node level (Decimal literals).
    /// An unresolvable target leaves the value untouched.
    /// </summary>
    internal LoweredExpression AdaptTo(DomainExpression value, DomainTypeReference target, Node subject) {
        var saved = _currentSubject;
        _currentSubject = subject;
        try { return Adapt(value, target); }
        finally { _currentSubject = saved; }
    }

    private LoweredExpression Adapt(DomainExpression value, DomainTypeReference target) {
        if (value is MatchExpr match)
            return LowerMatch(match, target);

        if (EffectLoweringPass.LowerDefaultExpression(
                value, new NamedTypeReference(target.TypeName), EffectiveMeaning, EffectiveForms) is { } runtime)
            return LoweredExpression.Of(TargetTypeAdapter.Adapt(runtime, target, _context));

        if (DomainToCSharpExporter.TryResolveEnumType(_domain, _analysis, target.TypeName, out var enumType)
            && enumType is not null) {
            // String-literal member: Kind: "Keyword".
            if (value is Literal { Value: string member } && member.Length > 0
                && enumType.MemberNames.Contains(member, StringComparer.Ordinal))
                return LoweredExpression.Of(new Member(new NamedTypeReference(enumType.Name), member));
            // Bare identifier member: Kind: Keyword. A name that shadows a property or
            // a parameter stays an instance read (rows 7 and 8 of #495's guard set).
            if (value is PropertyAccess access && !Shadows(access.Name)
                && enumType.MemberNames.Contains(access.Name, StringComparer.Ordinal))
                return LoweredExpression.Of(new Member(new NamedTypeReference(enumType.Name), access.Name));
        }

        var lowered = Route(value);
        return lowered with { Value = TargetTypeAdapter.Adapt(lowered.Value, target, _context) };
    }

    /// <summary>
    /// The enum type of the other side of a comparison, when that side reads an
    /// enum-typed property. A null or empty <see cref="LoweringContext.EnumPropertyNames"/>
    /// map means "no enum properties are known here" (a function body, a nested binder):
    /// a lookup miss, not a second lowering mode.
    /// </summary>
    private DomainTypeReference? EnumTargetOf(DomainExpression expr) {
        if (expr is not PropertyAccess property
            || _context.EnumPropertyNames is null
            || !_context.EnumPropertyNames.TryGetValue(property.Name, out var enumTypeName))
            return null;
        return new DomainTypeReference(enumTypeName);
    }

    /// <summary>
    /// True when a bare name must stay an instance read instead of becoming an enum
    /// member: it is a property the property-type resolver knows, a property in the
    /// enum map, or an action parameter (parameters win in
    /// <see cref="PropertyAccess"/>).
    /// </summary>
    private bool Shadows(string name) =>
        _actionParameterNames?.Contains(name) == true
        || _context.EnumPropertyNames?.ContainsKey(name) == true
        || _propertyTypeResolver?.Invoke(name) is not null;
}