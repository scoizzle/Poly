using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Dispatch;
using Poly.DomainModeling.Meaning;
using Poly.DomainModeling.Ontology;

using Add = Poly.DomainModeling.Ontology.Add;
using And = Poly.DomainModeling.Ontology.And;
using Divide = Poly.DomainModeling.Ontology.Divide;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using Not = Poly.DomainModeling.Ontology.Not;
using Or = Poly.DomainModeling.Ontology.Or;
using SN = Poly.Ast.Nodes;
using Subtract = Poly.DomainModeling.Ontology.Subtract;

namespace Poly.DomainModeling.Lowering;

/// <summary>
/// Lowers a DomainExpression tree into the shared Syntax AST
/// (<see cref="Syntax.Nodes"/>), making it compilable through
/// the existing LinqExpressionGenerator and CSharpGenerator.
/// </summary>
/// <remarks>
/// Domain-specific nodes (OwnedAccess, RelationshipNavigation)
/// are structurally unfolded into nested Member chains.
/// Existence queries (Exists/NotExists) become null comparisons.
/// Arithmetic, boolean, and comparison nodes map 1:1 to their Syntax AST
/// counterparts.
/// </remarks>
public sealed class DomainExpressionLoweringPass : DomainExpressionDispatch<Node> {
    private readonly IReadOnlyDictionary<string, Node> _parameters;
    private readonly HashSet<string>? _actionParameterNames;
    private readonly IReadOnlyDictionary<string, string>? _enumPropertyNames;
    private readonly Func<string, string>? _navigationNameResolver;
    private readonly Func<string, bool>? _isCollectionNavigation;
    private readonly Func<string, string?>? _propertyTypeResolver;
    private readonly Domain? _domain;
    private readonly ExpressionMeaning _meaning;
    private readonly ExpressionFormRegistry? _forms;
    private Node _currentSubject = null!;

    /// <param name="parameters">
    /// Optional map of parameter names to their Syntax AST nodes.
    /// When a ParameterAccess is encountered, its name is looked up here.
    /// If absent, a fresh Parameter node is created.
    /// </param>
    public DomainExpressionLoweringPass(IReadOnlyDictionary<string, Node>? parameters = null)
        : this(new LoweringContext(new Parameter("entity"), parameters)) { }

    /// <summary>
    /// Creates a pass using context from a <see cref="LoweringContext"/>.
    /// The instance root is <see cref="LoweringContext.Subject"/> (module bodies
    /// pass <see cref="ThisReference"/>). Names in
    /// <see cref="LoweringContext.ActionParameterNames"/> render as bare parameters
    /// instead of <c>this.name</c>.
    /// <see cref="LoweringContext.NavigationNameResolver"/> maps DSL relationship
    /// names to generated member names (pascal-cased navs).
    /// </summary>
    public DomainExpressionLoweringPass(LoweringContext context) {
        _parameters = context.Parameters ?? new Dictionary<string, Node>();
        _actionParameterNames = context.ActionParameterNames;
        _enumPropertyNames = context.EnumPropertyNames;
        _navigationNameResolver = context.NavigationNameResolver;
        _isCollectionNavigation = context.IsCollectionNavigation;
        _propertyTypeResolver = context.PropertyTypeResolver;
        _domain = context.Domain;
        _meaning = context.Meaning ?? ExpressionMeaning.Empty;
        _forms = context.Forms;
    }

    private ExpressionMeaning EffectiveMeaning {
        get {
            if (_meaning.Lowering.Handlers.Count > 0)
                return _meaning;
            return _domain is not null
                ? RuntimeAnalysisCache.MeaningFor(_domain)
                : _meaning;
        }
    }

    private ExpressionFormRegistry EffectiveForms =>
        _forms ?? (_domain is not null
            ? RuntimeAnalysisCache.FormsFor(_domain)
            : new ExpressionFormRegistry());

    /// <summary>
    /// Lowers <paramref name="expression"/> to a Syntax AST <see cref="Node"/>,
    /// using <paramref name="subject"/> as the current-instance root for
    /// property and owned-navigation resolution.
    /// </summary>
    public Node Lower(DomainExpression expression, Node subject) {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(subject);
        _currentSubject = subject;
        return Route(expression);
    }

    protected override Node Default() => throw new NotSupportedException(
        $"DomainExpression node type is not supported");

    protected override Node PropertyAccess(PropertyAccess p) {
        // Session ident folds (clocks) and Guid through LowerDefaultExpression.
        // Bare fragments without those tables stay Member so analysis can fail
        // closed on unknown property.
        var runtime = EffectLoweringPass.LowerDefaultExpression(
            p, typeHint: null, EffectiveMeaning, EffectiveForms);
        if (runtime is not null) return runtime;

        if (_actionParameterNames?.Contains(p.Name) == true)
            return new Parameter(p.Name);
        return new Member(_currentSubject, ResolveName(p.Name));
    }

    /// <summary>Applies the navigation name resolver (DSL nav → generated member name).</summary>
    private string ResolveName(string name) => _navigationNameResolver?.Invoke(name) ?? name;

    protected override Node ParameterAccess(ParameterAccess p)
        => _parameters.TryGetValue(p.Name, out var param) ? param : new Parameter(p.Name);

    protected override Node Literal(Literal l)
        => new Constant(l.Value);

    protected override Node OwnedAccess(OwnedAccess oa)
        => Route(oa.Inner, new Member(_currentSubject, ResolveName(oa.OwnedName)));

    protected override Node RelationshipNavigation(RelationshipNavigation rn) {
        // Peer binder / other parameter-backed path-prefix roots: subject is the
        // parameter node, not Member(this, name). Nested path-prefix under that
        // root is unsupported (analysis rejects; fail loud here for defense).
        if (_parameters.TryGetValue(rn.RelationshipName, out var parameterSubject)) {
            if (ContainsRelationshipNavigation(rn.TargetProperty)) {
                throw new InvalidOperationException(
                    $"Nested path-prefix under binder '{rn.RelationshipName}' is not supported. " +
                    "Use a single peer property (e.g. 'order Code'), not nested navigation.");
            }
            return Route(rn.TargetProperty, parameterSubject);
        }

        // Every hop in a path-prefix is a relationship navigation.
        // Module shape: NullForgiving for CS8602; require gates own
        // DomainResult.Failure ("requires a linked") in BuildActionBodyWithGuards
        // before the policy bool is called. Simulate binds This → entity and
        // rewrites NullForgiving hops so an unlinked to-one is false, not throw.
        // Collection hops cannot be a singular path-prefix — fail closed (use any/all).
        if (IsCollectionNav(rn.RelationshipName)) {
            throw new InvalidOperationException(
                $"Path-prefix on relationship '{rn.RelationshipName}' requires exactly one linked target. " +
                "Use any/all quantifiers for collections.");
        }
        var relMember = new Member(_currentSubject, ResolveNavName(rn.RelationshipName));
        return Route(rn.TargetProperty, new NullForgiving(relMember));
    }

    /// <summary>Pascal-cases a relationship hop name the resolver did not map
    /// (nested navs on target entities); uses the resolver's mapping when present.</summary>
    private string ResolveNavName(string name) {
        var resolved = _navigationNameResolver?.Invoke(name) ?? name;
        return resolved == name ? DomainToCSharpExporter.ToPascalCase(name) : resolved;
    }

    private static bool ContainsRelationshipNavigation(DomainExpression expr) =>
        expr is RelationshipNavigation
        || expr.Children.OfType<DomainExpression>().Any(ContainsRelationshipNavigation);

    // --- Recurse into a new subject — helper to avoid confusion with Route(expr) ---
    private Node Route(DomainExpression expr, Node subject) {
        var saved = _currentSubject;
        _currentSubject = subject;
        try { return Route(expr); }
        finally { _currentSubject = saved; }
    }

    protected override Node Exists(Exists e) {
        // Collection (`many`) relationship: ctor-initialized lists are never null;
        // store-link presence is a non-empty check. Simulate binds the same Count
        // member through the dictionary nav.
        if (e.Target is PropertyAccess col && IsCollectionNav(col.Name)) {
            return new NotEqual(
                new Member(Lower(e.Target, _currentSubject), "Count"),
                new Constant(0));
        }
        return new NotEqual(Lower(e.Target, _currentSubject), new Constant(null));
    }

    protected override Node NotExists(NotExists ne) {
        if (ne.Target is PropertyAccess col && IsCollectionNav(col.Name)) {
            return new Equal(
                new Member(Lower(ne.Target, _currentSubject), "Count"),
                new Constant(0));
        }
        return new Equal(Lower(ne.Target, _currentSubject), new Constant(null));
    }

    private bool IsCollectionNav(string name) =>
        _isCollectionNavigation?.Invoke(name) == true;

    protected override Node Add(Add a) {
        if (EffectiveMeaning.Lowering.TryLower(a, e => Lower(e, _currentSubject), _propertyTypeResolver, out var node))
            return node;
        return new SN.Add(Lower(a.Left, _currentSubject), Lower(a.Right, _currentSubject));
    }

    protected override Node Subtract(Subtract s) {
        if (EffectiveMeaning.Lowering.TryLower(s, e => Lower(e, _currentSubject), _propertyTypeResolver, out var node))
            return node;
        return new SN.Subtract(Lower(s.Left, _currentSubject), Lower(s.Right, _currentSubject));
    }

    protected override Node Multiply(Multiply m)
        => new SN.Multiply(Lower(m.Left, _currentSubject), Lower(m.Right, _currentSubject));

    protected override Node Divide(Divide d)
        => new SN.Divide(Lower(d.Left, _currentSubject), Lower(d.Right, _currentSubject));

    protected override Node And(And a)
        => new SN.And(Lower(a.Left, _currentSubject), Lower(a.Right, _currentSubject));

    protected override Node Or(Or o)
        => new SN.Or(Lower(o.Left, _currentSubject), Lower(o.Right, _currentSubject));

    protected override Node Not(Not n)
        => new SN.Not(Lower(n.Operand, _currentSubject));

    protected override Node Comparison(Comparison c) {
        var loweredLeft = Lower(c.Left, _currentSubject);
        var loweredRight = Lower(c.Right, _currentSubject);

        // For enum-typed properties, replace string literal with qualified member
        // access: Status == "Active" becomes Status == PatronStatus.Active
        if (_enumPropertyNames is { Count: > 0 }) {
            var fixedLeft = FixEnumLiteral(c.Left, c.Right, loweredRight);
            var fixedRight = FixEnumLiteral(c.Right, c.Left, loweredLeft);
            if (fixedLeft is not null) loweredLeft = fixedLeft;
            if (fixedRight is not null) loweredRight = fixedRight;
        }

        // Simplify boolean comparisons: boolProp == true  → boolProp
        //                            boolProp == false → !boolProp
        if (c.Kind == ComparisonKind.Equal
            && loweredRight is Constant { Value: bool b }) {
            return b ? loweredLeft : new SN.Not(loweredLeft);
        }
        if (c.Kind == ComparisonKind.NotEqual
            && loweredRight is Constant { Value: bool b2 }) {
            return b2 ? new SN.Not(loweredLeft) : loweredLeft;
        }

        return c.Kind switch {
            ComparisonKind.Equal => new Equal(loweredLeft, loweredRight),
            ComparisonKind.NotEqual => new NotEqual(loweredLeft, loweredRight),
            ComparisonKind.LessThan => new LessThan(loweredLeft, loweredRight),
            ComparisonKind.LessThanOrEqual => new LessThanOrEqual(loweredLeft, loweredRight),
            ComparisonKind.GreaterThan => new GreaterThan(loweredLeft, loweredRight),
            ComparisonKind.GreaterThanOrEqual => new GreaterThanOrEqual(loweredLeft, loweredRight),
            _ => throw new NotSupportedException($"Comparison kind '{c.Kind}' is not supported."),
        };
    }

    /// <summary>
    /// If <paramref name="valueNode"/> is a string literal and <paramref name="otherSide"/>
    /// is a property access for an enum-typed property, returns a Syntax node that
    /// references the enum member qualified by the type name (e.g. <c>PatronStatus.Active</c>).
    /// Returns null if no substitution is needed.
    /// </summary>
    private Node? FixEnumLiteral(DomainExpression valueExpr, DomainExpression otherSide, Node otherSideNode) {
        if (valueExpr is Literal { Value: string strVal }
            && !string.IsNullOrEmpty(strVal)
            && strVal is not "true" and not "false" and not "null"
            && !char.IsDigit(strVal[0])
            && otherSide is PropertyAccess prop
            && _enumPropertyNames!.TryGetValue(prop.Name, out var enumTypeName)) {
            return new Member(new NamedTypeReference(enumTypeName), strVal);
        }
        return null;
    }

    // Filtered any/all/none pack the DomainExpression predicate as a Store job
    // argument (AnyRelated/…). Bare `count Rel` is the collection Count member.
    // Print cannot emit the packed DomainExpression; the exporter substitutes a
    // throw stub on policy methods. Unifying those as tree nodes is separate work.
    protected override Node AnyExpr(AnyExpr a) =>
        StoreQuantifier("AnyRelated", a.RelationshipName, a.Body);

    protected override Node AllExpr(AllExpr a) =>
        StoreQuantifier("AllRelated", a.RelationshipName, a.Body);

    protected override Node NoneExpr(NoneExpr n) =>
        StoreQuantifier("NoneRelated", n.RelationshipName, n.Body);

    protected override Node CountExpr(CountExpr c) {
        if (c.Body is null) {
            return new Member(
                new Member(_currentSubject, ResolveNavName(c.RelationshipName)),
                "Count");
        }
        return StoreQuantifier("CountRelated", c.RelationshipName, c.Body);
    }

    private Node StoreQuantifier(string job, string relationshipName, DomainExpression? body) =>
        new Invoke(
            new Member(_currentSubject, job),
            new Constant(relationshipName),
            new Constant(body));

    protected override Node Library(DomainExpression expr) {
        if (EffectiveMeaning.Lowering.TryLower(expr, Route, _propertyTypeResolver, out var node))
            return node;
        throw new NotSupportedException(
            $"DomainExpression node type '{expr.GetType().Name}' is not supported");
    }
}