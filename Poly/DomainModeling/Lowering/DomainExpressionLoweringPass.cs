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
public sealed class DomainExpressionLoweringPass {
    private readonly IReadOnlyDictionary<string, Node> _parameters;

    /// <param name="parameters">
    /// Optional map of parameter names to their Syntax AST nodes.
    /// When a ParameterAccess is encountered, its name is looked up here.
    /// If absent, a fresh Parameter node is created.
    /// </param>
    public DomainExpressionLoweringPass(IReadOnlyDictionary<string, Node>? parameters = null) {
        _parameters = parameters ?? new Dictionary<string, Node>();
    }

    /// <summary>
    /// Lowers <paramref name="expression"/> to a Syntax AST <see cref="Node"/>,
    /// using <paramref name="subject"/> as the current-instance root for
    /// property and owned-navigation resolution.
    /// </summary>
    public Node Lower(DomainExpression expression, Node subject) {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(subject);
        return LowerCore(expression, subject);
    }

    private Node LowerCore(DomainExpression expr, Node currentSubject) {
        return expr switch {
            PropertyAccess p => new Member(currentSubject, p.Name),
            ParameterAccess p => _parameters.TryGetValue(p.Name, out var param) ? param : new Parameter(p.Name),
            Literal l => new Constant(l.Value),

            OwnedAccess oa => LowerCore(oa.Inner, new Member(currentSubject, oa.OwnedName)),
            RelationshipNavigation rn => LowerCore(rn.TargetProperty, new Member(currentSubject, rn.RelationshipName)),

            Exists e => new NotEqual(LowerCore(e.Target, currentSubject), new Constant(null)),
            NotExists ne => new Equal(LowerCore(ne.Target, currentSubject), new Constant(null)),

            Add a => new SN.Add(LowerCore(a.Left, currentSubject), LowerCore(a.Right, currentSubject)),
            Subtract s => new SN.Subtract(LowerCore(s.Left, currentSubject), LowerCore(s.Right, currentSubject)),
            Multiply m => new SN.Multiply(LowerCore(m.Left, currentSubject), LowerCore(m.Right, currentSubject)),
            Divide d => new SN.Divide(LowerCore(d.Left, currentSubject), LowerCore(d.Right, currentSubject)),

            And a => new SN.And(LowerCore(a.Left, currentSubject), LowerCore(a.Right, currentSubject)),
            Or o => new SN.Or(LowerCore(o.Left, currentSubject), LowerCore(o.Right, currentSubject)),
            Not n => new SN.Not(LowerCore(n.Operand, currentSubject)),

            Comparison c => c.Kind switch {
                ComparisonKind.Equal => new Equal(LowerCore(c.Left, currentSubject), LowerCore(c.Right, currentSubject)),
                ComparisonKind.NotEqual => new NotEqual(LowerCore(c.Left, currentSubject), LowerCore(c.Right, currentSubject)),
                ComparisonKind.LessThan => new LessThan(LowerCore(c.Left, currentSubject), LowerCore(c.Right, currentSubject)),
                ComparisonKind.LessThanOrEqual => new LessThanOrEqual(LowerCore(c.Left, currentSubject), LowerCore(c.Right, currentSubject)),
                ComparisonKind.GreaterThan => new GreaterThan(LowerCore(c.Left, currentSubject), LowerCore(c.Right, currentSubject)),
                ComparisonKind.GreaterThanOrEqual => new GreaterThanOrEqual(LowerCore(c.Left, currentSubject), LowerCore(c.Right, currentSubject)),
                _ => throw new NotSupportedException($"Comparison kind '{c.Kind}' is not supported."),
            },

            DateOperation d => d.Kind switch {
                DateOperationKind.AddDays => new Invoke(
                    new Member(LowerCore(d.Date, currentSubject), "AddDays"),
                    LowerCore(d.Offset, currentSubject)),
                DateOperationKind.AddMonths => new Invoke(
                    new Member(LowerCore(d.Date, currentSubject), "AddMonths"),
                    LowerCore(d.Offset, currentSubject)),
                DateOperationKind.DiffDays => new Invoke(
                    new Member(LowerCore(d.Date, currentSubject), "Subtract"),
                    LowerCore(d.Offset, currentSubject)),
                _ => throw new NotSupportedException($"DateOperation kind '{d.Kind}' is not supported."),
            },

            _ => throw new NotSupportedException(
                $"DomainExpression node type '{expr.GetType().Name}' is not supported.")
        };
    }
}