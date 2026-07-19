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
        var result = new List<Syntactic.TypeDefinitionNode>();

        foreach (var entity in entities)
            result.AddRange(BuildTypeDefsForEntity(entity, domainRelationships));

        return result;
    }

    // ── Per-entity builder ──────────────────────────────────────

    internal IReadOnlyList<Syntactic.TypeDefinitionNode> BuildTypeDefsForEntity(
        Entity entity, IReadOnlyList<Relationship> domainRelationships) {

        var typeDefs = new List<Syntactic.TypeDefinitionNode>();
        var props = new List<Syntactic.PropertyDefinitionNode>();
        var methods = new List<Syntactic.MethodDefinitionNode>();
        var fields = new List<Syntactic.FieldDefinitionNode>();
        var ctorParams = new List<Syntactic.Parameter>();
        var ctorAssignments = new List<Poly.Syntax.Node>();

        // ── Entity properties (sorted for deterministic output) ──
        foreach (var prop in entity.Properties.OrderBy(p => p.Name)) {
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

        // ── Actions as void methods (entity + stage-level) ─────
        foreach (var action in entity.Actions)
            AddActionMethod(entity, action, methods);
        foreach (var stage in entity.Stages)
            foreach (var action in stage.Actions)
                AddActionMethod(entity, action, methods);

        // ── Policies as bool methods ───────────────────────────
        foreach (var policy in entity.Policies) {
            var body = LowerExpressionToMethodBody(policy.Expression, entity);
            methods.Add(new Syntactic.MethodDefinitionNode(
                policy.Name,
                new Syntactic.PrimitiveTypeReference(PrimType.Boolean),
                Body: body,
                AccessModifier: AccessModifier.Public
            ));
        }

        // ── Stage enum + CurrentStage field ────────────────────
        if (entity.Stages.Count > 0) {
            var stageEnumFields = new List<Syntactic.FieldDefinitionNode>();
            for (int si = 0; si < entity.Stages.Count; si++) {
                stageEnumFields.Add(new Syntactic.FieldDefinitionNode(
                    entity.Stages[si].Name,
                    new Syntactic.PrimitiveTypeReference(PrimType.Int32),
                    DefaultValue: new Syntactic.Constant((long)si),
                    AccessModifier: AccessModifier.Public
                ));
            }
            var enumTypeName = $"{entity.Name}Stage";

            typeDefs.Add(new Syntactic.TypeDefinitionNode(
                enumTypeName,
                Fields: stageEnumFields,
                Semantics: Syntactic.TypeDefinitionSemantics.MutableReference
            ));

            fields.Add(new Syntactic.FieldDefinitionNode(
                "CurrentStage",
                new Syntactic.NamedTypeReference(enumTypeName),
                AccessModifier: AccessModifier.Public
            ));
        }

        // ── Constructor ────────────────────────────────────────
        List<Syntactic.ConstructorDefinitionNode>? ctors = null;
        if (ctorParams.Count > 0 || entity.Stages.Count > 0) {
            var bodyNodes = new List<Poly.Syntax.Node>();
            foreach (var assign in ctorAssignments)
                bodyNodes.Add(assign);

            if (entity.Stages.Count > 0) {
                bodyNodes.Add(new Syntactic.Assignment(
                    new Syntactic.Member(new Syntactic.ThisReference(), "CurrentStage"),
                    new Syntactic.Member(
                        new Syntactic.NamedTypeReference($"{entity.Name}Stage"),
                        entity.Stages[0].Name)));
            }

            ctors = [new Syntactic.ConstructorDefinitionNode(
                Parameters: ctorParams,
                Body: new Syntactic.Block(bodyNodes),
                AccessModifier: AccessModifier.Public
            )];
        }

        typeDefs.Add(new Syntactic.TypeDefinitionNode(
            entity.Name,
            Constructors: ctors,
            Properties: props.Count > 0 ? props : null,
            Methods: methods.Count > 0 ? methods : null,
            Fields: fields.Count > 0 ? fields : null,
            Semantics: Syntactic.TypeDefinitionSemantics.MutableReference
        ));

        return typeDefs;
    }

    // ── Action method builder ───────────────────────────────────

    private static void AddActionMethod(Entity entity, Poly.DomainModeling.Action action,
        List<Syntactic.MethodDefinitionNode> methods) {
        var paramNames = new HashSet<string>(
            action.Parameters.Select(p => p.Name), StringComparer.Ordinal);
        var body = LowerActionToMethodBody(entity, action, paramNames);
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
        HashSet<string>? paramNames = null) {
        if (action.Effects.Count == 0) return null;
        var context = new LoweringContext(
            new Syntactic.Parameter("entity", new Syntactic.TypeReference(entity.Name)),
            UseThisReference: true,
            ActionParameterNames: paramNames,
            LowerStageTransitions: true
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