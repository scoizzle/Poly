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
    private readonly bool _useThisReference;
    private readonly IReadOnlyDictionary<string, string>? _enumPropertyNames;
    private readonly Func<string, string>? _navigationNameResolver;
    private readonly Func<string, bool>? _isCollectionNavigation;
    private readonly Func<string, bool>? _isRelationshipNavigation;
    private readonly Func<string, string?>? _propertyTypeResolver;
    private readonly Domain? _domain;
    private readonly INodeMetadataProvider? _analysis;
    private readonly ExpressionMeaning _meaning;
    private readonly ExpressionFormRegistry? _forms;
    private string? _sourceEntityName;
    private Node _currentSubject = null!;
    private int _quantifierSequence;

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
        _context = context;
        _parameters = context.Parameters ?? new Dictionary<string, Node>();
        _actionParameterNames = context.ActionParameterNames;
        _useThisReference = context.UseThisReference;
        _enumPropertyNames = context.EnumPropertyNames;
        _navigationNameResolver = context.NavigationNameResolver;
        _isCollectionNavigation = context.IsCollectionNavigation;
        _isRelationshipNavigation = context.IsRelationshipNavigation;
        _propertyTypeResolver = context.PropertyTypeResolver;
        _domain = context.Domain;
        _analysis = context.Analysis;
        _meaning = context.Meaning ?? ExpressionMeaning.Empty;
        _forms = context.Forms;
        _sourceEntityName = context.SourceEntityName;
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
        _currentSubject = _useThisReference && subject is Parameter { Name: "entity" }
            ? new ThisReference()
            : subject;
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
        // Export (UseThisReference) resolves session ident folds (clocks) and Guid
        // through LowerDefaultExpression. Bare fragments without those tables stay
        // Member so analysis can fail closed on unknown property.
        if (_useThisReference) {
            var runtime = EffectLoweringPass.LowerDefaultExpression(
                p, typeHint: null, EffectiveMeaning, EffectiveForms);
            if (runtime is not null) return LoweredExpression.Of(runtime);
        }

        // When UseThisReference is set, action parameters render as bare names
        if (_useThisReference && _actionParameterNames?.Contains(p.Name) == true)
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
        // Runtime: ExistsRelated short-circuit → false when unlinked so require
        // EvaluatePolicy fills FailedGuards without throw; GetRelatedOne still
        // throws on many (fail closed). Export: NullForgiving for CS8602; require
        // gates own DomainResult.Failure ("requires a linked") in
        // BuildActionBodyWithGuards before the policy bool is called.
        if (!_useThisReference) {
            var exists = new Invoke(
                new Member(_currentSubject, "ExistsRelated"),
                new Constant(rn.RelationshipName));
            var related = new Invoke(
                new Member(_currentSubject, "GetRelatedOne"),
                new Constant(rn.RelationshipName));
            var targetName = ResolveRelationshipTarget(rn.RelationshipName);
            Node typedHop = targetName is not null
                ? new TypeCast(related, new TypeReference(targetName))
                : related;
            var whenPresent = Route(rn.TargetProperty, typedHop, targetName);
            if (whenPresent.Statements.Count > 0) {
                throw new NotSupportedException(
                    "A path-prefix hop cannot contain a quantifier: the present-side " +
                    "expression produced statements, and analysis already rejects this form.");
            }
            return LoweredExpression.Of(new Conditional(exists, whenPresent.Value, new Constant(false)));
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

    private LoweredExpression Route(DomainExpression expr, Node subject, string? sourceEntityName = null) {
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

    /// <summary>
    /// Child route for library handlers, which can only embed a value in expression
    /// position. A child that produced statements fails loud rather than becoming a Block.
    /// </summary>
    private Node RouteValue(DomainExpression expr) {
        var lowered = Route(expr);
        if (lowered.Statements.Count > 0) {
            throw new NotSupportedException(
                $"Cannot lower a {expr.GetType().Name} child that produced statements " +
                "(a loop must sit in statement position).");
        }
        return lowered.Value;
    }

    protected override LoweredExpression Exists(Exists e) {
        if (!_useThisReference && e.Target is PropertyAccess pa && IsRelationship(pa.Name)) {
            return LoweredExpression.Of(new Invoke(
                new Member(_currentSubject, "ExistsRelated"),
                new Constant(pa.Name)));
        }
        // Collection (`many`) relationship: the export's `collection != null` is
        // always true (ctor-initialized) while the runtime answers store-link
        // presence (false on empty) — lower to a real non-empty check instead.
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
        if (!_useThisReference && ne.Target is PropertyAccess pa && IsRelationship(pa.Name)) {
            return LoweredExpression.Of(new SN.Not(new Invoke(
                new Member(_currentSubject, "ExistsRelated"),
                new Constant(pa.Name))));
        }
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

    protected override LoweredExpression Add(Add a) {
        if (EffectiveMeaning.Lowering.TryLower(a, RouteValue, _propertyTypeResolver, out var node))
            return LoweredExpression.Of(node);
        var left = Route(a.Left);
        var right = Route(a.Right);
        return LoweredExpression.Combine([left, right], new SN.Add(left.Value, right.Value));
    }

    protected override LoweredExpression Subtract(Subtract s) {
        if (EffectiveMeaning.Lowering.TryLower(s, RouteValue, _propertyTypeResolver, out var node))
            return LoweredExpression.Of(node);
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
        var t = new Variable($"t{_quantifierSequence++}");
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
        var seq = _quantifierSequence++;
        var item = new Variable($"item{seq}");
        var bodyLowered = LowerQuantifierBody(relationshipName, body, item);

        var result = new Variable(kind switch {
            QuantifierKind.Any => $"any{seq}",
            QuantifierKind.All => $"all{seq}",
            QuantifierKind.None => $"none{seq}",
            _ => $"count{seq}",
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
            var sawItem = new Variable($"sawItem{seq}");
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
    /// statements that run inside the outer loop, once per item, and continue this
    /// pass's item/result numbering so names stay unique.
    /// </summary>
    private LoweredExpression LowerQuantifierBody(
        string relationshipName, DomainExpression body, Variable item) {
        var targetName = ResolveRelationshipTarget(relationshipName);
        IReadOnlyDictionary<string, string>? enums = _enumPropertyNames;
        if (_domain is not null && targetName is not null) {
            var targetEntity = _domain.Types.OfType<Entity>().FirstOrDefault(e =>
                string.Equals(e.Name, targetName, StringComparison.Ordinal));
            if (targetEntity is not null)
                enums = DomainToCSharpExporter.GetEnumPropertyNames(
                    targetEntity, _domain, _analysis);
        }
        var nested = new DomainExpressionLoweringPass(_context with {
            EnumPropertyNames = enums,
            SourceEntityName = targetName ?? _sourceEntityName
        });
        nested._quantifierSequence = _quantifierSequence;
        var lowered = nested.LowerExpression(body, item);
        _quantifierSequence = nested._quantifierSequence;
        return lowered;
    }

    protected override LoweredExpression Library(DomainExpression expr) {
        if (EffectiveMeaning.Lowering.TryLower(expr, RouteValue, _propertyTypeResolver, out var node))
            return LoweredExpression.Of(node);
        throw new NotSupportedException(
            $"DomainExpression node type '{expr.GetType().Name}' is not supported");
    }
}
