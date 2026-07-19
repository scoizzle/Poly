using Poly.DomainModeling.Effects;
using Poly.Syntax.Nodes;

namespace Poly.DomainModeling.Lowering;

/// <summary>
/// Lowers domain <see cref="Effect"/> types to Syntax AST nodes for VM
/// execution via <see cref="Interpreter.Compile"/>. Composes with
/// <see cref="DomainExpressionLoweringPass"/> for expression-heavy effects
/// like <see cref="AssignEffect"/> and <see cref="ConditionalEffect"/>.
///
/// <para>Some effects (<see cref="StageTransitionEffect"/>,
/// <see cref="CreateEntityInstance"/>, <see cref="InvokeActionEffect"/>)
/// execute directly on <see cref="DomainEntityInstance"/> rather than
/// through the VM — they produce <c>null</c> from <see cref="Route"/>
/// and are handled by the caller.</para>
/// </summary>
public sealed class EffectLoweringPass : EffectDispatch<Node?> {
    private readonly Entity _entity;
    private readonly Domain? _domain;
    private readonly DomainExpressionLoweringPass _expressionPass;
    private readonly bool _useThisReference;
    private readonly bool _lowerStageTransitions;

    public EffectLoweringPass(Entity entity, Node subject)
        : this(entity, new LoweringContext(subject)) { }

    public EffectLoweringPass(Entity entity, LoweringContext context) {
        _entity = entity;
        _domain = context.Domain;
        _useThisReference = context.UseThisReference;
        _lowerStageTransitions = context.LowerStageTransitions;
        _expressionPass = new DomainExpressionLoweringPass(context);
        Subject = context.UseThisReference && context.Subject is Parameter { Name: "entity" }
            ? new ThisReference()
            : context.Subject;
    }

    /// <summary>The Syntax AST node representing the current entity instance.</summary>
    public Node Subject { get; }

    /// <summary>
    /// Lowers <paramref name="effect"/> to a Syntax AST node suitable for VM
    /// compilation, or returns <c>null</c> when the effect must be executed
    /// directly on a <see cref="DomainEntityInstance"/>.
    /// </summary>
    public Node? TryLowerVmNode(Effect effect) => Route(effect);

    protected override Node? Default() => null;

    protected override Node? Assign(AssignEffect a) {
        var target = _expressionPass.Lower(a.Target, Subject);
        var value = _expressionPass.Lower(a.Value, Subject);
        return new Assignment(target, value);
    }

    /// <summary>
    /// Lowers a stage transition. When <see cref="_lowerStageTransitions"/> is true,
    /// emits the target stage's entry effects followed by a CurrentStage assignment.
    /// Otherwise returns null so the runtime calls <see cref="DomainEntityInstance.TransitionStage"/>.
    /// </summary>
    protected override Node? StageTransition(StageTransitionEffect t) {
        if (!_lowerStageTransitions) return null;

        var nodes = new List<Node>();

        // Include entry effects from the target stage
        var targetStage = _entity.Stages.FirstOrDefault(s =>
            string.Equals(s.Name, t.TargetStage.StageName, StringComparison.Ordinal));
        if (targetStage is not null) {
            foreach (var entryEffect in targetStage.OnEntryEffects) {
                var lowered = Route(entryEffect);
                if (lowered is not null)
                    nodes.Add(lowered);
            }
        }

        var stageEnumType = new NamedTypeReference($"{_entity.Name}Stage");
        nodes.Add(new Assignment(
            new Member(Subject, "CurrentStage"),
            new Member(stageEnumType, t.TargetStage.StageName)
        ));

        return nodes.Count == 1 ? nodes[0] : new Block(nodes);
    }

    /// <summary>
    /// Lowers invoke effects for C# codegen mode. Self-invoke (no TargetRelationship)
    /// becomes <c>this.ActionName(args)</c>. Cross-entity invoke becomes
    /// <c>this.TargetRelationship.ActionName(args)</c>. Quantified/collection invoke
    /// still returns null (no C# lowering yet).
    /// </summary>
    protected override Node? InvokeAction(InvokeActionEffect i) {
        if (!_lowerStageTransitions) return null;
        // Quantified/collection invoke not yet lowerable
        if (i.Quantifier is not null) return null;

        var args = new List<Node>();
        foreach (var binding in i.ParameterBindings) {
            args.Add(_expressionPass.Lower(binding.Expression, Subject));
        }

        var target = i.TargetRelationship is not null
            ? (Node)new Member(Subject, i.TargetRelationship)
            : Subject;

        return new Invoke(new Member(target, i.ActionName), [.. args]);
    }

    /// <summary>
    /// Lowers a CompositeEffect. Only VM-compilable sub-effects are included;
    /// direct-execution sub-effects (which return null) are recorded as
    /// <see cref="Comment"/> nodes so the lowered AST preserves information
    /// about what was not lowered. The Syntax AST's Block requires at least
    /// one expression (type inference constraint), so Comments serve as
    /// both documentation and a structural placeholder.
    /// </summary>
    protected override Node? Composite(CompositeEffect c) {
        var nodes = new List<Node>();
        foreach (var sub in c.Effects) {
            var lowered = Route(sub);
            if (lowered is not null)
                CollectNode(nodes, lowered);
            else
                nodes.Add(new Comment(DescribeEffect(sub)));
        }
        return new Block(nodes);
    }

    protected override Node? Conditional(ConditionalEffect c) {
        var condition = _expressionPass.Lower(c.Condition, Subject);
        var thenNodes = new List<Node>();
        foreach (var sub in c.ThenEffects) {
            var lowered = Route(sub);
            if (lowered is not null)
                CollectNode(thenNodes, lowered);
            else
                thenNodes.Add(new Comment(DescribeEffect(sub)));
        }

        if (c.ElseEffects is not { Count: > 0 })
            return new IfStatement(condition, new Block(thenNodes));

        var elseNodes = new List<Node>();
        foreach (var sub in c.ElseEffects) {
            var lowered = Route(sub);
            if (lowered is not null)
                CollectNode(elseNodes, lowered);
            else
                elseNodes.Add(new Comment(DescribeEffect(sub)));
        }

        return new IfStatement(condition, new Block(thenNodes), new Block(elseNodes));
    }

    /// <summary>Adds a lowered node to a list. Flattens Block children.
    /// If null, no node is added (the calling code handled the comment).</summary>
    private static void CollectNode(List<Node> nodes, Node? lowered) {
        if (lowered is null) return;
        if (lowered is Block b)
            nodes.AddRange(b.Nodes);
        else
            nodes.Add(lowered);
    }

    /// <summary>
    /// Lowers CreateEntityInstance for C# mode. Emits <c>new TypeName(arg1, arg2, ...)</c>,
    /// matching initializer bindings to constructor parameters by property name.
    /// When <see cref="_domain"/> is null or the target entity is not found, returns null.
    /// </summary>
    protected override Node? CreateEntityInstance(CreateEntityInstance cei) {
        if (!_lowerStageTransitions || _domain is null) return null;

        var targetEntity = _domain.Types.OfType<Entity>().FirstOrDefault(e =>
            string.Equals(e.Name, cei.Type.TypeName, StringComparison.Ordinal));
        if (targetEntity is null) return null;

        var args = BuildConstructorArgs(cei.Initializers, targetEntity);
        return new New(new NamedTypeReference(targetEntity.Name), [.. args]);
    }

    /// <summary>
    /// Lowers CreateEntityInRelationshipEffect for C# mode. Emits the same `new`
    /// as CreateEntityInstance but the caller will need to wire the relationship.
    /// </summary>
    protected override Node? CreateEntityInRelationship(CreateEntityInRelationshipEffect cr) {
        if (!_lowerStageTransitions || _domain is null) return null;

        // Find relationship to determine target type
        var rel = _domain.Relationships.FirstOrDefault(r =>
            string.Equals(r.Name, cr.RelationshipName, StringComparison.Ordinal));
        if (rel is null) return null;

        var targetEntity = _domain.Types.OfType<Entity>().FirstOrDefault(e =>
            string.Equals(e.Name, rel.Target.TypeName, StringComparison.Ordinal));
        if (targetEntity is null) return null;

        var args = BuildConstructorArgs(cr.Initializers, targetEntity);
        return new New(new NamedTypeReference(targetEntity.Name), [.. args]);
    }

    /// <summary>
    /// Lowers DeleteEntityInstance for C# mode. Emits <c>this.IsDeleted = true;</c>.
    /// </summary>
    protected override Node? DeleteEntity(DeleteEntityInstance _) {
        if (!_lowerStageTransitions) return null;
        return new Assignment(new Member(Subject, "IsDeleted"), new Constant(true));
    }

    /// <summary>Builds constructor arguments matching initializers to entity property order.</summary>
    private List<Node> BuildConstructorArgs(
        IReadOnlyList<PropertyBinding> initializers, Entity targetEntity) {
        var initMap = new Dictionary<string, DomainExpression>(StringComparer.Ordinal);
        foreach (var init in initializers)
            initMap[init.PropertyName] = init.Expression;

        var args = new List<Node>();
        foreach (var prop in targetEntity.Properties) {
            if (initMap.TryGetValue(prop.Name, out var expr))
                args.Add(_expressionPass.Lower(expr, Subject));
            else
                args.Add(new Constant(null)); // null default for unset properties
        }
        return args;
    }

    /// <summary>
    /// Returns a human-readable description of why <paramref name="effect"/>
    /// cannot be lowered, including effect-specific detail like action names.
    /// </summary>
    private static string DescribeEffect(Effect effect) => effect switch {
        InvokeActionEffect i when i.TargetRelationship is null => $"invoke {i.ActionName}",
        InvokeActionEffect i when i.TargetRelationship is not null && i.Quantifier is null => $"invoke {i.TargetRelationship}.{i.ActionName}",
        InvokeActionEffect i => $"Cannot lower: invoke {i.ActionName} (InvokeActionEffect)",
        StageTransitionEffect s => $"transition to {s.TargetStage.StageName} (StageTransitionEffect)",
        CreateEntityInstance cei => $"create {cei.Type.TypeName}",
        CreateEntityInRelationshipEffect cr => $"create in {cr.RelationshipName}",
        DeleteEntityInstance => $"delete",
        LinkRelationshipEffect l => $"Cannot lower: link {l.RelationshipName} (LinkRelationshipEffect)",
        UnlinkRelationshipEffect u => $"Cannot lower: unlink {u.RelationshipName} (UnlinkRelationshipEffect)",
        TransitionRelationshipEffect tre => $"Cannot lower: transition {tre.RelationshipName} to {tre.TargetStage.StageName} (TransitionRelationshipEffect)",
        _ => $"Cannot lower: {effect.GetType().Name}"
    };
}