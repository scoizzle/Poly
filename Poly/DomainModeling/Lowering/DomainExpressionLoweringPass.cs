using Poly.Ast.Nodes;
using Poly.DomainModeling.Dispatch;
using Poly.DomainModeling.Ontology;

using Add = Poly.DomainModeling.Ontology.Add;
using And = Poly.DomainModeling.Ontology.And;
using Divide = Poly.DomainModeling.Ontology.Divide;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using Not = Poly.DomainModeling.Ontology.Not;
using Or = Poly.DomainModeling.Ontology.Or;
using Prim = Poly.Introspection.PrimitiveType;
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
    private readonly bool _useThisReference;
    private readonly IReadOnlyDictionary<string, string>? _enumPropertyNames;
    private readonly Func<string, string>? _navigationNameResolver;
    private readonly Func<string, bool>? _isCollectionNavigation;
    private readonly Func<string, bool>? _isRelationshipNavigation;
    private readonly Func<string, string?>? _propertyTypeResolver;
    private readonly Domain? _domain;
    private string? _sourceEntityName;
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
    /// When <see cref="LoweringContext.UseThisReference"/> is true, the lowered
    /// tree uses <see cref="ThisReference"/> instead of <see cref="Parameter"/>
    /// for the instance root, and names in <see cref="LoweringContext.ActionParameterNames"/>
    /// render as bare parameters instead of <c>this.name</c>.
    /// <see cref="LoweringContext.NavigationNameResolver"/> maps DSL relationship
    /// names to generated member names (pascal-cased navs).
    /// </summary>
    public DomainExpressionLoweringPass(LoweringContext context) {
        _parameters = context.Parameters ?? new Dictionary<string, Node>();
        _actionParameterNames = context.ActionParameterNames;
        _useThisReference = context.UseThisReference;
        _enumPropertyNames = context.EnumPropertyNames;
        _navigationNameResolver = context.NavigationNameResolver;
        _isCollectionNavigation = context.IsCollectionNavigation;
        _isRelationshipNavigation = context.IsRelationshipNavigation;
        _propertyTypeResolver = context.PropertyTypeResolver;
        _domain = context.Domain;
        _sourceEntityName = context.SourceEntityName;
    }

    /// <summary>
    /// Lowers <paramref name="expression"/> to a Syntax AST <see cref="Node"/>,
    /// using <paramref name="subject"/> as the current-instance root for
    /// property and owned-navigation resolution.
    /// </summary>
    public Node Lower(DomainExpression expression, Node subject) {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(subject);
        _currentSubject = _useThisReference && subject is Parameter { Name: "entity" }
            ? new ThisReference()
            : subject;
        return Route(expression);
    }

    protected override Node Default() => throw new NotSupportedException(
        $"DomainExpression node type is not supported");

    protected override Node PropertyAccess(PropertyAccess p) {
        // Runtime keywords: now/today/guid lower to BCL members, not entity
        // property access. Type adaptation for a known target happens in
        // EffectLoweringPass (assign / create initializers).
        var runtime = EffectLoweringPass.LowerDefaultExpression(p);
        if (runtime is not null) return runtime;

        // When UseThisReference is set, action parameters render as bare names
        if (_useThisReference && _actionParameterNames?.Contains(p.Name) == true)
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

        // Every hop in a path-prefix is a relationship navigation. C# export
        // uses coalesce-throw on the pascal nav. Runtime binds Store via
        // GetRelatedOne so unlinked / multi-link fail with the path-prefix
        // contract, then TypeCast to the target entity so leaf members resolve.
        if (!_useThisReference) {
            var related = new Invoke(
                new Member(_currentSubject, "GetRelatedOne"),
                new Constant(rn.RelationshipName));
            var targetName = ResolveRelationshipTarget(rn.RelationshipName);
            Node typedHop = targetName is not null
                ? new TypeCast(related, new TypeReference(targetName))
                : related;
            return Route(rn.TargetProperty, typedHop, targetName);
        }

        var relMember = new Member(_currentSubject, ResolveNavName(rn.RelationshipName));
        Node hop = new Coalesce(relMember,
            new ThrowExpression(new New(
                new NamedTypeReference("InvalidOperationException"),
                new Constant($"No linked instances found for relationship '{rn.RelationshipName}'."))));
        return Route(rn.TargetProperty, hop);
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
    private Node Route(DomainExpression expr, Node subject, string? sourceEntityName = null) {
        var saved = _currentSubject;
        var savedSource = _sourceEntityName;
        _currentSubject = subject;
        if (sourceEntityName is not null)
            _sourceEntityName = sourceEntityName;
        try { return Route(expr); }
        finally {
            _currentSubject = saved;
            _sourceEntityName = savedSource;
        }
    }

    protected override Node Exists(Exists e) {
        if (!_useThisReference && e.Target is PropertyAccess pa && IsRelationship(pa.Name)) {
            return new Invoke(
                new Member(_currentSubject, "ExistsRelated"),
                new Constant(pa.Name));
        }
        // Collection (`many`) relationship: the export's `collection != null` is
        // always true (ctor-initialized) while the runtime answers store-link
        // presence (false on empty) — lower to a real non-empty check instead.
        if (e.Target is PropertyAccess col && IsCollectionNav(col.Name)) {
            return new NotEqual(
                new Member(Lower(e.Target, _currentSubject), "Count"),
                new Constant(0));
        }
        return new NotEqual(Lower(e.Target, _currentSubject), new Constant(null));
    }

    protected override Node NotExists(NotExists ne) {
        if (!_useThisReference && ne.Target is PropertyAccess pa && IsRelationship(pa.Name)) {
            return new SN.Not(new Invoke(
                new Member(_currentSubject, "ExistsRelated"),
                new Constant(pa.Name)));
        }
        if (ne.Target is PropertyAccess col && IsCollectionNav(col.Name)) {
            return new Equal(
                new Member(Lower(ne.Target, _currentSubject), "Count"),
                new Constant(0));
        }
        return new Equal(Lower(ne.Target, _currentSubject), new Constant(null));
    }

    private bool IsCollectionNav(string name) =>
        _isCollectionNavigation?.Invoke(name) == true;

    private bool IsRelationship(string name) =>
        _isRelationshipNavigation?.Invoke(name) == true;

    private string? ResolveRelationshipTarget(string relationshipName) {
        if (_domain is null || _sourceEntityName is null)
            return null;
        var source = _domain.Types.OfType<Entity>().FirstOrDefault(e =>
            string.Equals(e.Name, _sourceEntityName, StringComparison.Ordinal));
        var rel = source?.Navigations.FirstOrDefault(n =>
            string.Equals(n.Name, relationshipName, StringComparison.Ordinal));
        return rel?.Target.TypeName;
    }

    protected override Node Add(Add a) {
        var left = Lower(a.Left, _currentSubject);
        var right = Lower(a.Right, _currentSubject);
        return LowerDateArithmetic(a.Left, left, right, isSubtract: false);
    }

    protected override Node Subtract(Subtract s) {
        var left = Lower(s.Left, _currentSubject);
        var right = Lower(s.Right, _currentSubject);
        return LowerDateArithmetic(s.Left, left, right, isSubtract: true);
    }

    /// <summary>
    /// Hoists the date-arithmetic rewrite (`DueDate + 14` → `DueDate.AddDays(14)`,
    /// `DueDate - 14` → `DueDate.AddDays(-14)`) into expression lowering so it applies
    /// everywhere a date-typed member appears in arithmetic — policies, if conditions,
    /// entry/exit, and create-in initializers — not just the assign path. The CLR types
    /// don't support `DateOnly + long`, so without this the generated C# fails CS0019
    /// (and the runtime evaluates garbage on heap-handle arithmetic).
    /// </summary>
    private Node LowerDateArithmetic(DomainExpression leftExpr, Node left, Node right, bool isSubtract) {
        if (leftExpr is PropertyAccess pa
            && _propertyTypeResolver?.Invoke(pa.Name) is { } typeName
            && typeName is "DateTime" or "Timestamp" or "Date" or "DateOnly") {
            // Subtract lowers to AddDays with a negated offset (DateOnly/DateTime have no
            // `- long` operator). Negate the RHS rather than emitting `0 - N` so the
            // DateOnly int-cast binds to the whole operand: (int)-14L, not (int)0L - 14L.
            var rawArg = isSubtract ? (Node)new SN.UnaryMinus(right) : right;
            // DateOnly.AddDays takes int; DateTime.AddDays takes double (long widens implicitly).
            var typedArg = typeName is "Date" or "DateOnly"
                ? Int32Offset(rawArg)
                : rawArg;
            return new Invoke(new Member(left, "AddDays"), [typedArg]);
        }
        return isSubtract ? new SN.Subtract(left, right) : new SN.Add(left, right);
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

    // Filtered any/all/none/count: runtime Store jobs (Notify-shaped). C# export
    // keeps in-memory collection Count for bare `count Rel`; filtered still throws.
    protected override Node AnyExpr(AnyExpr a) =>
        _useThisReference
            ? throw Q3NotSupported("any", a.RelationshipName)
            : StoreQuantifier("AnyRelated", a.RelationshipName, a.Body);

    protected override Node AllExpr(AllExpr a) =>
        _useThisReference
            ? throw Q3NotSupported("all", a.RelationshipName)
            : StoreQuantifier("AllRelated", a.RelationshipName, a.Body);

    protected override Node NoneExpr(NoneExpr n) =>
        _useThisReference
            ? throw Q3NotSupported("none", n.RelationshipName)
            : StoreQuantifier("NoneRelated", n.RelationshipName, n.Body);

    protected override Node CountExpr(CountExpr c) {
        if (_useThisReference) {
            if (c.Body is not null)
                throw Q3NotSupported("count", c.RelationshipName);
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

    private static Exception Q3NotSupported(string quantifier, string relName) =>
        new NotSupportedException(
            $"Collection quantifier '{quantifier} {relName} …' requires store-aware evaluation " +
            "which is not yet implemented on the VM compilation path.");

    protected override Node Now(Now _) =>
        new Member(new NamedTypeReference("DateTime"), "UtcNow");

    protected override Node Today(Today _) =>
        new Invoke(
            new Member(new NamedTypeReference("DateOnly"), "FromDateTime"),
            new Member(new NamedTypeReference("DateTime"), "UtcNow"));

    protected override Node Duration(Duration d) =>
        throw new NotSupportedException(
            $"Bare duration '{d.Amount} {d.Unit}' reached lowering without a temporal left operand " +
            "(e.g. 'Now - 12 days'). Resolve it into a DateOperation before lowering.");

    protected override Node DateOperation(DateOperation d) {
        var date = Route(d.Date);
        var offset = Route(d.Offset);
        var family = ResolveDateClrFamily(d.Date);

        if (family is DateClrFamily.TimeOnly
            && d.Kind is DateOperationKind.AddSeconds or DateOperationKind.AddMilliseconds) {
            var from = d.Kind is DateOperationKind.AddSeconds ? "FromSeconds" : "FromMilliseconds";
            return new Invoke(
                new Member(date, "Add"),
                new Invoke(new Member(new NamedTypeReference("TimeSpan"), from), offset));
        }

        var (method, arg) = d.Kind switch {
            DateOperationKind.AddMilliseconds => ("AddMilliseconds", offset),
            DateOperationKind.AddSeconds => ("AddSeconds", offset),
            DateOperationKind.AddMinutes => ("AddMinutes", offset),
            DateOperationKind.AddHours => ("AddHours", offset),
            DateOperationKind.AddDays => ("AddDays", offset),
            DateOperationKind.AddWeeks => ("AddDays", ScaleOffset(offset, 7)),
            DateOperationKind.AddMonths => ("AddMonths", offset),
            DateOperationKind.AddYears => ("AddYears", offset),
            DateOperationKind.DiffDays => ("Subtract", offset),
            _ => throw new NotSupportedException($"DateOperation kind '{d.Kind}' is not supported."),
        };

        if (NeedsIntOffset(family, d.Kind))
            arg = Int32Offset(arg);

        return new Invoke(new Member(date, method), arg);
    }

    private enum DateClrFamily { Unknown, DateOnly, DateTime, TimeOnly }

    private DateClrFamily ResolveDateClrFamily(DomainExpression dateExpr) {
        if (dateExpr is Now)
            return DateClrFamily.DateTime;
        if (dateExpr is Today)
            return DateClrFamily.DateOnly;
        if (DateOperandName(dateExpr) is { } name
            && _propertyTypeResolver?.Invoke(name) is { } typeName)
            return FamilyFromTypeName(typeName);
        if (dateExpr is DateOperation nested)
            return ResolveDateClrFamily(nested.Date);
        return DateClrFamily.Unknown;
    }

    private static string? DateOperandName(DomainExpression dateExpr) => dateExpr switch {
        PropertyAccess pa => pa.Name,
        ParameterAccess pa => pa.Name,
        _ => null,
    };

    private static DateClrFamily FamilyFromTypeName(string typeName) => typeName switch {
        "Date" or "DateOnly" => DateClrFamily.DateOnly,
        "DateTime" or "Timestamp" => DateClrFamily.DateTime,
        "Time" or "TimeOnly" => DateClrFamily.TimeOnly,
        _ => DateClrFamily.Unknown,
    };

    /// <summary>
    /// DateOnly.AddDays/AddMonths/AddYears take int; DateTime.AddMonths/AddYears take int.
    /// DateTime.AddDays (and clock Add*) take double — long widens. Cast wraps the scaled
    /// or negated offset so the printer emits <c>(int)-14L</c>, not <c>(int)0L - 14L</c>.
    /// </summary>
    private static bool NeedsIntOffset(DateClrFamily family, DateOperationKind kind) {
        if (kind is DateOperationKind.AddMonths or DateOperationKind.AddYears)
            return family is not DateClrFamily.TimeOnly;
        return family is DateClrFamily.DateOnly
            && kind is DateOperationKind.AddDays or DateOperationKind.AddWeeks;
    }

    private static Node Int32Offset(Node rawArg) =>
        new TypeCast(rawArg, new PrimitiveTypeReference(Prim.Int32));

    private static Node ScaleOffset(Node offset, int factor) => offset switch {
        Constant { Value: long n } => new Constant(n * factor),
        Constant { Value: int n } => new Constant(n * factor),
        _ => new SN.Multiply(offset, new Constant((long)factor)),
    };
}