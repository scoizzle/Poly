using Poly.DomainModeling.Constraints;
using Poly.DomainModeling.Effects;

using AccessModifier = Poly.Introspection.AccessModifier;
using PrimType = Poly.Introspection.PrimitiveType;
using Syntactic = Poly.Syntax.Nodes;

namespace Poly.DomainModeling.Lowering;

/// <summary>
/// Produces Syntax AST <see cref="Syntactic.TypeDefinitionNode"/> trees from a
/// <see cref="Domain"/>, suitable for C# code generation via
/// <see cref="Interpretation.CSharp.CSharpGenerator"/>.
///
/// Each entity becomes a class with properties, navigation properties (as
/// <c>IReadOnlyList&lt;T&gt;</c> for collections), lifecycle stages (as enum +
/// <c>CurrentStage</c> field), actions (as void methods), and policies (as
/// bool methods). Constructor parameters are auto-generated for every property.
/// </summary>
public sealed class DomainToCSharpExporter {
    /// <summary>
    /// Builds Syntax AST type definitions for all entities and their stage enums
    /// in the given domain.
    /// </summary>
    public IReadOnlyList<Syntactic.TypeDefinitionNode> Export(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);
        var domainRelationships = domain.Relationships.ToList();
        var entities = domain.Types.OfType<Entity>().ToList();
        var entityLookup = entities.ToDictionary(e => e.Name, StringComparer.Ordinal);
        var result = new List<Syntactic.TypeDefinitionNode>();

        foreach (var entity in entities)
            result.AddRange(BuildTypeDefsForEntity(entity, domainRelationships, entityLookup));

        return result;
    }

    // ── Per-entity builder ──────────────────────────────────────

    internal IReadOnlyList<Syntactic.TypeDefinitionNode> BuildTypeDefsForEntity(
        Entity entity,
        IReadOnlyList<Relationship> domainRelationships,
        IReadOnlyDictionary<string, Entity> entityLookup) {

        var typeDefs = new List<Syntactic.TypeDefinitionNode>();
        var props = new List<Syntactic.PropertyDefinitionNode>();
        var methods = new List<Syntactic.MethodDefinitionNode>();
        var ctorParams = new List<Syntactic.Parameter>();
        var ctorAssignments = new List<Poly.Syntax.Node>();

        // ── Resolve inheritance: effective members + base type ─────
        string? baseTypeName = null;
        string stageEnumOwner;
        var ownPropertyNames = new HashSet<string>(
            entity.Properties.Select(p => p.Name), StringComparer.Ordinal);
        var ownActionNames = new HashSet<string>(
            entity.Actions.Select(a => a.Name), StringComparer.Ordinal);
        var ownPolicyNames = new HashSet<string>(
            entity.Policies.Select(p => p.Name), StringComparer.Ordinal);
        var ownStageNames = new HashSet<string>(
            entity.Stages.Select(s => s.Name), StringComparer.Ordinal);

        var effectiveProperties = new List<Property>(entity.Properties);
        var effectiveActions = new List<Poly.DomainModeling.Action>(entity.Actions);
        var effectivePolicies = new List<Policy>(entity.Policies);
        var effectiveStages = new List<Stage>(entity.Stages);

        if (entity.ParentEntityName is not null
            && entityLookup.TryGetValue(entity.ParentEntityName, out var parentEntity)) {
            baseTypeName = parentEntity.Name;

            foreach (var ancestor in WalkLineageRootToLeaf(parentEntity, entityLookup)) {
                effectiveProperties = MergeByName(effectiveProperties, ancestor.Properties, p => p.Name);
                effectiveActions = MergeByName(effectiveActions, ancestor.Actions, a => a.Name);
                effectivePolicies = MergeByName(effectivePolicies, ancestor.Policies, p => p.Name);
                effectiveStages = MergeByName(effectiveStages, ancestor.Stages, s => s.Name);
            }
        }

        // Determine stage enum owner (root ancestor that defines stages)
        stageEnumOwner = GetStageEnumOwner(entity, entityLookup);

        // ── Common property: IsDeleted (always emitted) ────────────
        props.Add(new Syntactic.PropertyDefinitionNode(
            "IsDeleted",
            new Syntactic.PrimitiveTypeReference(PrimType.Boolean),
            Getter: new Syntactic.PropertyGetterDefinitionNode(),
            Setter: new Syntactic.PropertySetterDefinitionNode(
                AccessModifier: AccessModifier.Private)
        ));

        // ── Entity properties (own only for declaration, effective for ctor) ──
        foreach (var prop in (baseTypeName is not null
            ? entity.Properties.OrderBy(p => p.Name)
            : effectiveProperties.OrderBy(p => p.Name))) {
            var propRef = MapDomainTypeRef(prop.Type);
            List<Poly.Syntax.Node>? constraints = null;
            if (prop.Constraints.Any(c => c is RequiredConstraint))
                constraints = [new Syntactic.Constant("required")];

            props.Add(new Syntactic.PropertyDefinitionNode(
                prop.Name, propRef,
                Getter: new Syntactic.PropertyGetterDefinitionNode(),
                Setter: new Syntactic.PropertySetterDefinitionNode(
                    AccessModifier: AccessModifier.Protected),
                Constraints: constraints
            ));
        }

        // ── Constructor params and assignments (ALL effective properties) ──
        foreach (var prop in effectiveProperties.OrderBy(p => p.Name)) {
            var propRef = MapDomainTypeRef(prop.Type);
            var paramName = ToCamelCase(prop.Name);
            ctorParams.Add(new Syntactic.Parameter(paramName, propRef));
            ctorAssignments.Add(new Syntactic.Assignment(
                new Syntactic.Member(new Syntactic.ThisReference(), prop.Name),
                new Syntactic.Parameter(paramName)));
        }

        // ── Navigation properties (PascalCase; IReadOnlyList for collections) ──
        foreach (var rel in domainRelationships) {
            if (!string.Equals(rel.Source.TypeName, entity.Name, StringComparison.Ordinal))
                continue;

            var isMany = rel.Cardinality is RelationshipCardinality.OneToMany
                         or RelationshipCardinality.ManyToMany;
            var targetType = new Syntactic.NamedTypeReference(rel.Target.TypeName);
            var pascalName = ToPascalCase(rel.Name);

            Poly.Syntax.Node propType;
            Poly.Syntax.Node ctorParamType;
            if (isMany) {
                propType = new Syntactic.NamedTypeReference("IReadOnlyList",
                    TypeArguments: [targetType]);
                ctorParamType = new Syntactic.CollectionTypeReference(targetType);
            }
            else {
                propType = targetType;
                ctorParamType = targetType;
            }

            var paramName = ToCamelCase(pascalName);
            ctorParams.Add(new Syntactic.Parameter(paramName, ctorParamType));
            ctorAssignments.Add(new Syntactic.Assignment(
                new Syntactic.Member(new Syntactic.ThisReference(), pascalName),
                new Syntactic.Parameter(paramName)));

            props.Add(new Syntactic.PropertyDefinitionNode(
                pascalName, propType,
                Getter: new Syntactic.PropertyGetterDefinitionNode(),
                Setter: new Syntactic.PropertySetterDefinitionNode(
                    AccessModifier: AccessModifier.Protected)
            ));
        }

        // ── Actions as void methods (own only for inheritance) ────
        var stageEnumTypeName = $"{stageEnumOwner}Stage";
        var actionsToEmit = baseTypeName is not null ? entity.Actions : effectiveActions;
        foreach (var action in actionsToEmit)
            AddActionMethod(entity, action, methods, stageEnumTypeName);
        foreach (var stage in (baseTypeName is not null ? entity.Stages : effectiveStages))
            foreach (var action in stage.Actions)
                AddActionMethod(entity, action, methods, stageEnumTypeName);

        // ── Policies as bool methods (own only for inheritance) ───
        var policiesToEmit = baseTypeName is not null ? entity.Policies : effectivePolicies;
        foreach (var policy in policiesToEmit) {
            var body = LowerExpressionToMethodBody(policy.Expression, entity);
            methods.Add(new Syntactic.MethodDefinitionNode(
                policy.Name,
                new Syntactic.PrimitiveTypeReference(PrimType.Boolean),
                Body: body,
                AccessModifier: AccessModifier.Public
            ));
        }

        // ── Stage enum + CurrentStage property ────────────────────
        if (effectiveStages.Count > 0) {
            var enumOwner = GetStageEnumOwner(entity, entityLookup);
            var enumTypeName = $"{enumOwner}Stage";

            // Only emit the stage enum if this entity owns it
            if (string.Equals(enumOwner, entity.Name, StringComparison.Ordinal)) {
                var stageEnumFields = new List<Syntactic.FieldDefinitionNode>();
                for (int si = 0; si < effectiveStages.Count; si++) {
                    stageEnumFields.Add(new Syntactic.FieldDefinitionNode(
                        effectiveStages[si].Name,
                        new Syntactic.PrimitiveTypeReference(PrimType.Int32),
                        DefaultValue: new Syntactic.Constant((long)si),
                        AccessModifier: AccessModifier.Public
                    ));
                }
                typeDefs.Add(new Syntactic.TypeDefinitionNode(
                    enumTypeName,
                    Fields: stageEnumFields,
                    Semantics: Syntactic.TypeDefinitionSemantics.MutableReference
                ));
            }

            props.Add(new Syntactic.PropertyDefinitionNode(
                "CurrentStage",
                new Syntactic.NamedTypeReference(enumTypeName),
                Getter: new Syntactic.PropertyGetterDefinitionNode(),
                Setter: new Syntactic.PropertySetterDefinitionNode(
                    AccessModifier: AccessModifier.Private)
            ));
        }

        // ── Constructor (with base() call for inheritance) ────────
        List<Syntactic.ConstructorDefinitionNode>? ctors = null;
        if (ctorParams.Count > 0 || effectiveStages.Count > 0) {
            List<Node>? baseCallArgs = null;
            List<Poly.Syntax.Node> bodyNodes;

            if (baseTypeName is not null) {
                baseCallArgs = new List<Node>();
                var ownAssignments = new List<Poly.Syntax.Node>();

                foreach (var assign in ctorAssignments) {
                    if (assign is Syntactic.Assignment a
                        && a.Destination is Syntactic.Member m
                        && ownPropertyNames.Contains(m.MemberName)) {
                        ownAssignments.Add(assign);
                    }
                    else if (assign is Syntactic.Assignment a2) {
                        baseCallArgs.Add(a2.Value ?? new Syntactic.Constant(null));
                    }
                }

                bodyNodes = new List<Poly.Syntax.Node>();
                bodyNodes.AddRange(ownAssignments);

                if (effectiveStages.Count > 0) {
                    bodyNodes.Add(new Syntactic.Assignment(
                        new Syntactic.Member(new Syntactic.ThisReference(), "CurrentStage"),
                        new Syntactic.Member(
                            new Syntactic.NamedTypeReference($"{stageEnumOwner}Stage"),
                            effectiveStages[0].Name)));
                }
            }
            else {
                bodyNodes = new List<Poly.Syntax.Node>();
                bodyNodes.AddRange(ctorAssignments);

                if (effectiveStages.Count > 0) {
                    bodyNodes.Add(new Syntactic.Assignment(
                        new Syntactic.Member(new Syntactic.ThisReference(), "CurrentStage"),
                        new Syntactic.Member(
                            new Syntactic.NamedTypeReference($"{stageEnumOwner}Stage"),
                            effectiveStages[0].Name)));
                }
            }

            ctors = [new Syntactic.ConstructorDefinitionNode(
                Parameters: ctorParams,
                Body: bodyNodes.Count > 0 ? new Syntactic.Block(bodyNodes) : null,
                BaseCall: baseCallArgs?.Count > 0 ? baseCallArgs : null,
                AccessModifier: AccessModifier.Public
            )];
        }

        typeDefs.Add(new Syntactic.TypeDefinitionNode(
            entity.Name,
            Constructors: ctors,
            Properties: props.Count > 0 ? props : null,
            Methods: methods.Count > 0 ? methods : null,
            BaseType: baseTypeName is not null
                ? new Syntactic.NamedTypeReference(baseTypeName)
                : null,
            Semantics: Syntactic.TypeDefinitionSemantics.MutableReference
        ));

        return typeDefs;
    }

    /// <summary>
    /// Determines which entity "owns" the stage enum. For inherited entities,
    /// the root ancestor's stages are canonical; the child reuses that enum.
    /// </summary>
    private static string GetStageEnumOwner(
        Entity entity, IReadOnlyDictionary<string, Entity> entityLookup) {
        var current = entity;
        while (current.ParentEntityName is not null
               && entityLookup.TryGetValue(current.ParentEntityName, out var parent)) {
            current = parent;
        }
        return current.Name;
    }

    /// <summary>
    /// Walks the entity lineage from root (topmost ancestor) to leaf (<paramref name="entity"/>).
    /// </summary>
    private static IEnumerable<Entity> WalkLineageRootToLeaf(
        Entity entity, IReadOnlyDictionary<string, Entity> lookup) {
        var chain = new List<Entity>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        Entity? current = entity;

        while (current is not null) {
            if (!visited.Add(current.Name)) break;
            chain.Add(current);
            current = current.ParentEntityName is not null
                      && lookup.TryGetValue(current.ParentEntityName, out var parent)
                ? parent : null;
        }

        chain.Reverse();
        return chain;
    }

    /// <summary>
    /// Merges <paramref name="newItems"/> into <paramref name="existing"/> by name.
    /// Existing (child) wins on conflict.
    /// </summary>
    private static List<T> MergeByName<T>(
        List<T> existing, IReadOnlyList<T> newItems,
        Func<T, string> nameSelector) {
        var merged = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in existing)
            merged[nameSelector(item)] = item;
        foreach (var item in newItems) {
            var name = nameSelector(item);
            if (!merged.ContainsKey(name))
                merged[name] = item;
        }
        return merged.Values.ToList();
    }

    // ── Action method builder ───────────────────────────────────

    private static void AddActionMethod(Entity entity, Poly.DomainModeling.Action action,
        List<Syntactic.MethodDefinitionNode> methods, string? stageEnumTypeName = null) {
        var paramNames = new HashSet<string>(
            action.Parameters.Select(p => p.Name), StringComparer.Ordinal);
        var body = LowerActionToMethodBody(entity, action, paramNames, stageEnumTypeName);
        methods.Add(new Syntactic.MethodDefinitionNode(
            action.Name,
            new Syntactic.TypeReference("void"),
            Parameters: action.Parameters
                .Select(p => new Syntactic.Parameter(p.Name, MapDomainTypeRef(p.Type)))
                .ToList(),
            Body: body,
            AccessModifier: AccessModifier.Public
        ));
    }

    // ── Lowering helpers ────────────────────────────────────────

    internal static Poly.Syntax.Node? LowerActionToMethodBody(
        Entity entity, Poly.DomainModeling.Action action,
        HashSet<string>? paramNames = null, string? stageEnumTypeName = null) {
        if (action.Effects.Count == 0) return null;
        var context = new LoweringContext(
            new Syntactic.Parameter("entity", new Syntactic.TypeReference(entity.Name)),
            UseThisReference: true,
            ActionParameterNames: paramNames,
            LowerStageTransitions: true,
            StageEnumTypeName: stageEnumTypeName
        );
        var effectPass = new EffectLoweringPass(entity, context);
        var composite = new CompositeEffect(action.Effects);
        return effectPass.TryLowerVmNode(composite);
    }

    internal static Poly.Syntax.Node? LowerExpressionToMethodBody(
        DomainExpression expr, Entity entity) {
        var context = new LoweringContext(
            new Syntactic.Parameter("entity", new Syntactic.TypeReference(entity.Name)),
            UseThisReference: true
        );
        var pass = new DomainExpressionLoweringPass(context);
        var lowered = pass.Lower(expr, new Syntactic.Parameter("entity"));
        return lowered is not null
            ? new Syntactic.Block([new Syntactic.Return(lowered)])
            : null;
    }

    // ── Type mapping ────────────────────────────────────────────

    internal static Poly.Syntax.Node MapDomainTypeRef(DomainTypeReference domainType) {
        var typeName = domainType.TypeName;
        return typeName switch {
            "Text" => new Syntactic.PrimitiveTypeReference(PrimType.String),
            "Number" or "Int" => new Syntactic.PrimitiveTypeReference(PrimType.Int64),
            "Boolean" or "Bool" => new Syntactic.PrimitiveTypeReference(PrimType.Boolean),
            "DateTime" or "Timestamp" => new Syntactic.PrimitiveTypeReference(PrimType.DateTime),
            "Date" or "DateOnly" => new Syntactic.PrimitiveTypeReference(PrimType.DateOnly),
            "Time" or "TimeOnly" => new Syntactic.PrimitiveTypeReference(PrimType.TimeOnly),
            "Duration" or "TimeSpan" => new Syntactic.PrimitiveTypeReference(PrimType.TimeSpan),
            "Uuid" or "Guid" => new Syntactic.PrimitiveTypeReference(PrimType.Guid),
            "Decimal" => new Syntactic.PrimitiveTypeReference(PrimType.Decimal),
            "Float" or "Double" => new Syntactic.PrimitiveTypeReference(PrimType.Float64),
            _ => new Syntactic.NamedTypeReference(typeName)
        };
    }

    // ── String helpers ──────────────────────────────────────────

    internal static string ToCamelCase(string name) =>
        string.IsNullOrEmpty(name) || char.IsLower(name[0])
            ? name
            : char.ToLowerInvariant(name[0]) + name.Substring(1);

    internal static string ToPascalCase(string name) =>
        string.IsNullOrEmpty(name) || char.IsUpper(name[0])
            ? name
            : char.ToUpperInvariant(name[0]) + name.Substring(1);
}