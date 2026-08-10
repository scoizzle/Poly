using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Constraints;
using Poly.DomainModeling.Effects;

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

    /// <summary>Pre-computed analysis metadata provider, when available.</summary>
    public INodeMetadataProvider? Analysis => _analysis;

    public EffectLoweringPass(Entity entity, Node subject)
        : this(entity, new LoweringContext(subject)) { }

    public EffectLoweringPass(Entity entity, LoweringContext context) {
        _entity = entity;
        _domain = context.Domain;
        _analysis = context.Analysis;
        _useThisReference = context.UseThisReference;
        _lowerStageTransitions = context.LowerStageTransitions;
        _stageEnumTypeName = context.StageEnumTypeName;
        _postTransitionNodes = context.PostTransitionNodes;
        _sourceStageName = context.SourceStageName;
        _expressionPass = new DomainExpressionLoweringPass(context with {
            NavigationNameResolver = context.NavigationNameResolver ?? BuildNavigationNameResolver(entity, _domain, _analysis)
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
            var rlm = analysis.GetMetadata<RelationshipLookupMetadata>(default);
            if (rlm is not null) {
                return name => rlm.Relationships.TryGetValue(name, out var rel)
                    && string.Equals(rel.Source.TypeName, entity.Name, StringComparison.Ordinal)
                        ? DomainToCSharpExporter.ToPascalCase(name)
                        : name;
            }
        }
        if (domain is not null) {
            var sourceNavs = domain.Relationships
                .Where(r => string.Equals(r.Source.TypeName, entity.Name, StringComparison.Ordinal))
                .Select(r => r.Name)
                .ToHashSet(StringComparer.Ordinal);
            return name => sourceNavs.Contains(name)
                ? DomainToCSharpExporter.ToPascalCase(name)
                : name;
        }
        return name => name;
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
        if (a.Target is PropertyAccess propAccess) {
            var entityProp = _entity.Properties.FirstOrDefault(p =>
                string.Equals(p.Name, propAccess.Name, StringComparison.Ordinal));
            if (entityProp is not null
                && DomainToCSharpExporter.TryResolveEnumType(_domain, _analysis, entityProp.Type.TypeName, out var enumType)
                && enumType is not null) {
                if (a.Value is Literal { Value: string strVal }
                    && !string.IsNullOrEmpty(strVal)) {
                    value = new Member(new NamedTypeReference(enumType.Name), strVal);
                }
                else if (a.Value is PropertyAccess pa
                    && enumType.MemberNames.Contains(pa.Name, StringComparer.Ordinal)) {
                    value = new Member(new NamedTypeReference(enumType.Name), pa.Name);
                }
            }

            // Date/DateTime arithmetic: DueDate + 14 → DueDate.AddDays(14)
            // The domain type names "DateTime", "Timestamp", "Date", "DateOnly"
            // map to CLR types where + long is invalid — use AddDays instead.
            if (entityProp is not null
                && value is Ast.Nodes.Add { LeftHandValue: Node lhs, RightHandValue: Node rhs }
                && IsDateTimeDomainType(entityProp.Type.TypeName)) {
                value = new Invoke(new Member(lhs, "AddDays"), [rhs]);
            }
        }

        return new Assignment(target, value);
    }

    /// <summary>Returns true when the domain type name maps to a date/time CLR type.</summary>
    private static bool IsDateTimeDomainType(string typeName) => typeName switch {
        "DateTime" or "Timestamp" or "Date" or "DateOnly" => true,
        _ => false,
    };

    /// <summary>
    /// Lowers a stage transition. When <see cref="_lowerStageTransitions"/> is true,
    /// emits the source stage's exit effects (if known), then the target stage's entry
    /// effects, then a CurrentStage assignment, then post-transition notification nodes.
    /// Otherwise returns null so the runtime calls <see cref="DomainEntityInstance.TransitionStage"/>.
    /// </summary>
    protected override Node? StageTransition(StageTransitionEffect t) {
        if (!_lowerStageTransitions) return null;

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

        // Include entry effects from the target stage (same analysis contract as exit).
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
                    nodes.Add(lowered);
            }
        }

        var stageEnumType = new NamedTypeReference(
            _stageEnumTypeName ?? $"{_entity.Name}Stage");
        nodes.Add(new Assignment(
            new Member(Subject, "CurrentStage"),
            new Member(stageEnumType, t.TargetStage.StageName)
        ));

        // Append post-transition notification nodes (subscription fan-out)
        if (_postTransitionNodes is not null
            && _postTransitionNodes.TryGetValue(t.TargetStage.StageName, out var postNodes)) {
            foreach (var postNode in postNodes)
                nodes.Add(postNode);
        }

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
        var resultVar = $"{targetName}Result";
        var nds = new List<Node>();

        // var fineResult = Fine.Create(...);
        nds.Add(new Variable(resultVar, createCall));

        // if (!fineResult.IsSuccess) throw new InvalidOperationException(fineResult.ErrorMessage);
        nds.Add(new IfStatement(
            new Ast.Nodes.Not(new Member(new Variable(resultVar), "IsSuccess")),
            new Block(new Node[] {
                new ThrowStatement(
                    new New(
                        new NamedTypeReference("InvalidOperationException"),
                        new Member(new Variable(resultVar), "ErrorMessage")))
            })));

        // var fine = fineResult.Value;
        nds.Add(new Variable(targetName,
            new Member(new Variable(resultVar), "Value")));

        // Defaulted-property overrides (props with DefaultValueConstraint are not
        // ctor params — the factory body sets the default). Apply bound values as
        // post-create assignments so `create Fine { Severity: Hint }` overrides the
        // default instead of being silently dropped (matches the runtime values bag).
        var parameterNames = GetConstructorParameterOrder(targetEntity)
            .Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var init in cei.Initializers) {
            if (parameterNames.Contains(init.PropertyName)) continue;
            var targetProp = targetEntity.Properties.FirstOrDefault(p =>
                string.Equals(p.Name, init.PropertyName, StringComparison.Ordinal));
            if (targetProp is null) continue;
            nds.Add(new Assignment(
                new Member(new Variable(targetName), targetProp.Name),
                LowerEnumAwareValue(init.Expression, targetProp.Type, Subject)));
        }

        return new Block(nds);
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

        foreach (var parameter in parameterMetadata) {
            if (parameter.IsBackReference) continue;
            if (initMap.TryGetValue(parameter.Name, out var expr))
                args.Add(LowerEnumAwareValue(expr, parameter.Type, Subject));
            else
                args.Add(DefaultForDomainType(parameter.Type, _domain, _analysis));
        }

        // Defaulted-property overrides: props with a DefaultValueConstraint are NOT
        // ctor params (the factory body sets the default), so a binding like
        // `create in diagnostics { Severity: Hint }` would be silently dropped.
        // The runtime honors it (values bag); the export must too — apply as a
        // post-create assignment (props get `internal set` so same-assembly code
        // can override after construction).
        var localName = DomainToCSharpExporter.ToCamelCase(targetEntity.Name);
        var blockNodes = new List<Node> {
            new Variable(localName,
                new Invoke(new Member(Subject, methodName), [.. args]))
        };
        var parameterNames = parameterMetadata.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var init in cr.Initializers) {
            if (parameterNames.Contains(init.PropertyName)) continue; // already a ctor arg
            var targetProp = targetEntity.Properties.FirstOrDefault(p =>
                string.Equals(p.Name, init.PropertyName, StringComparison.Ordinal));
            if (targetProp is null) continue; // unknown/collection — analyzer already reported
            blockNodes.Add(new Assignment(
                new Member(new Variable(localName), targetProp.Name),
                LowerEnumAwareValue(init.Expression, targetProp.Type, Subject)));
        }

        return new Block(blockNodes);
    }

    /// <summary>
    /// Lowers DeleteEntityInstance for C# mode. Emits <c>this.IsDeleted = true;</c>.
    /// </summary>
    protected override Node? DeleteEntity(DeleteEntityInstance _) {
        if (!_lowerStageTransitions) return null;
        return new Assignment(new Member(Subject, "IsDeleted"), new Constant(true));
    }

    // ── Runtime default expression helpers ───────────────────────

    /// <summary>
    /// Builds a Syntax AST node for a runtime default expression.
    /// Returns <c>DateTime.UtcNow</c>, <c>Guid.NewGuid()</c>, etc.
    /// based on the expression type in the <see cref="DefaultValueConstraint"/>.
    /// Returns null for literal defaults (handled directly by the exporter).
    /// </summary>
    internal static Node? LowerDefaultExpression(DomainExpression expr, Node? typeHint = null) {
        if (expr is PropertyAccess pa) {
            return pa.Name switch {
                "now" or "utcnow" => new Member(
                    new NamedTypeReference("DateTime"), "UtcNow"),
                "today" => new Invoke(
                    new Member(new NamedTypeReference("DateOnly"), "FromDateTime"),
                    new Member(new NamedTypeReference("DateTime"), "UtcNow")),
                "guid" => new Invoke(
                    new Member(new NamedTypeReference("Guid"), "NewGuid")),
                _ => null, // treat as enum member name
            };
        }
        return null;
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

        return args;
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
            && enumType is not null
            && expr is PropertyAccess pa
            && enumType.MemberNames.Contains(pa.Name, StringComparer.Ordinal)) {
            return new Member(new NamedTypeReference(enumType.Name), pa.Name);
        }
        return _expressionPass.Lower(expr, subject);
    }

    private IReadOnlyList<ConstructorParameterOrder> GetConstructorParameterOrder(Entity targetEntity) {
        if (_analysis is not null) {
            if (_analysis.GetMetadata<EntityStructureMetadata>(targetEntity) is EntityStructureMetadata metadata)
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
            foreach (var rel in _domain.Relationships.Where(r =>
                         string.Equals(r.Source.TypeName, targetEntity.Name, StringComparison.Ordinal)
                         && r.Cardinality is not (RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany))) {
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
                && lookup.Relationships.TryGetValue(relationshipName, out var relationship))
                return relationship;

            return null;
        }

        if (_domain is not null) {
            return _domain.Relationships.FirstOrDefault(r =>
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