using Poly.DomainModeling.Ontology;

using SN = Poly.Ast.Nodes;

namespace Poly.DomainModeling.Lowering;

// Presence (#936): a read through an unlinked optional to-one hop yields ABSENT, the same value an
// omitted optional property has. Every position that consumes a domain value asks this pass how to
// tell "the read has a value" from "the read is absent", so one lowered answer serves the simulator
// and the printed module.
public sealed partial class DomainExpressionLoweringPass {
    /// <summary>
    /// What a value read needs before it can be evaluated on the current host. <see cref="Guard"/> is a
    /// predicate that is true exactly when the read has a value, or null when the read is always present
    /// (a literal, a required read, a condition that already encodes its own absence).
    /// <see cref="ThroughHop"/> is true when the guard protects a to-one dereference, which is the case
    /// where a consumer must not evaluate the value before testing the guard.
    /// </summary>
    internal readonly record struct ValuePresence(Node? Guard, bool ThroughHop) {
        /// <summary>Always present: nothing to test and nothing to dereference.</summary>
        public static readonly ValuePresence Present = new(null, false);
    }

    /// <summary>
    /// The hop a navigation to a condition reads through: set while that condition lowers, so each read
    /// on its subject is absent when the hop is unlinked (the condition's own comparison then answers
    /// <c>!=</c> true, an ordering false, and <c>is null</c> true, instead of every operator being false).
    /// </summary>
    private Node? _subjectPresence;

    /// <summary>
    /// The guard for <paramref name="expr"/> read on <paramref name="subject"/>. A to-one hop contributes
    /// <c>hop != null</c> per hop, the optional leaf contributes <c>leaf != null</c>, and arithmetic
    /// contributes its operands' guards, because arithmetic over an absent operand is absent.
    /// </summary>
    internal ValuePresence PresenceOf(DomainExpression expr, Node subject) =>
        PresenceIn(expr, subject, _subjectPresence, _context.SourceEntityName);

    private ValuePresence PresenceIn(
        DomainExpression expr, Node subject, Node? pending, string? entityName) => expr switch {
            PropertyAccess property => PropertyPresence(property, subject, pending, entityName),
            RelationshipNavigation navigation => NavigationPresence(navigation, subject, pending, entityName),
            Ontology.Add or Ontology.Subtract or Ontology.Multiply or Ontology.Divide =>
                OperandPresence(expr, subject, pending, entityName),
            _ => ValuePresence.Present,
        };

    private ValuePresence PropertyPresence(
        PropertyAccess property, Node subject, Node? pending, string? entityName) {
        // An action parameter or a binder names a value the host already holds, not a property read.
        if (_parameters.ContainsKey(property.Name) || _actionParameterNames?.Contains(property.Name) == true)
            return ValuePresence.Present;
        var own = IsOptionalMember(entityName, property.Name)
            ? new NotEqual(new Member(subject, property.Name), new Constant(null!))
            : null;
        return Combine(pending, own);
    }

    private ValuePresence NavigationPresence(
        RelationshipNavigation navigation, Node subject, Node? pending, string? entityName) {
        // A binder root is the parameter, not a dereference; a collection has no singular value; and a
        // navigation to a condition already answers false when its hop is unlinked.
        if (_parameters.ContainsKey(navigation.RelationshipName)
            || IsCollectionNav(navigation.RelationshipName)
            || IsPathPrefixPredicate(navigation.TargetProperty))
            return ValuePresence.Present;

        var guards = new List<Node>();
        var throughHop = false;
        var current = subject;
        var hop = (DomainExpression)navigation;
        while (hop is RelationshipNavigation next) {
            if (_parameters.TryGetValue(next.RelationshipName, out var binder)) {
                current = binder;
                entityName = null;
            }
            else {
                var member = new Member(current, ResolveNavName(next.RelationshipName));
                guards.Add(new NotEqual(member, new Constant(null!)));
                throughHop = true;
                current = member;
                entityName = EntityOf(entityName)?.Navigations
                        .FirstOrDefault(n => string.Equals(n.Name, next.RelationshipName, StringComparison.Ordinal))?
                        .Target.TypeName
                    ?? ResolveRelationshipTarget(next.RelationshipName);
            }
            hop = next.TargetProperty;
        }

        // The value is read on the last hop: an optional leaf adds its own check, and arithmetic or a
        // nested navigation inside the jump (`advisor mentor Age * 2`) adds the guards it needs there.
        var leaf = hop is PropertyAccess property
            ? PropertyPresence(property, current, null, entityName)
            : PresenceIn(hop, current, null, entityName);
        if (leaf.Guard is not null)
            guards.Add(leaf.Guard);
        throughHop |= leaf.ThroughHop;

        return Combine(pending, guards.Count == 0 ? null : Conjoin(guards), throughHop);
    }

    private ValuePresence OperandPresence(
        DomainExpression expr, Node subject, Node? pending, string? entityName) {
        // The subject's own hop guard is added once, not once per operand.
        var presences = expr.Children.OfType<DomainExpression>()
            .Select(operand => PresenceIn(operand, subject, null, entityName))
            .ToList();
        var guards = presences.Select(p => p.Guard).OfType<Node>().ToList();
        return presences.Any(p => p.ThroughHop)
            ? new ValuePresence(
                Combine(pending, guards.Count == 0 ? null : Conjoin(guards), true).Guard, true)
            : Combine(pending, guards.Count == 0 ? null : Conjoin(guards));
    }

    /// <summary>
    /// The guard for a read on a subject that is itself reached through a hop (a navigation to a condition
    /// or a nested jump), combined with the read's own absence.
    /// </summary>
    private static ValuePresence Combine(Node? pending, Node? own) => (pending, own) switch {
        (null, null) => ValuePresence.Present,
        (not null, null) => new ValuePresence(pending, true),
        (null, not null) => new ValuePresence(own, false),
        _ => new ValuePresence(And(pending!, own!), true),
    };

    private static ValuePresence Combine(Node? pending, Node? own, bool throughHop) {
        var combined = Combine(pending, own);
        return combined.Guard is null ? combined : combined with { ThroughHop = combined.ThroughHop || throughHop };
    }

    /// <summary>
    /// ANDs the guards, flattening nested conjunctions and dropping a guard an operand already added
    /// (a hop reached twice is guarded once).
    /// </summary>
    private static Node Conjoin(IReadOnlyList<Node> guards) {
        var flat = new List<Node>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(Node guard) {
            if (guard is SN.And and) {
                Add(and.LeftHandValue);
                Add(and.RightHandValue);
            }
            else if (seen.Add(guard.ToString())) {
                flat.Add(guard);
            }
        }
        foreach (var guard in guards)
            Add(guard);
        return flat.Aggregate((left, right) => new SN.And(left, right));
    }

    /// <summary><c>a &amp;&amp; b</c>, folded when either side is a literal.</summary>
    private static Node And(Node left, Node right) => left switch {
        Constant { Value: true } => right,
        Constant { Value: false } => left,
        _ => right switch {
            Constant { Value: true } => left,
            Constant { Value: false } => right,
            _ => new SN.And(left, right),
        },
    };

    /// <summary><c>a || b</c>, folded when either side is a literal.</summary>
    private static Node Or(Node left, Node right) => left switch {
        Constant { Value: true } => left,
        Constant { Value: false } => right,
        _ => right switch {
            Constant { Value: true } => right,
            Constant { Value: false } => left,
            _ => new SN.Or(left, right),
        },
    };

    /// <summary><c>!a</c>, folded when the operand is a literal.</summary>
    private static Node Not(Node operand) => operand switch {
        Constant { Value: true } => new Constant(false),
        Constant { Value: false } => new Constant(true),
        _ => new SN.Not(operand),
    };

    /// <summary><c>a != b</c> between guards, folded when either side is a literal.</summary>
    private static Node Unequal(Node left, Node right) => left switch {
        Constant { Value: true } => Not(right),
        Constant { Value: false } => right,
        _ => right switch {
            Constant { Value: true } => Not(left),
            Constant { Value: false } => left,
            _ => new NotEqual(left, right),
        },
    };

    private static bool IsNullLiteral(DomainExpression expr) => expr is Literal { Value: null };

    /// <summary>
    /// A comparison whose operands may be absent (#936), with the answer the guide's optional-value rule
    /// gives: <c>==</c> is true only when both sides are absent, <c>!=</c> is true when exactly one is, an
    /// ordering is false. <c>is null</c> and <c>is not null</c> read the same absence, so a value that is
    /// absent is null.
    /// </summary>
    private static Node AbsentAware(Comparison comparison, Node? leftGuard, Node? rightGuard, Node compared) {
        if (IsNullLiteral(comparison.Right) && leftGuard is not null)
            return comparison.Kind == ComparisonKind.NotEqual
                ? And(leftGuard, compared)
                : Or(Not(leftGuard), compared);
        if (IsNullLiteral(comparison.Left) && rightGuard is not null)
            return comparison.Kind == ComparisonKind.NotEqual
                ? And(rightGuard, compared)
                : Or(Not(rightGuard), compared);

        var left = leftGuard ?? new Constant(true);
        var right = rightGuard ?? new Constant(true);
        return comparison.Kind switch {
            ComparisonKind.Equal => Or(And(And(left, right), compared), And(Not(left), Not(right))),
            ComparisonKind.NotEqual => Or(And(And(left, right), compared), Unequal(left, right)),
            _ => And(And(left, right), compared),
        };
    }
}