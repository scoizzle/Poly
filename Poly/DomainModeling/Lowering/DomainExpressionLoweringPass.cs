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
    private readonly IReadOnlyDictionary<string, string>? _enumPropertyNames;
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
        _enumPropertyNames = context.EnumPropertyNames;
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

    /// <summary>Applies the navigation name resolver (DSL nav → generated member name).</summary>
    private string ResolveName(string name) => _navigationNameResolver?.Invoke(name) ?? name;

    /// <summary>Pascal-cases a relationship hop name the resolver did not map
    /// (nested navs on target entities); uses the resolver's mapping when present.</summary>
    private string ResolveNavName(string name) {
        var resolved = _navigationNameResolver?.Invoke(name) ?? name;
        return resolved == name ? DomainToCSharpExporter.ToPascalCase(name) : resolved;
    }

    private static bool ContainsRelationshipNavigation(DomainExpression expr) =>
        expr is RelationshipNavigation
        || expr.Children.OfType<DomainExpression>().Any(ContainsRelationshipNavigation);

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
    /// Preserves source-order short-circuit when the right operand has statements:
    /// evaluate left, then run the right statements only if the left requires it
    /// (<c>if (t)</c> for and, <c>if (!t)</c> for or).
    /// </summary>
    private LoweredExpression ShortCircuit(LoweredExpression left, LoweredExpression right, bool whenTrue) {
        var t = _names.Next("t");
        Node gate = whenTrue ? t : new SN.Not(t);
        var thenBlock = new Block(
            [.. right.Statements, new Assignment(t, right.Value)],
            right.Variables);
        List<Node> statements = [.. left.Statements, new Assignment(t, left.Value), new IfStatement(gate, thenBlock)];
        return new LoweredExpression(statements, [.. left.Variables, t], t);
    }

    private static bool IsCount(Node node) =>
        node is Member { MemberName: "Count" };

    private static Node CountConstant(long value) =>
        value is >= int.MinValue and <= int.MaxValue
            ? new Constant((int)value)
            : new Constant(value);

    /// <summary>
    /// Collects the to-one hops an operand reads through from <paramref name="subject"/>
    /// (the comparison's subject, or the hop a navigation's target is read on):
    /// <c>advisor mentor Age</c> and <c>advisor where mentor Age * 2</c> both yield
    /// <c>.Advisor</c> and <c>.Advisor.Mentor</c>. Arithmetic and
    /// <c>not</c>/<c>and</c>/<c>or</c> are looked through; any other operand adds no
    /// hop (a quantifier body runs against its loop item and guards its own
    /// comparisons). The chain stops at a subscription binder name
    /// (<c>order Total</c>) and at a navigation to a condition
    /// (<c>advisor where Age &lt; 30</c>): lowering guards that navigation's own
    /// hop, and hops inside the condition are guarded by the comparisons there.
    /// Known issue: lowering treats a binder name as the binder at any depth, so
    /// <c>advisor order Total</c> reads the binder, not the advisor's order. The
    /// chain matches lowering rather than guarding a hop lowering never reads.
    /// </summary>
    /// <param name="subjectPath">Dotted member path of <paramref name="subject"/>, used to
    /// recognise the same hop reached twice (nodes compare by identity, not shape).</param>
    private void CollectValueHops(
        DomainExpression operand, Node subject, string subjectPath,
        List<Node> hops, HashSet<string> seenPaths) {
        switch (operand) {
            case Ontology.Add or Ontology.Subtract or Ontology.Multiply or Ontology.Divide
                or Ontology.Not or Ontology.And or Ontology.Or:
                foreach (var child in operand.Children.OfType<DomainExpression>())
                    CollectValueHops(child, subject, subjectPath, hops, seenPaths);
                break;
            case RelationshipNavigation rn:
                CollectHopChain(rn, subject, subjectPath, hops, seenPaths);
                break;
        }
    }

    private void CollectHopChain(
        RelationshipNavigation rn, Node subject, string subjectPath,
        List<Node> hops, HashSet<string> seenPaths) {
        if (_parameters.ContainsKey(rn.RelationshipName)
            || IsCollectionNav(rn.RelationshipName)
            || IsPathPrefixPredicate(rn.TargetProperty))
            return;
        var navName = ResolveNavName(rn.RelationshipName);
        var hopPath = subjectPath + "." + navName;
        var hop = new Member(subject, navName);
        if (seenPaths.Add(hopPath))
            hops.Add(hop);
        CollectValueHops(rn.TargetProperty, hop, hopPath, hops, seenPaths);
    }

    /// <summary>
    /// If <paramref name="valueNode"/> is a string literal and <paramref name="otherSide"/>
    /// is a property access for an enum-typed property, returns a Syntax node that
    /// references the enum member qualified by the type name (e.g. <c>PatronStatus.Active</c>).
    /// Returns null if no substitution is needed.
    /// </summary>
    private Node? FixEnumLiteral(DomainExpression valueExpr, DomainExpression otherSide, Node otherSideNode) {
        if (otherSide is not PropertyAccess prop
            || _enumPropertyNames is null
            || !_enumPropertyNames.TryGetValue(prop.Name, out var enumTypeName))
            return null;

        // String form: Status == "Active" → PatronStatus.Active
        if (valueExpr is Literal { Value: string strVal }
            && !string.IsNullOrEmpty(strVal)
            && strVal is not "true" and not "false" and not "null"
            && !char.IsDigit(strVal[0])) {
            return new Member(new NamedTypeReference(enumTypeName), strVal);
        }

        // Bare member form: Status is Active → PatronStatus.Active. A bare name that
        // is itself a property stays an instance read (an enum-to-enum comparison).
        if (valueExpr is PropertyAccess memberAccess
            && !_enumPropertyNames.ContainsKey(memberAccess.Name)
            && _propertyTypeResolver?.Invoke(memberAccess.Name) is null
            && _domain.Types.OfType<EnumType>().Any(e =>
                string.Equals(e.Name, enumTypeName, StringComparison.Ordinal)
                && e.MemberNames.Contains(memberAccess.Name, StringComparer.Ordinal))) {
            return new Member(new NamedTypeReference(enumTypeName), memberAccess.Name);
        }

        return null;
    }

    private enum QuantifierKind { Any, All, None, Count }

    /// <summary>
    /// One quantifier: <c>result = init; foreach (var itemN in collection) { body; if (...) }</c>,
    /// value is <c>result</c>.
    /// <list type="bullet">
    /// <item><c>any</c>: true at the first match; false when nothing matches or there are no items.</item>
    /// <item><c>all</c>: false at the first miss; also false when there are no items (via sawItem).</item>
    /// <item><c>none</c>: false at the first match; true otherwise.</item>
    /// <item><c>count … where</c>: the number of matching items.</item>
    /// </list>
    /// </summary>
    private LoweredExpression LowerFilteredQuantifier(
        string relationshipName, DomainExpression body, QuantifierKind kind) {
        var collection = new Member(_currentSubject, ResolveNavName(relationshipName));
        var item = _names.Next("item");
        var bodyLowered = LowerQuantifierBody(relationshipName, body, item);

        var result = _names.Next(kind switch {
            QuantifierKind.Any => "any",
            QuantifierKind.All => "all",
            QuantifierKind.None => "none",
            _ => "count",
        });
        Node initial = kind switch {
            QuantifierKind.Any => new Constant(false),
            QuantifierKind.All => new Constant(true),
            QuantifierKind.None => new Constant(true),
            _ => new Constant(0L),
        };
        Node onItem = kind switch {
            QuantifierKind.Any => new IfStatement(bodyLowered.Value, new Block([
                new Assignment(result, new Constant(true)), new BreakStatement()])),
            QuantifierKind.All => new IfStatement(new SN.Not(bodyLowered.Value), new Block([
                new Assignment(result, new Constant(false)), new BreakStatement()])),
            QuantifierKind.None => new IfStatement(bodyLowered.Value, new Block([
                new Assignment(result, new Constant(false)), new BreakStatement()])),
            _ => new IfStatement(bodyLowered.Value, new Block([
                new Assignment(result, new SN.Add(result, new Constant(1L)))])),
        };

        if (kind == QuantifierKind.All) {
            // `all` over no items is false, so the loop also records that it saw one.
            var sawItem = _names.Next("sawItem");
            Node[] statements = [
                new Assignment(result, initial),
                new Assignment(sawItem, new Constant(false)),
                new ForEachLoop(item, collection,
                    new Block(
                        [.. bodyLowered.Statements, new Assignment(sawItem, new Constant(true)), onItem],
                        bodyLowered.Variables)),
            ];
            return new LoweredExpression(statements, [result, sawItem], new SN.And(sawItem, result));
        }

        Node[] loopStatements = [
            new Assignment(result, initial),
            new ForEachLoop(item, collection,
                new Block([.. bodyLowered.Statements, onItem], bodyLowered.Variables)),
        ];
        return new LoweredExpression(loopStatements, [result], result);
    }

    /// <summary>
    /// Lowers a quantifier body against the loop's <paramref name="item"/>, using the
    /// related entity's enum properties. Nested quantifiers in the body produce
    /// statements that run inside the outer loop, once per item, and share this
    /// pass's local-name generator so names stay unique.
    /// </summary>
    private LoweredExpression LowerQuantifierBody(
        string relationshipName, DomainExpression body, Variable item) =>
        LowerAgainstEntity(ResolveRelationshipTarget(relationshipName), body, item);

    /// <summary>
    /// Lowers <paramref name="expression"/> against <paramref name="subject"/> using
    /// <paramref name="entityName"/> as the entity whose members the subject exposes
    /// (that entity's SourceEntityName and enum map). Path-prefix hops and quantifier
    /// bodies share this so nested resolution sees the target. Shares this pass's
    /// local-name generator.
    /// </summary>
    private LoweredExpression LowerAgainstEntity(
        string? entityName, DomainExpression expression, Node subject) {
        IReadOnlyDictionary<string, string>? enums = _enumPropertyNames;
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
}