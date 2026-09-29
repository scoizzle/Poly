using System.Diagnostics.CodeAnalysis;
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
/// counterparts. Filtered quantifiers become foreach loops whose statements
/// sit immediately before the statement that reads the result.
/// </remarks>
public sealed class DomainExpressionLoweringPass : DomainExpressionDispatch<LoweredExpression> {
    private readonly LoweringContext _context;
    private readonly IReadOnlyDictionary<string, Node> _parameters;
    private readonly HashSet<string>? _actionParameterNames;
    private readonly IReadOnlyDictionary<string, string>? _enumPropertyNames;
    private readonly Func<string, string>? _navigationNameResolver;
    private readonly Func<string, bool>? _isCollectionNavigation;
    private readonly Func<string, string?>? _propertyTypeResolver;
    private readonly Domain? _domain;
    private readonly INodeMetadataProvider? _analysis;
    private readonly ExpressionMeaning _meaning;
    private readonly ExpressionFormRegistry? _forms;
    private Node _currentSubject = null!;
    private readonly LocalNames _names;

    /// <param name="parameters">
    /// Optional map of parameter names to their Syntax AST nodes.
    /// When a ParameterAccess is encountered, its name is looked up here.
    /// If absent, a fresh Parameter node is created.
    /// </param>
    public DomainExpressionLoweringPass(IReadOnlyDictionary<string, Node>? parameters = null)
        : this(new LoweringContext(new Parameter("entity"), parameters)) { }

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

    protected override LoweredExpression PropertyAccess(PropertyAccess p) {
        // Session ident folds (clocks) and Guid through LowerDefaultExpression.
        // Bare fragments without those tables stay Member so analysis can fail
        // closed on unknown property.
        var runtime = EffectLoweringPass.LowerDefaultExpression(
            p, typeHint: null, EffectiveMeaning, EffectiveForms);
        if (runtime is not null) return LoweredExpression.Of(runtime);

        // Action parameters win over a property of the same name, including
        // inside a quantifier body (the nested pass inherits ActionParameterNames).
        if (_actionParameterNames?.Contains(p.Name) == true)
            return LoweredExpression.Of(new Parameter(p.Name));
        return LoweredExpression.Of(new Member(_currentSubject, ResolveName(p.Name)));
    }

    /// <summary>Applies the navigation name resolver (DSL nav → generated member name).</summary>
    private string ResolveName(string name) => _navigationNameResolver?.Invoke(name) ?? name;

    protected override LoweredExpression ParameterAccess(ParameterAccess p)
        => LoweredExpression.Of(
            _parameters.TryGetValue(p.Name, out var param) ? param : new Parameter(p.Name));

    protected override LoweredExpression Literal(Literal l)
        => LoweredExpression.Of(new Constant(l.Value));

    protected override LoweredExpression OwnedAccess(OwnedAccess oa)
        => Route(oa.Inner, new Member(_currentSubject, ResolveName(oa.OwnedName)));

    protected override LoweredExpression RelationshipNavigation(RelationshipNavigation rn) {
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
        // Predicate leaves: an unlinked to-one is false. A statement-free predicate
        // leaf is `rel != null && <leaf>`. When the leaf has statements (a quantifier
        // on the target), those statements run only inside `if (rel != null)` after
        // a temp is set to false. Value leaves stay the hop. NullForgiving on the
        // hop is CS8602 only; require gates own DomainResult.Failure
        // ("requires a linked") in BuildActionBodyWithGuards.
        // Collection hops cannot be a singular path-prefix — fail closed (use any/all).
        if (IsCollectionNav(rn.RelationshipName)) {
            throw new InvalidOperationException(
                $"Path-prefix on relationship '{rn.RelationshipName}' requires exactly one linked target. " +
                "Use any/all quantifiers for collections.");
        }
        var relMember = new Member(_currentSubject, ResolveNavName(rn.RelationshipName));
        var leaf = LowerAgainstEntity(
            ResolveRelationshipTarget(rn.RelationshipName),
            rn.TargetProperty,
            new NullForgiving(relMember));
        if (!IsPathPrefixPredicate(rn.TargetProperty))
            return leaf;
        var exists = new NotEqual(relMember, new Constant(null));
        if (leaf.Statements.Count == 0)
            return leaf with { Value = new SN.And(exists, leaf.Value) };
        var t = _names.Next("t");
        var thenBlock = new Block(
            [.. leaf.Statements, new Assignment(t, leaf.Value)],
            leaf.Variables);
        return new LoweredExpression(
            [new Assignment(t, new Constant(false)), new IfStatement(exists, thenBlock)],
            [t],
            t);
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

    /// <summary>
    /// Library handlers receive a value-only route. This records each child's
    /// statements and combines them with the handler's node.
    /// </summary>
    private bool TryLowerLibrary(DomainExpression expr, [NotNullWhen(true)] out LoweredExpression? lowered) {
        var children = new List<LoweredExpression>();
        Node RouteChild(DomainExpression child) {
            var routed = Route(child);
            children.Add(routed);
            return routed.Value;
        }
        if (EffectiveMeaning.Lowering.TryLower(expr, RouteChild, _propertyTypeResolver, out var node)) {
            lowered = LoweredExpression.Combine(children, node);
            return true;
        }
        lowered = null;
        return false;
    }

    protected override LoweredExpression Exists(Exists e) {
        // Collection (`many`) relationship: ctor-initialized lists are never null;
        // store-link presence is a non-empty check. Simulate binds the same Count
        // member through the dictionary nav.
        if (e.Target is PropertyAccess col && IsCollectionNav(col.Name)) {
            var target = Route(e.Target);
            return target with {
                Value = new NotEqual(new Member(target.Value, "Count"), new Constant(0))
            };
        }
        var other = Route(e.Target);
        return other with { Value = new NotEqual(other.Value, new Constant(null)) };
    }

    protected override LoweredExpression NotExists(NotExists ne) {
        if (ne.Target is PropertyAccess col && IsCollectionNav(col.Name)) {
            var target = Route(ne.Target);
            return target with {
                Value = new Equal(new Member(target.Value, "Count"), new Constant(0))
            };
        }
        var other = Route(ne.Target);
        return other with { Value = new Equal(other.Value, new Constant(null)) };
    }

    private bool IsCollectionNav(string name) =>
        _isCollectionNavigation?.Invoke(name) == true;

    private string? ResolveRelationshipTarget(string relationshipName) {
        if (_domain is null || _context.SourceEntityName is null)
            return null;
        var source = _domain.Types.OfType<Entity>().FirstOrDefault(e =>
            string.Equals(e.Name, _context.SourceEntityName, StringComparison.Ordinal));
        var rel = source?.Navigations.FirstOrDefault(n =>
            string.Equals(n.Name, relationshipName, StringComparison.Ordinal));
        return rel?.Target.TypeName;
    }

    protected override LoweredExpression Add(Add a) {
        if (TryLowerLibrary(a, out var lowered))
            return lowered;
        var left = Route(a.Left);
        var right = Route(a.Right);
        return LoweredExpression.Combine([left, right], new SN.Add(left.Value, right.Value));
    }

    protected override LoweredExpression Subtract(Subtract s) {
        if (TryLowerLibrary(s, out var lowered))
            return lowered;
        var left = Route(s.Left);
        var right = Route(s.Right);
        return LoweredExpression.Combine([left, right], new SN.Subtract(left.Value, right.Value));
    }

    protected override LoweredExpression Multiply(Multiply m) {
        var left = Route(m.Left);
        var right = Route(m.Right);
        return LoweredExpression.Combine([left, right], new SN.Multiply(left.Value, right.Value));
    }

    protected override LoweredExpression Divide(Divide d) {
        var left = Route(d.Left);
        var right = Route(d.Right);
        return LoweredExpression.Combine([left, right], new SN.Divide(left.Value, right.Value));
    }

    protected override LoweredExpression And(And a) {
        var left = Route(a.Left);
        var right = Route(a.Right);
        if (right.Statements.Count == 0)
            return LoweredExpression.Combine([left, right], new SN.And(left.Value, right.Value));
        return ShortCircuit(left, right, whenTrue: true);
    }

    protected override LoweredExpression Or(Or o) {
        var left = Route(o.Left);
        var right = Route(o.Right);
        if (right.Statements.Count == 0)
            return LoweredExpression.Combine([left, right], new SN.Or(left.Value, right.Value));
        return ShortCircuit(left, right, whenTrue: false);
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

    protected override LoweredExpression Not(Not n) {
        var operand = Route(n.Operand);
        return operand with { Value = new SN.Not(operand.Value) };
    }

    protected override LoweredExpression Comparison(Comparison c) {
        var left = Route(c.Left);
        var right = Route(c.Right);
        var loweredLeft = left.Value;
        var loweredRight = right.Value;

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
        Node value;
        if (c.Kind == ComparisonKind.Equal
            && loweredRight is Constant { Value: bool b }) {
            value = b ? loweredLeft : new SN.Not(loweredLeft);
        }
        else if (c.Kind == ComparisonKind.NotEqual
            && loweredRight is Constant { Value: bool b2 }) {
            value = b2 ? new SN.Not(loweredLeft) : loweredLeft;
        }
        else {
            value = c.Kind switch {
                ComparisonKind.Equal => new Equal(loweredLeft, loweredRight),
                ComparisonKind.NotEqual => new NotEqual(loweredLeft, loweredRight),
                ComparisonKind.LessThan => new LessThan(loweredLeft, loweredRight),
                ComparisonKind.LessThanOrEqual => new LessThanOrEqual(loweredLeft, loweredRight),
                ComparisonKind.GreaterThan => new GreaterThan(loweredLeft, loweredRight),
                ComparisonKind.GreaterThanOrEqual => new GreaterThanOrEqual(loweredLeft, loweredRight),
                _ => throw new NotSupportedException($"Comparison kind '{c.Kind}' is not supported."),
            };
        }

        return LoweredExpression.Combine([left, right], value);
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

    // Filtered any/all/none/count: a foreach over the navigation collection
    // (the same Member(subject, PascalCase(rel)) that ForEachInvoke loops over).
    // The quantifier's statements are the init + loop; its value is the result variable
    // (or sawItem && result for all, so an empty collection is false).
    // Bare `count Rel` is the collection's Count.
    protected override LoweredExpression AnyExpr(AnyExpr a) =>
        LowerFilteredQuantifier(a.RelationshipName, a.Body, QuantifierKind.Any);

    protected override LoweredExpression AllExpr(AllExpr a) =>
        LowerFilteredQuantifier(a.RelationshipName, a.Body, QuantifierKind.All);

    protected override LoweredExpression NoneExpr(NoneExpr n) =>
        LowerFilteredQuantifier(n.RelationshipName, n.Body, QuantifierKind.None);

    protected override LoweredExpression CountExpr(CountExpr c) {
        if (c.Body is null)
            return LoweredExpression.Of(new Member(
                new Member(_currentSubject, ResolveNavName(c.RelationshipName)),
                "Count"));
        return LowerFilteredQuantifier(c.RelationshipName, c.Body, QuantifierKind.Count);
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
        if (_domain is not null && entityName is not null) {
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

    protected override LoweredExpression Library(DomainExpression expr) {
        if (TryLowerLibrary(expr, out var lowered))
            return lowered;
        throw new NotSupportedException(
            $"DomainExpression node type '{expr.GetType().Name}' is not supported");
    }
}
