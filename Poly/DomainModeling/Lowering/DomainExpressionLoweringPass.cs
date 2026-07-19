using SN = Poly.Syntax.Nodes;

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
    private Node _currentSubject = null!;

    /// <param name="parameters">
    /// Optional map of parameter names to their Syntax AST nodes.
    /// When a ParameterAccess is encountered, its name is looked up here.
    /// If absent, a fresh Parameter node is created.
    /// </param>
    public DomainExpressionLoweringPass(IReadOnlyDictionary<string, Node>? parameters = null) {
        _parameters = parameters ?? new Dictionary<string, Node>();
    }

    /// <summary>
    /// Creates a pass using context from a <see cref="LoweringContext"/>.
    /// </summary>
    public DomainExpressionLoweringPass(LoweringContext context)
        : this(context.Parameters) { }

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

    protected override Node PropertyAccess(Poly.DomainModeling.PropertyAccess p)
        => new Member(_currentSubject, p.Name);

    protected override Node ParameterAccess(Poly.DomainModeling.ParameterAccess p)
        => _parameters.TryGetValue(p.Name, out var param) ? param : new Parameter(p.Name);

    protected override Node Literal(Poly.DomainModeling.Literal l)
        => new Constant(l.Value);

    protected override Node OwnedAccess(Poly.DomainModeling.OwnedAccess oa)
        => Route(oa.Inner, new Member(_currentSubject, oa.OwnedName));

    protected override Node RelationshipNavigation(Poly.DomainModeling.RelationshipNavigation rn)
        => Route(rn.TargetProperty, new Member(_currentSubject, rn.RelationshipName));

    // --- Recurse into a new subject — helper to avoid confusion with Route(expr) ---
    private Node Route(DomainExpression expr, Node subject) {
        var saved = _currentSubject;
        _currentSubject = subject;
        try { return Route(expr); }
        finally { _currentSubject = saved; }
    }

    protected override Node Exists(Poly.DomainModeling.Exists e)
        => new NotEqual(Lower(e.Target, _currentSubject), new Constant(null));

    protected override Node NotExists(Poly.DomainModeling.NotExists ne)
        => new Equal(Lower(ne.Target, _currentSubject), new Constant(null));

    protected override Node Add(Poly.DomainModeling.Add a)
        => new SN.Add(Lower(a.Left, _currentSubject), Lower(a.Right, _currentSubject));

    protected override Node Subtract(Poly.DomainModeling.Subtract s)
        => new SN.Subtract(Lower(s.Left, _currentSubject), Lower(s.Right, _currentSubject));

    protected override Node Multiply(Poly.DomainModeling.Multiply m)
        => new SN.Multiply(Lower(m.Left, _currentSubject), Lower(m.Right, _currentSubject));

    protected override Node Divide(Poly.DomainModeling.Divide d)
        => new SN.Divide(Lower(d.Left, _currentSubject), Lower(d.Right, _currentSubject));

    protected override Node And(Poly.DomainModeling.And a)
        => new SN.And(Lower(a.Left, _currentSubject), Lower(a.Right, _currentSubject));

    protected override Node Or(Poly.DomainModeling.Or o)
        => new SN.Or(Lower(o.Left, _currentSubject), Lower(o.Right, _currentSubject));

    protected override Node Not(Poly.DomainModeling.Not n)
        => new SN.Not(Lower(n.Operand, _currentSubject));

    protected override Node Comparison(Poly.DomainModeling.Comparison c)
        => c.Kind switch {
            ComparisonKind.Equal => new Equal(Lower(c.Left, _currentSubject), Lower(c.Right, _currentSubject)),
            ComparisonKind.NotEqual => new NotEqual(Lower(c.Left, _currentSubject), Lower(c.Right, _currentSubject)),
            ComparisonKind.LessThan => new LessThan(Lower(c.Left, _currentSubject), Lower(c.Right, _currentSubject)),
            ComparisonKind.LessThanOrEqual => new LessThanOrEqual(Lower(c.Left, _currentSubject), Lower(c.Right, _currentSubject)),
            ComparisonKind.GreaterThan => new GreaterThan(Lower(c.Left, _currentSubject), Lower(c.Right, _currentSubject)),
            ComparisonKind.GreaterThanOrEqual => new GreaterThanOrEqual(Lower(c.Left, _currentSubject), Lower(c.Right, _currentSubject)),
            _ => throw new NotSupportedException($"Comparison kind '{c.Kind}' is not supported."),
        };

    protected override Node DateOperation(Poly.DomainModeling.DateOperation d)
        => d.Kind switch {
            DateOperationKind.AddDays => new Invoke(
                new Member(Lower(d.Date, _currentSubject), "AddDays"),
                Lower(d.Offset, _currentSubject)),
            DateOperationKind.AddMonths => new Invoke(
                new Member(Lower(d.Date, _currentSubject), "AddMonths"),
                Lower(d.Offset, _currentSubject)),
            DateOperationKind.DiffDays => new Invoke(
                new Member(Lower(d.Date, _currentSubject), "Subtract"),
                Lower(d.Offset, _currentSubject)),
            _ => throw new NotSupportedException($"DateOperation kind '{d.Kind}' is not supported."),
        };

    // Q3′ quantifiers — authoring-only for now (need store-aware evaluation).
    protected override Node AnyExpr(Poly.DomainModeling.AnyExpr a) => throw Q3NotSupported("any", a.RelationshipName);
    protected override Node AllExpr(Poly.DomainModeling.AllExpr a) => throw Q3NotSupported("all", a.RelationshipName);
    protected override Node NoneExpr(Poly.DomainModeling.NoneExpr n) => throw Q3NotSupported("none", n.RelationshipName);
    protected override Node CountExpr(Poly.DomainModeling.CountExpr c) => throw Q3NotSupported("count", c.RelationshipName);

    private static Exception Q3NotSupported(string quantifier, string relName) =>
        new NotSupportedException(
            $"Q3′ quantifier '{quantifier} {relName} …' requires store-aware evaluation " +
            "which is not yet implemented on the VM compilation path.");
}