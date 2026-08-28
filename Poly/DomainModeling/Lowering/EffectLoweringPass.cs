using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Dispatch;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;

using Syntactic = Poly.Ast.Nodes;

namespace Poly.DomainModeling.Lowering;

/// <summary>
/// Lowers domain <see cref="Effect"/> types to Syntax AST nodes for VM
/// execution via <see cref="Interpreter.Compile"/>. Composes with
/// <see cref="DomainExpressionLoweringPass"/> for expression-heavy effects
/// like <see cref="AssignEffect"/> and <see cref="ConditionalEffect"/>.
///
/// <para>Create / create-in still produce <c>null</c> from <see cref="Route"/>
/// on the runtime path and are handled by EffectExecutor. StageTransition and
/// invoke (self, cross-entity, for-each) are handwritten IR on both runtime
/// and emit — not host-ABI nodes. <c>LowerStageTransitions</c> still gates
/// create / create-in.</para>
///
/// <para>When <see cref="Analysis"/> is set, lowering reads pre-computed
/// <see cref="IAnalysisMetadata"/> instead of re-scanning domain collections.
/// Null-safe — falls back to re-scan when absent.</para>
/// </summary>
public sealed class EffectLoweringPass : EffectDispatch<Node?> {
    private readonly Entity _entity;
    private readonly Domain? _domain;
    private readonly DomainExpressionLoweringPass _expressionPass;
    private readonly INodeMetadataProvider? _analysis;
    private readonly bool _useThisReference;
    private readonly bool _lowerStageTransitions;
    private readonly string? _stageEnumTypeName;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<Node>>? _postTransitionNodes;
    private readonly string? _sourceStageName;
    private readonly IReadOnlyDictionary<string, string>? _enumPropertyNames;
    private readonly LoweringContext _context;
    private readonly bool _emitInstanceNotify;
    private int _forEachInvokeSequence;
    private int _createInProbeSequence;

    /// <summary>Pre-computed analysis metadata provider, when available.</summary>
    public INodeMetadataProvider? Analysis => _analysis;

    public EffectLoweringPass(Entity entity, Node subject)
        : this(entity, new LoweringContext(subject)) { }

    public EffectLoweringPass(Entity entity, LoweringContext context) {
        _entity = entity;
        _context = context;
        _domain = context.Domain;
        _analysis = context.Analysis;
        _useThisReference = context.UseThisReference;
        _lowerStageTransitions = context.LowerStageTransitions;
        _stageEnumTypeName = context.StageEnumTypeName;
        _postTransitionNodes = context.PostTransitionNodes;
        _sourceStageName = context.SourceStageName;
        _enumPropertyNames = context.EnumPropertyNames;
        _emitInstanceNotify = context.EmitInstanceNotify;
        _expressionPass = new DomainExpressionLoweringPass(context with {
            NavigationNameResolver = context.NavigationNameResolver ?? BuildNavigationNameResolver(entity, _domain, _analysis),
            IsCollectionNavigation = context.IsCollectionNavigation
                ?? BuildIsCollectionNavigation(entity, _domain, _analysis),
            PropertyTypeResolver = context.PropertyTypeResolver ?? BuildPropertyTypeResolver(entity)
        });
        Subject = context.UseThisReference && context.Subject is Parameter { Name: "entity" }
            ? new ThisReference()
            : context.Subject;
    }

    /// <summary>
    /// Builds the default DSL-nav-name → generated-member-name resolver for the
    /// entity: relationship navigation names (source-side) pascal-case to the
    /// exporter's property naming (<c>compilations</c> → <c>Compilations</c>);
    /// plain properties keep their DSL name. Analysis metadata is primary;
    /// falls back to the domain relationship list. Identity when no context.
    /// </summary>
    internal static Func<string, string> BuildNavigationNameResolver(
        Entity entity, Domain? domain, INodeMetadataProvider? analysis) {
        if (analysis is not null) {
            var rlm = domain is not null
                ? analysis.GetRelationshipLookup(domain)
                : analysis.GetRelationshipLookup();
            if (rlm is not null) {
                return name => rlm.TryGetRelationship(entity.Name, name, out var rel)
                        ? DomainToCSharpExporter.ToPascalCase(name)
                        : name;
            }
        }
        if (domain is not null) {
            var sourceNavs = entity.Navigations
                .Select(r => r.Name)
                .ToHashSet(StringComparer.Ordinal);
            return name => sourceNavs.Contains(name)
                ? DomainToCSharpExporter.ToPascalCase(name)
                : name;
        }
        return name => name;
    }

    /// <summary>
    /// Builds the default "is this DSL nav name a collection relationship on the
    /// current entity" predicate, mirroring <see cref="BuildNavigationNameResolver"/>
    /// (analysis metadata primary; domain relationship list fallback; false when
    /// unknown). Used to lower <c>Rel exists</c> on <c>many</c> navs to a
    /// <c>.Count != 0</c> check in the export.
    /// </summary>
    internal static Func<string, bool> BuildIsCollectionNavigation(
        Entity entity, Domain? domain, INodeMetadataProvider? analysis) {
        if (analysis is not null) {
            var rlm = domain is not null
                ? analysis.GetRelationshipLookup(domain)
                : analysis.GetRelationshipLookup();
            if (rlm is not null) {
                return name => rlm.TryGetRelationship(entity.Name, name, out var rel)
                    && rel.Cardinality is RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany;
            }
        }
        if (domain is not null) {
            var navCardinalities = entity.Navigations
                .GroupBy(r => r.Name)
                .ToDictionary(g => g.Key, g => g.First().Cardinality, StringComparer.Ordinal);
            return name => navCardinalities.TryGetValue(name, out var card)
                && card is RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany;
        }
        return _ => false;
    }

    /// <summary>
    /// Builds the default property-name → domain-type-name resolver for date-arithmetic
    /// lowering (entity.Properties primary; null when unknown).
    /// </summary>
    internal static Func<string, string?> BuildPropertyTypeResolver(Entity entity) {
        var byName = entity.Properties.ToDictionary(
            p => p.Name, p => p.Type.TypeName, StringComparer.Ordinal);
        foreach (var action in entity.Actions)
            foreach (var p in action.Parameters)
                byName.TryAdd(p.Name, p.Type.TypeName);
        foreach (var stage in entity.Stages)
            foreach (var action in stage.Actions)
                foreach (var p in action.Parameters)
                    byName.TryAdd(p.Name, p.Type.TypeName);
        return name => byName.TryGetValue(name, out var typeName) ? typeName : null;
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

        // Convert enum-valued RHS to qualified enum member access when the
        // target property is enum-typed:  assign Status to "Suspended"
        // on PatronStatus-typed property →  this.Status = PatronStatus.Suspended;
        // and a bare identifier member (assign Status to Suspended) → same.
        // Only in C# export mode (EnumPropertyNames present) — the generated C#
        // must reference the enum type. On the runtime path enum values are stored
        // as strings, so a bare enum member identifier lowers to its name string
        // and a string literal is already a string constant (no qualification).
        if (a.Target is PropertyAccess propAccess) {
            var entityProp = _entity.Properties.FirstOrDefault(p =>
                string.Equals(p.Name, propAccess.Name, StringComparison.Ordinal));

            // Runtime keywords in an assign RHS (assign DueDate to now / today /
            // guid) must adapt to the TARGET property's CLR type — the raw keyword
            // shape (DateTime.UtcNow / DateOnly / Guid) would otherwise be
            // cross-typed (CS0029/CS0019 in the export, wrong-typed stores at runtime).
            // Discovery round5 F2/F3.
            if (entityProp is not null
                && a.Value is PropertyAccess keywordAccess
                && keywordAccess.Name is "Now" or "UtcNow" or "Today" or "Guid") {
                var adapted = LowerDefaultExpression(
                    keywordAccess, new NamedTypeReference(entityProp.Type.TypeName));
                if (adapted is not null) value = adapted;
            }

            if (entityProp is not null
                && DomainToCSharpExporter.TryResolveEnumType(_domain, _analysis, entityProp.Type.TypeName, out var enumType)
                && enumType is not null) {
                if (_enumPropertyNames is not null) {
                    if (a.Value is Literal { Value: string strVal }
                        && !string.IsNullOrEmpty(strVal)) {
                        value = new Member(new NamedTypeReference(enumType.Name), strVal);
                    }
                    else if (a.Value is PropertyAccess pa
                        && enumType.MemberNames.Contains(pa.Name, StringComparer.Ordinal)) {
                        value = new Member(new NamedTypeReference(enumType.Name), pa.Name);
                    }
                }
                else if (a.Value is PropertyAccess pa
                    && enumType.MemberNames.Contains(pa.Name, StringComparer.Ordinal)) {
                    value = new Constant(pa.Name);
                }
            }

            // Date arithmetic (DueDate + 14 → AddDays) is lowered by the expression
            // pass (Add/Subtract) so it applies in every context — nothing more here.
        }

        if (_analysis?.GetMetadata<AssignedMemberConversionMetadata>(a) is { } conversion) {
            value = new Invoke(
                new Member(value, conversion.MethodName),
                [.. conversion.Arguments.Select(arg =>
                    new Member(new NamedTypeReference(arg.TypeName), arg.MemberName))]);
        }

        return new Assignment(target, value);
    }

    /// <summary>
    /// Lowers a stage transition to generic Syntax AST on both runtime and emit:
    /// source-stage exit effects (when known), CurrentStage assignment, target-stage
    /// entry effects (in try), post-transition notification nodes, then
    /// <c>Invoke(Member(Subject, "Notify"), stageName)</c> in finally.
    /// Not a host-ABI node. Not gated on <see cref="LoweringContext.LowerStageTransitions"/>
    /// — that flag still gates create / create-in.
    /// </summary>
    protected override Node? StageTransition(StageTransitionEffect t) {
        if (!_entity.Stages.Any(s =>
            string.Equals(s.Name, t.TargetStage.StageName, StringComparison.Ordinal)))
            return new Block([]);

        var nodes = new List<Node>();

        // Exit/entry effects are best-effort at lowering time: with analysis present a
        // TryGetStage miss implies analysis/domain disagreement (both derive from the
        // same entity.Stages), so skipping is safe; InvokeActionInternal's fail-closed
        // throw covers the dispatch contract.

        // Include exit effects from the source stage (if known).
        // Analysis-present: TryGetStage only (no entity.Stages rescan).
        // Null analysis: structural stage list for non-product/test callers only.
        if (_sourceStageName is not null) {
            Stage? sourceStage = null;
            if (_analysis is not null)
                _analysis.TryGetStage(_entity, _sourceStageName, out sourceStage);
            else
                sourceStage = _entity.Stages.FirstOrDefault(s =>
                    string.Equals(s.Name, _sourceStageName, StringComparison.Ordinal));
            if (sourceStage is not null) {
                foreach (var exitEffect in sourceStage.OnExitEffects) {
                    var lowered = Route(exitEffect);
                    if (lowered is not null)
                        nodes.Add(lowered);
                }
            }
        }

        // Set the target stage BEFORE the target's entry effects run — the runtime
        // TransitionStage sets CurrentStage first, then runs entry effects, so a
        // transition nested inside entry (entry of X → Y) must end at Y, not be
        // overwritten by the outer assignment.
        Node stageValue = _useThisReference || _stageEnumTypeName is not null
            ? new Member(
                new NamedTypeReference(_stageEnumTypeName ?? $"{_entity.Name}Stage"),
                t.TargetStage.StageName)
            : new Constant(t.TargetStage.StageName);
        nodes.Add(new Assignment(
            new Member(Subject, "CurrentStage"),
            stageValue));

        // Entry + C# post-transition fan-out run inside try so Invoke Notify
        // still fires in finally (TransitionStage notified the store in finally).
        var tryNodes = new List<Node>();
        Stage? targetStage = null;
        if (_analysis is not null)
            _analysis.TryGetStage(_entity, t.TargetStage.StageName, out targetStage);
        else
            targetStage = _entity.Stages.FirstOrDefault(s =>
                string.Equals(s.Name, t.TargetStage.StageName, StringComparison.Ordinal));
        if (targetStage is not null) {
            foreach (var entryEffect in targetStage.OnEntryEffects) {
                var lowered = Route(entryEffect);
                if (lowered is not null)
                    tryNodes.Add(lowered);
            }
        }

        if (_postTransitionNodes is not null
            && _postTransitionNodes.TryGetValue(t.TargetStage.StageName, out var postNodes)) {
            foreach (var postNode in postNodes)
                tryNodes.Add(postNode);
        }

        Node tryBody = tryNodes.Count switch {
            0 => new Block([]),
            1 => tryNodes[0],
            _ => new Block(tryNodes)
        };
        if (_emitInstanceNotify) {
            nodes.Add(new TryCatchFinally(
                tryBody,
                CatchClauses: null,
                FinallyBlock: new Invoke(
                    new Member(Subject, "Notify"),
                    new Constant(t.TargetStage.StageName))));
        }
        else if (tryNodes.Count > 0) {
            nodes.Add(tryBody);
        }

        return nodes.Count == 1 ? nodes[0] : new Block(nodes);
    }

    /// <summary>
    /// Self-invoke (no TargetRelationship) is handwritten IR like StageTransition:
    /// <c>Invoke(Member(Subject, actionName), args)</c> on both runtime and emit.
    /// Singular cross-entity invoke is <c>this.Rel.Action(args)</c> with a
    /// linked-target guard that returns <c>DomainResult.Failure</c> before deref
    /// (never a bare NRE). Kitchen dogfood: nested Failure must fail-fast
    /// (<c>if (!result.IsSuccess) return result</c>) so later effects do not run.
    /// Not gated on
    /// <see cref="LoweringContext.LowerStageTransitions"/> — that flag still
    /// gates create / create-in. OneToMany fan-out uses the for-each lowering.
    /// </summary>
    protected override Node? InvokeAction(InvokeActionEffect i) {
        var args = new List<Node>();
        foreach (var binding in i.ParameterBindings) {
            args.Add(_expressionPass.Lower(binding.Expression, Subject));
        }

        // Singular cross-entity invoke (OneToOne): the runtime requires exactly one
        // outbound link (ResolveRelationshipTarget) and fails loud otherwise. Enforce the
        // invariant at the action boundary with a domain Failure BEFORE derefing the nav —
        // never a bare null-forgiving deref (which would crash with an NRE at runtime).
        if (i.TargetRelationship is not null) {
            var navMember = new Member(Subject, DomainToCSharpExporter.ToPascalCase(i.TargetRelationship));
            var guard = new IfStatement(
                new Equal(navMember, new Constant(null!)),
                new Block([new Return(new Invoke(
                    new Member(new TypeReference("DomainResult"), "Failure"),
                    new Constant($"'{i.ActionName}' requires a linked '{i.TargetRelationship}' on entity '{_entity.Name}'.")))]));
            var seq = _forEachInvokeSequence++;
            var resultVar = new Variable($"invoke{seq}");
            var invokeCall = new Invoke(new Member(navMember, i.ActionName), [.. args]);
            return new Block([
                guard,
                new Assignment(resultVar, invokeCall),
                new IfStatement(
                    new Poly.Ast.Nodes.Not(new Member(resultVar, "IsSuccess")),
                    new Block([new Return(resultVar)]))
            ], [resultVar]);
        }

        return WrapInvokeResult(new Invoke(new Member(Subject, i.ActionName), [.. args]));
    }

    private Node WrapInvokeResult(Invoke invokeCall) {
        var seq = _forEachInvokeSequence++;
        var resultVar = new Variable($"invoke{seq}");
        return new Block([
            new Assignment(resultVar, invokeCall),
            new IfStatement(
                new Poly.Ast.Nodes.Not(new Member(resultVar, "IsSuccess")),
                new Block([new Return(resultVar)]))
        ], [resultVar]);
    }

    /// <summary>
    /// Lowers a <see cref="ForEachInvokeEffect"/> (the <c>for Rel as x [where x.Policy |
    /// where x in Stage] invoke x.Action(args)</c> fan-out) to a fail-fast loop over the
    /// source's collection navigation — "fetch all from storage, invoke on every record".
    /// The first failing record fails the whole <c>for</c>; zero matches fail (no vacuous
    /// success). The predicate is a named policy (bool method) or stage membership
    /// (<c>x.CurrentStage == TargetStage.X</c>) on the target entity.
    /// </summary>
    protected override Node? ForEachInvoke(ForEachInvokeEffect e) {
        var relName = e.RelationshipName;
        var navMember = new Member(Subject, DomainToCSharpExporter.ToPascalCase(relName));

        var seq = _forEachInvokeSequence++;
        var loopVar = new Variable($"target{seq}");

        // Predicate → a `continue` guard inside the loop (no LINQ dependency in the
        // standalone export): named policy → target's bool method; stage membership →
        // CurrentStage (string on runtime, enum on emit). Same loop Variable instance
        // so the VM can bind Current onto it.
        Node? predicateGuard = null;
        if (e.Predicate is not null) {
            Node predicateExpr = e.Predicate switch {
                ForEachNamedPolicy p => (Node)new Invoke(
                    new Member(loopVar, p.PolicyName)),
                ForEachStageMembership s => new Equal(
                    new Member(loopVar, "CurrentStage"),
                    _useThisReference || _stageEnumTypeName is not null
                        ? new Member(new NamedTypeReference(TargetStageEnumTypeName(relName)), s.StageName)
                        : new Constant(s.StageName)),
                _ => throw new NotSupportedException($"Unsupported ForEachInvoke predicate '{e.Predicate.GetType().Name}'."),
            };
            predicateGuard = new IfStatement(
                new Poly.Ast.Nodes.Not(predicateExpr),
                new Block([new ContinueStatement()]));
        }

        // Lower the invoke arguments with the binder mapped to the loop variable, so
        // `invoke x.Mark(amount: x Qty)` references the current record.
        var mergedParams = new Dictionary<string, Node>(StringComparer.Ordinal);
        if (_context.Parameters is not null)
            foreach (var kv in _context.Parameters)
                mergedParams[kv.Key] = kv.Value;
        mergedParams[e.BinderName] = loopVar;
        var argPass = new DomainExpressionLoweringPass(_context with { Parameters = mergedParams });
        var args = new List<Node>();
        foreach (var binding in e.ParameterBindings)
            args.Add(argPass.Lower(binding.Expression, Subject));

        var invokeCall = new Invoke(new Member(loopVar, e.ActionName), [.. args]);

        // Fail-fast + zero-matches-fail. Same Variable instances for VM identity.
        var matchedVar = new Variable($"matched{seq}");
        var resultVar = new Variable($"result{seq}");
        var loopBody = new List<Node>();
        if (predicateGuard is not null) loopBody.Add(predicateGuard);
        loopBody.Add(new Assignment(matchedVar, new Constant(true)));
        loopBody.Add(new Assignment(resultVar, invokeCall));
        loopBody.Add(new IfStatement(new Poly.Ast.Nodes.Not(new Member(resultVar, "IsSuccess")),
            new Block([new Return(resultVar)])));
        var loop = new ForEachLoop(loopVar, navMember, new Block(loopBody, [resultVar]));
        var zeroCheck = new IfStatement(
            new Poly.Ast.Nodes.Not(matchedVar),
            new Block([new Return(new Invoke(
                new Member(new TypeReference("DomainResult"), "Failure"),
                new Constant($"for {relName}.{e.ActionName} matched zero targets.")))]));
        return new Block(
            [new Assignment(matchedVar, new Constant(false)), loop, zeroCheck],
            [matchedVar]);
    }

    /// <summary>Resolves the stage enum CLR name for a target entity of a relationship
    /// (e.g. <c>LineStage</c>), from analysis when present, else the default convention.</summary>
    private string TargetStageEnumTypeName(string relationshipName) {
        var targetName = _entity.Navigations.FirstOrDefault(n =>
            string.Equals(n.Name, relationshipName, StringComparison.Ordinal))?.Target.TypeName;
        if (targetName is not null && _domain is not null) {
            var targetEntity = _domain.Types.OfType<Entity>()
                .FirstOrDefault(t => string.Equals(t.Name, targetName, StringComparison.Ordinal));
            if (targetEntity is not null && _analysis is not null) {
                var esm = _analysis.GetStructure(targetEntity);
                if (esm?.StageEnumTypeName is { } custom) return custom;
            }
            if (targetEntity is not null && targetEntity.Stages.Count > 0)
                return $"{targetName}Stage";
        }
        return $"{targetName ?? relationshipName}Stage";
    }

    /// <summary>
    /// Lowers a CompositeEffect. Every sub-effect must lower in emit mode —
    /// a sub-effect that cannot be lowered (unknown create target, unresolved
    /// relationship) is a fail-closed error, never a silent drop. The runtime
    /// seam for mixed composites is <see cref="DomainEntityInstance.ExecuteStructured"/>,
    /// not this pass.
    /// </summary>
    protected override Node? Composite(CompositeEffect c) {
        var nodes = new List<Node>();
        var variables = new List<Node>();
        foreach (var sub in c.Effects) {
            var lowered = Route(sub);
            if (lowered is null)
                throw new InvalidOperationException(
                    $"Cannot lower effect '{DescribeEffect(sub)}' to a Syntax AST node.");
            CollectNode(nodes, variables, lowered);
        }
        return new Block(nodes, variables);
    }

    protected override Node? Conditional(ConditionalEffect c) {
        var condition = _expressionPass.Lower(c.Condition, Subject);
        var thenNodes = new List<Node>();
        var thenVars = new List<Node>();
        foreach (var sub in c.ThenEffects) {
            var lowered = Route(sub);
            if (lowered is null)
                throw new InvalidOperationException(
                    $"Cannot lower effect '{DescribeEffect(sub)}' to a Syntax AST node.");
            CollectNode(thenNodes, thenVars, lowered);
        }

        if (c.ElseEffects is not { Count: > 0 })
            return new IfStatement(condition, new Block(thenNodes, thenVars));

        var elseNodes = new List<Node>();
        var elseVars = new List<Node>();
        foreach (var sub in c.ElseEffects) {
            var lowered = Route(sub);
            if (lowered is null)
                throw new InvalidOperationException(
                    $"Cannot lower effect '{DescribeEffect(sub)}' to a Syntax AST node.");
            CollectNode(elseNodes, elseVars, lowered);
        }

        return new IfStatement(condition, new Block(thenNodes, thenVars), new Block(elseNodes, elseVars));
    }

    /// <summary>Adds a lowered node to a list. Flattens Block children, keeping declarations.</summary>
    private static void CollectNode(List<Node> nodes, List<Node> variables, Node? lowered) {
        if (lowered is null) return;
        if (lowered is Block b) {
            nodes.AddRange(b.Nodes);
            variables.AddRange(b.Variables);
        }
        else
            nodes.Add(lowered);
    }

    /// <summary>
    /// Lowers CreateEntityInstance for C# mode. Emits <c>TargetType.Create(arg1, arg2, ...)</c>,
    /// matching initializer bindings to constructor parameters by property name.
    /// Uses the static <c>Create</c> factory method instead of <c>new</c> since
    /// constructors are private (Principle: owner constructs owned).
    /// When <see cref="_domain"/> is null or the target entity is not found, returns null.
    /// </summary>
    protected override Node? CreateEntityInstance(CreateEntityInstance cei) {
        if (!_lowerStageTransitions) return null;

        var targetEntity = ResolveEntity(cei.Type.TypeName);
        if (targetEntity is null) return null;

        var args = BuildConstructorArgs(cei.Initializers, targetEntity);
        var createCall = new Invoke(
            new Member(new NamedTypeReference(targetEntity.Name), "Create"),
            [.. args]);

        // The Create factory now returns DomainResult<T> with constraint validation.
        // Unwrap: var fineResult = Fine.Create(...);
        //         if (!fineResult.IsSuccess) throw ...;
        //         var fine = fineResult.Value;
        var targetName = DomainToCSharpExporter.ToCamelCase(targetEntity.Name);
        var resultVar = new Variable($"{targetName}Result");
        var targetVar = new Variable(targetName);
        var nds = new List<Node>();

        nds.Add(new Assignment(resultVar, createCall));

        nds.Add(new IfStatement(
            new Ast.Nodes.Not(new Member(resultVar, "IsSuccess")),
            new Block(new Node[] {
                new ThrowStatement(
                    new New(
                        new NamedTypeReference("InvalidOperationException"),
                        new Member(resultVar, "ErrorMessage")))
            })));

        nds.Add(new Assignment(targetVar, new Member(resultVar, "Value")));

        // Bound initializers for non-constructor props are applied as post-create
        // assignments. Defaulted props are NOT ctor params here, but their overrides
        // were already forwarded as trailing optional args by BuildConstructorArgs
        // (same as create-in) — re-assigning them would hit the private setter (CS0272).
        var parameterNames = GetConstructorParameterOrder(targetEntity)
            .Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var init in cei.Initializers) {
            if (parameterNames.Contains(init.PropertyName)) continue;
            var targetProp = targetEntity.Properties.FirstOrDefault(p =>
                string.Equals(p.Name, init.PropertyName, StringComparison.Ordinal));
            if (targetProp is null) continue;
            if (targetProp.Constraints.Any(c => c is DefaultValueConstraint)) continue;
            nds.Add(new Assignment(
                new Member(targetVar, targetProp.Name),
                LowerEnumAwareValue(init.Expression, targetProp.Type, Subject)));
        }

        return new Block(nds, [resultVar, targetVar]);
    }

    /// <summary>
    /// Lowers CreateEntityInRelationshipEffect for C# mode. Emits a call to the
    /// source entity's <c>Create{Nav}()</c> factory method, which handles
    /// construction, collection wiring, and subscription registration.
    /// E.g. <c>create in loans { book: book }</c> → <c>var loan = this.CreateLoans(book);</c>
    ///
    /// The return value is captured in a local variable so subsequent effects
    /// and the action's return value can reference the created instance.
    ///
    /// Builds the argument list to match the factory method signature produced
    /// by <see cref="DomainToCSharpExporter.AddCreateNavMethod"/>: entity
    /// properties (excluding defaults) followed by singular navs (excluding
    /// the auto-wired back-reference). Unspecified initializers default to null.
    /// </summary>
    protected override Node? CreateEntityInRelationship(CreateEntityInRelationshipEffect cr) {
        if (!_lowerStageTransitions || _domain is null) return null;

        if (_analysis is null) {
            throw new InvalidOperationException(
                "Create-in lowering requires analysis metadata. Semantic lowering without analysis is not supported.");
        }

        var pascalName = DomainToCSharpExporter.ToPascalCase(cr.RelationshipName);
        var methodName = $"Create{pascalName}";

        var resolvedTarget = _analysis.GetMetadata<ResolvedRelationshipTargetMetadata>(cr);
        var relationship = resolvedTarget?.Relationship
            ?? ResolveRelationship(cr.RelationshipName);
        if (relationship is null) return null;

        var targetEntity = resolvedTarget?.TargetEntity
            ?? ResolveEntity(relationship.Target.TypeName);
        if (targetEntity is null) return null;

        // Build initializer map keyed by property name (camelCase and PascalCase)
        var initMap = new Dictionary<string, DomainExpression>(StringComparer.Ordinal);
        foreach (var init in cr.Initializers)
            initMap[init.PropertyName] = init.Expression;

        var args = new List<Node>();
        var parameterMetadata = GetConstructorParameterOrder(targetEntity);

        // The CreateNav factory signature (DomainToCSharpExporter.AddCreateNavMethod)
        // omits back-references and collection navs: back-refs are auto-wired with
        // `this`, collections start as empty lists in the factory body. The call site
        // must emit exactly the factory's parameters or the export won't compile
        // (CS1501 arity drift). Skip back-refs, the auto-wire back-ref, and
        // collections here to stay in lockstep.
        var autoWireBackRef = DomainToCSharpExporter.FindAutoWireBackReference(targetEntity, _entity.Name);
        foreach (var parameter in parameterMetadata) {
            if (parameter.IsBackReference) continue;
            if (parameter.IsCollection) continue;
            if (autoWireBackRef is not null
                && string.Equals(parameter.Name, autoWireBackRef.Name, StringComparison.Ordinal)) continue;
            if (initMap.TryGetValue(parameter.Name, out var expr))
                args.Add(LowerEnumAwareValue(expr, parameter.Type, Subject));
            else
                args.Add(DefaultForDomainType(parameter.Type, _domain, _analysis));
        }

        // Defaulted props are TRAILING optional params on the CreateNav factory (the DSL
        // default is the C# default). When a create-in binds one, emit args for ALL
        // defaulted props in the same sorted order — bound value or the DSL default —
        // so positional order matches the signature. When none are bound, omit (C# default).
        AppendDefaultedPropArgs(args, initMap, targetEntity);

        var resultLocal = new Variable(DomainToCSharpExporter.ToCamelCase(targetEntity.Name) + "Result");
        var local = new Variable(DomainToCSharpExporter.ToCamelCase(targetEntity.Name));
        var resultType = _context.ActionResultType ?? new NamedTypeReference("DomainResult");
        var blockNodes = new List<Node> {
            new Assignment(resultLocal, new Invoke(new Member(Subject, methodName), [.. args])),
            new IfStatement(
                new Syntactic.Not(new Member(resultLocal, "IsSuccess")),
                new Block([
                    new Return(
                        new Invoke(
                            new Member(resultType, "Failure"),
                            new Syntactic.Coalesce(
                                new Member(resultLocal, "ErrorMessage"),
                                new Constant(""))))
                ])),
            new Assignment(local, new Member(resultLocal, "Value"))
        };

        return new Block(blockNodes, [resultLocal, local]);
    }

    internal List<Node> LowerCreateInConstraintProbes(IReadOnlyList<Effect> effects) {
        var nodes = new List<Node>();
        CollectCreateInProbes(effects, nodes);
        return nodes;
    }

    private void CollectCreateInProbes(IReadOnlyList<Effect> effects, List<Node> nodes) {
        foreach (var effect in effects) {
            switch (effect) {
                case CompositeEffect composite:
                    CollectCreateInProbes(composite.Effects, nodes);
                    break;
                case CreateEntityInRelationshipEffect cr:
                    if (LowerCreateInProbe(cr) is { } probe)
                        nodes.Add(probe);
                    break;
            }
        }
    }

    private Block? LowerCreateInProbe(CreateEntityInRelationshipEffect cr) {
        if (!_lowerStageTransitions || _domain is null || _analysis is null)
            return null;

        var resolvedTarget = _analysis.GetMetadata<ResolvedRelationshipTargetMetadata>(cr);
        var relationship = resolvedTarget?.Relationship
            ?? ResolveRelationship(cr.RelationshipName);
        if (relationship is null) return null;
        var targetEntity = resolvedTarget?.TargetEntity
            ?? ResolveEntity(relationship.Target.TypeName);
        if (targetEntity is null) return null;

        var initMap = new Dictionary<string, DomainExpression>(StringComparer.Ordinal);
        foreach (var init in cr.Initializers)
            initMap[init.PropertyName] = init.Expression;

        var args = new List<Node>();
        var parameterMetadata = GetConstructorParameterOrder(targetEntity);
        var autoWireBackRef = DomainToCSharpExporter.FindAutoWireBackReference(targetEntity, _entity.Name);
        foreach (var parameter in parameterMetadata) {
            if (parameter.IsCollection) continue;
            if (parameter.IsBackReference
                || (autoWireBackRef is not null
                    && string.Equals(parameter.Name, autoWireBackRef.Name, StringComparison.Ordinal))) {
                args.Add(Subject);
                continue;
            }
            if (initMap.TryGetValue(parameter.Name, out var expr))
                args.Add(LowerEnumAwareValue(expr, parameter.Type, Subject));
            else
                args.Add(DefaultForDomainType(parameter.Type, _domain, _analysis));
        }
        AppendDefaultedPropArgs(args, initMap, targetEntity);

        var probe = new Variable($"probe{_createInProbeSequence++}");
        var resultType = _context.ActionResultType ?? new NamedTypeReference("DomainResult");
        var targetType = new NamedTypeReference(targetEntity.Name);
        var errorMessage = new Syntactic.Coalesce(
            new Member(probe, "ErrorMessage"),
            new Constant(""));
        return new Block(
            [
                new Assignment(probe, new Invoke(new Member(targetType, "Create"), [.. args])),
                new IfStatement(
                    new Syntactic.Not(new Member(probe, "IsSuccess")),
                    new Block([
                        new Return(
                            new Invoke(
                                new Member(resultType, "Failure"),
                                errorMessage))
                    ]))
            ],
            [probe]);
    }

    // ── Runtime default expression helpers ─────────────────

    /// <summary>
    /// Builds a Syntax AST node for a runtime default expression, adapted to the
    /// target property's CLR type when known (discovery round5 F1–F3).
    /// <c>now</c>/<c>utcnow</c> → <c>DateTime.UtcNow</c> on a DateTime target,
    /// <c>DateOnly.FromDateTime(DateTime.UtcNow)</c> on a Date target;
    /// <c>today</c> → <c>DateTime.Today</c> / <c>DateOnly.FromDateTime(DateTime.Today)</c>;
    /// <c>guid</c> → <c>Guid.NewGuid()</c> on a Guid target,
    /// <c>Guid.NewGuid().ToString()</c> on a Text target.
    /// Returns null for literal defaults (handled directly by the exporter).
    /// </summary>
    internal static Node? LowerDefaultExpression(
        DomainExpression expr,
        Node? typeHint = null) {
        var targetName = typeHint is NamedTypeReference ntr ? ntr.TypeName : null;
        var isDateTimeTarget = targetName is "DateTime" or "Timestamp";
        if (expr is Now) {
            return isDateTimeTarget
                ? new Member(new NamedTypeReference("DateTime"), "UtcNow")
                : new Invoke(new Member(new NamedTypeReference("DateOnly"), "FromDateTime"),
                    new Member(new NamedTypeReference("DateTime"), "UtcNow"));
        }
        if (expr is Today) {
            return isDateTimeTarget
                ? new Member(new NamedTypeReference("DateTime"), "Today")
                : new Invoke(new Member(new NamedTypeReference("DateOnly"), "FromDateTime"),
                    new Member(new NamedTypeReference("DateTime"), "Today"));
        }
        if (expr is not PropertyAccess pa) return null;
        return pa.Name switch {
            "Now" or "UtcNow" => isDateTimeTarget
                ? new Member(new NamedTypeReference("DateTime"), "UtcNow")
                : new Invoke(new Member(new NamedTypeReference("DateOnly"), "FromDateTime"),
                    new Member(new NamedTypeReference("DateTime"), "UtcNow")),
            "Today" => isDateTimeTarget
                ? new Member(new NamedTypeReference("DateTime"), "Today")
                : new Invoke(new Member(new NamedTypeReference("DateOnly"), "FromDateTime"),
                    new Member(new NamedTypeReference("DateTime"), "Today")),
            "Guid" => targetName is "Text" or "String"
                ? new Invoke(new Member(
                    new Invoke(new Member(new NamedTypeReference("Guid"), "NewGuid")), "ToString"))
                : new Invoke(new Member(new NamedTypeReference("Guid"), "NewGuid")),
            _ => null, // treat as enum member name
        };
    }

    /// <summary>
    /// Builds constructor arguments matching the <c>Create</c> factory method
    /// signature produced by <see cref="DomainToCSharpExporter"/>.
    ///
    /// The factory signature orders params as:
    ///   1. Entity properties without <see cref="DefaultValueConstraint"/>
    ///      (sorted by property name — same order as the exporter).
    ///   2. Singular navigation properties (one-to-one where target entity
    ///      is the source).
    ///
    /// Back-references to the current entity (<c>_entity</c>) are auto-wired
    /// as <c>this</c>. Unspecified initializers use CLR-appropriate defaults
    /// (false for bool, 0 for numbers, null for strings/references).
    /// Properties with <see cref="DefaultValueConstraint"/> are NOT included
    /// in constructor args — the factory body sets them from the default.
    /// </summary>
    private List<Node> BuildConstructorArgs(
        IReadOnlyList<PropertyBinding> initializers, Entity targetEntity) {
        var initMap = new Dictionary<string, DomainExpression>(StringComparer.Ordinal);
        foreach (var init in initializers)
            initMap[init.PropertyName] = init.Expression;

        var args = new List<Node>();
        var parameterMetadata = GetConstructorParameterOrder(targetEntity);

        foreach (var parameter in parameterMetadata) {
            if (parameter.IsBackReference) {
                args.Add(Subject);
                continue;
            }

            if (parameter.IsCollection) {
                // Collection nav: starts empty unless an initializer binds it.
                if (initMap.TryGetValue(parameter.Name, out var collectionInit))
                    args.Add(LowerEnumAwareValue(collectionInit, parameter.Type, Subject));
                else
                    args.Add(new New(
                        new NamedTypeReference("List",
                            TypeArguments: [new NamedTypeReference(parameter.Type.TypeName)])));
                continue;
            }

            if (initMap.TryGetValue(parameter.Name, out var expr))
                args.Add(LowerEnumAwareValue(expr, parameter.Type, Subject));
            else
                args.Add(DefaultForDomainType(parameter.Type, _domain, _analysis));
        }

        // Defaulted props are TRAILING optional params (the DSL default is the C#
        // default). When any is bound, emit args for ALL defaulted props in sorted
        // order — bound value or the DSL default — so positional order matches the
        // Create/CreateNav signature. When none are bound, omit (C# default).
        AppendDefaultedPropArgs(args, initMap, targetEntity);

        return args;
    }

    /// <summary>
    /// Appends trailing optional-parameter args for defaulted props of the target
    /// entity. Shared by the standalone <c>create Type</c> and <c>create in Rel</c>
    /// call sites so a bound defaulted-prop override flows through construction
    /// instead of a (private-setter) post-create assignment.
    /// </summary>
    private void AppendDefaultedPropArgs(
        List<Node> args,
        IReadOnlyDictionary<string, DomainExpression> initMap,
        Entity targetEntity) {
        var defaultedProps = targetEntity.Properties
            .Where(p => p.Constraints.Any(c => c is DefaultValueConstraint))
            .OrderBy(p => p.Name)
            .ToList();
        if (!defaultedProps.Any(p => initMap.ContainsKey(p.Name)))
            return;

        foreach (var prop in defaultedProps) {
            if (initMap.TryGetValue(prop.Name, out var expr)) {
                args.Add(LowerEnumAwareValue(expr, prop.Type, Subject));
            }
            else {
                var defaultConstraint = prop.Constraints.OfType<DefaultValueConstraint>().First();
                var runtimeExpr = EffectLoweringPass.LowerDefaultExpression(
                    defaultConstraint.Expression, new NamedTypeReference(prop.Type.TypeName));
                var defaultNode = DomainToCSharpExporter.LowerDefaultConstantNode(defaultConstraint, prop, _domain, _analysis);
                args.Add(runtimeExpr is not null
                    ? new Constant(null) // sentinel — ctor applies the runtime default
                    : defaultNode ?? DefaultForDomainType(prop.Type, _domain, _analysis));
            }
        }
    }

    /// <summary>
    /// Lowers an initializer/assignment VALUE whose target is
    /// <paramref name="targetType"/>. A bare identifier that names a member of the
    /// target enum type resolves to qualified member access
    /// (<c>create in tokens { Kind: Numeric }</c> → <c>TokenKind.Numeric</c>),
    /// mirroring how string literals already lower to qualified enum members on
    /// assign (<c>"Suspended"</c> → <c>PatronStatus.Suspended</c>). Any other
    /// expression (parameter, subject property, literal) lowers normally.
    /// </summary>
    private Node LowerEnumAwareValue(DomainExpression expr, DomainTypeReference targetType, Node subject) {
        if (DomainToCSharpExporter.TryResolveEnumType(_domain, _analysis, targetType.TypeName, out var enumType)
            && enumType is not null) {
            // Bare identifier member: Tier: Pro
            if (expr is PropertyAccess pa && enumType.MemberNames.Contains(pa.Name, StringComparer.Ordinal)) {
                return new Member(new NamedTypeReference(enumType.Name), pa.Name);
            }
            // String-literal member: Kind: "Keyword" — same qualification as assign.
            if (expr is Literal { Value: string s }
                && enumType.MemberNames.Contains(s, StringComparer.Ordinal)) {
                return new Member(new NamedTypeReference(enumType.Name), s);
            }
        }
        return _expressionPass.Lower(expr, subject);
    }

    private IReadOnlyList<ConstructorParameterOrder> GetConstructorParameterOrder(Entity targetEntity) {
        if (_analysis is not null) {
            if (_analysis.GetStructure(targetEntity) is EntityStructureMetadata metadata)
                return metadata.ConstructorParameters;

            throw new InvalidOperationException(
                $"EntityStructureMetadata is required for constructor ordering on entity '{targetEntity.Name}'.");
        }

        // Analysis absent: structural property-order rebuild (standalone / no-analysis path only).
        var parameters = targetEntity.Properties
            .Where(p => !p.Constraints.Any(c => c is DefaultValueConstraint))
            .OrderBy(p => p.Name)
            .Select(p => new ConstructorParameterOrder(p.Name, p.Type, false, false))
            .ToList();

        if (_domain is not null) {
            foreach (var rel in targetEntity.Navigations.Where(r =>
                         r.Cardinality is not (RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany))) {
                if (string.Equals(rel.Target.TypeName, _entity.Name, StringComparison.Ordinal)) {
                    parameters.Add(new ConstructorParameterOrder(rel.Name, rel.Target, true, true));
                    continue;
                }

                parameters.Add(new ConstructorParameterOrder(rel.Name, rel.Target, true, false));
            }
        }

        return parameters;
    }

    /// <summary>
    /// Returns a type-appropriate default value Syntax node for a domain type.
    /// Used by <see cref="BuildConstructorArgs"/> and <see cref="CreateEntityInRelationship"/>
    /// to emit valid defaults instead of bare <c>null</c> for value-type properties.
    /// Enum defaults use catalog lookup when <paramref name="analysis"/> is present.
    /// </summary>
    private Node DefaultForDomainType(DomainTypeReference typeRef, Domain? domain, INodeMetadataProvider? analysis = null) {
        if (DomainToCSharpExporter.TryResolveEnumType(domain, analysis, typeRef.TypeName, out var enumType) && enumType is not null)
            return new Member(new NamedTypeReference(enumType.Name), enumType.MemberNames[0]);
        return typeRef.TypeName switch {
            "Text" or "String" => new Constant(""),
            "Number" or "Int" or "Int64" => new Constant(0L),
            "Int32" => new Constant(0),
            "Boolean" or "Bool" => new Constant(false),
            "DateTime" or "Timestamp" => new Member(
                new NamedTypeReference("DateTime"), "MinValue"),
            "Date" or "DateOnly" => new Member(
                new NamedTypeReference("DateOnly"), "MinValue"),
            "Guid" or "Uuid" => new Member(
                new NamedTypeReference("Guid"), "Empty"),
            _ => new Constant(null),
        };
    }

    private Entity? ResolveEntity(string typeName) {
        // Catalog primary when domain + analysis present.
        if (_analysis is not null) {
            var lookup = _analysis.GetTypeLookup(_domain);
            if (lookup is not null
                && lookup.Types.TryGetValue(typeName, out var domainType)
                && domainType is Entity entity)
                return entity;

            // Analysis present: fail closed (no domain tree rescan).
            return null;
        }

        if (_domain is not null) {
            return _domain.Types.OfType<Entity>().FirstOrDefault(e =>
                string.Equals(e.Name, typeName, StringComparison.Ordinal));
        }

        return null;
    }

    private Relationship? ResolveRelationship(string relationshipName) {
        if (_analysis is not null) {
            var lookup = _analysis.GetRelationshipLookup(_domain);
            if (lookup is not null
                && lookup.TryGetRelationship(_entity.Name, relationshipName, out var relationship))
                return relationship;

            return null;
        }

        if (_domain is not null) {
            return _entity.Navigations.FirstOrDefault(r =>
                string.Equals(r.Name, relationshipName, StringComparison.Ordinal));
        }

        return null;
    }

    /// <summary>
    /// Returns a human-readable description of why <paramref name="effect"/>
    /// cannot be lowered, including effect-specific detail like action names.
    /// </summary>
    private static string DescribeEffect(Effect effect) => effect switch {
        InvokeActionEffect i when i.TargetRelationship is null => $"invoke {i.ActionName}",
        InvokeActionEffect i => $"invoke {i.TargetRelationship}.{i.ActionName}",
        ForEachInvokeEffect efe => $"for {efe.RelationshipName}.{efe.ActionName}",
        StageTransitionEffect s => $"transition to {s.TargetStage.StageName} (StageTransitionEffect)",
        CreateEntityInstance cei => $"create {cei.Type.TypeName}",
        CreateEntityInRelationshipEffect cr => $"create in {cr.RelationshipName}",
        _ => $"Cannot lower: {effect.GetType().Name}"
    };
}