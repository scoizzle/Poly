using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;

using PrimType = Poly.Introspection.PrimitiveType;
using Syntactic = Poly.Ast.Nodes;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.DomainModeling.Lowering;

// Properties: property/field emission, value-type shapes, and property default lowering.
public sealed partial class DomainToCSharpExporter {
    internal static TypeDefinitionNode BuildValueTypeTypeDef(
        ValueType valueType, Domain domain, INodeMetadataProvider metadata) {
        var props = new List<PropertyDefinitionNode>();
        foreach (var prop in valueType.Properties) {
            var propRef = MapDomainTypeRef(prop.Type, domain, metadata);
            props.Add(new PropertyDefinitionNode(
                prop.Name, propRef,
                Getter: new PropertyGetterDefinitionNode(),
                Setter: new PropertySetterDefinitionNode(),
                Initializer: IsNonNullableReferenceScalar(propRef)
                    ? new Syntactic.PropertyInitializerDefinitionNode(
                        new Syntactic.NullForgiving(new Syntactic.Default()))
                    : null
            ));
        }
        return new TypeDefinitionNode(
            valueType.Name,
            Properties: props,
            Semantics: Syntactic.TypeDefinitionSemantics.ImmutableValue
        );
    }

    /// <summary>
    /// True when the mapped CLR type is a non-nullable reference type (currently the
    /// only one is <c>Text → string</c>), which the parameterless EF-materialization
    /// ctor leaves unset and would otherwise raise CS8618.
    /// </summary>
    private static bool IsNonNullableReferenceScalar(Node propRef) =>
        propRef is PrimitiveTypeReference { PrimitiveId: PrimType.String };

    private static bool IsDomainValueType(Domain domain, string typeName) =>
        domain.Types.OfType<ValueType>().Any(v =>
            string.Equals(v.Name, typeName, StringComparison.Ordinal));

    /// <summary>
    /// Lowers a <see cref="DefaultValueConstraint"/> to its C# constant/enum node,
    /// used as an optional-parameter default on the Create/CreateNav/ctor signatures.
    /// Returns null for runtime defaults (now/today/guid) — those use a
    /// <c>T? = null</c> sentinel and a <c>?? &lt;runtime&gt;</c> coalesce in the ctor
    /// body. Shared with the create-in call site so signature and call stay in lockstep.
    /// </summary>
    internal static Node? LowerDefaultConstantNode(
        DefaultValueConstraint defaultValue,
        Property prop,
        Domain domain,
        INodeMetadataProvider metadata) {
        if (defaultValue.Expression is Literal lit) {
            // A Decimal property's literal default becomes a Decimal constant, so the
            // VM's range guard compares Decimal to Decimal (#511). The arm is local
            // because this function's inputs are (domain, metadata) only.
            if (TargetTypeAdapter.IsDecimal(domain, prop.Type.TypeName)) {
                switch (lit.Value) {
                    case long integral:
                        return new Constant((decimal)integral);
                    case int narrow:
                        return new Constant((decimal)narrow);
                }
            }
            return new Constant(lit.Value);
        }
        if (defaultValue.Expression is PropertyAccess pa) {
            if (TryResolveEnumType(domain, metadata, prop.Type.TypeName, out var enumType)
                && enumType is not null
                && enumType.MemberNames.Contains(pa.Name, StringComparer.Ordinal)) {
                return new Member(new NamedTypeReference(enumType.Name), pa.Name);
            }
            // Non-keyword, non-enum-member PropertyAccess default cannot be lowered —
            // fail loud instead of silently dropping the constraint (which would turn
            // the property into a required Create parameter).
            throw new NotSupportedException(
                $"default({pa.Name}) on property '{prop.Name}' (type '{prop.Type.TypeName}') cannot be lowered: " +
                $"'{pa.Name}' is not a member of an enum that '{prop.Name}' is typed with.");
        }
        return null;
    }

    /// <summary>
    /// Emits the scalar property definitions (sorted for deterministic output) and
    /// collects the constructor parameters and assignments they imply.
    /// </summary>
    private static void AddEntityProperties(
        Entity entity, Domain domain, INodeMetadataProvider metadata,
        EntityStructureMetadata esm, IReadOnlySet<string> entryAssignedProps,
        List<PropertyDefinitionNode> props,
        List<Parameter> ctorParams, List<Node> ctorAssignments,
        List<Parameter> defaultedCtorParams) {
        // ── Entity properties (sorted for deterministic output) ──
        foreach (var prop in entity.Properties.OrderBy(p => p.Name)) {
            var propRef = MapDomainTypeRef(prop.Type, domain, metadata);
            // Requiredness is the published fact: required is the default, `optional`
            // is its opt-out. No constraint re-scan decides nullability.
            var isRequired = esm.RequiredPropertyNames.Contains(prop.Name);
            var propertyType = isRequired ? propRef : new OptionalTypeReference(propRef);
            var isDomainValue = IsDomainValueType(domain, prop.Type.TypeName);
            List<Node>? constraints = null;
            if (isRequired)
                constraints = [new Constant("required")];

            // CS8618: the parameterless materialization ctor does not assign
            // reference properties. Text uses `default!`. A required domain value record
            // is default-constructed so a missing initializer is still non-null.
            Syntactic.PropertyInitializerDefinitionNode? initializer = null;
            if (IsNonNullableReferenceScalar(propertyType))
                initializer = new Syntactic.PropertyInitializerDefinitionNode(
                    new Syntactic.NullForgiving(new Syntactic.Default()));
            else if (isDomainValue && isRequired)
                initializer = new Syntactic.PropertyInitializerDefinitionNode(new New(propRef));

            props.Add(new PropertyDefinitionNode(
                prop.Name, propertyType,
                Getter: new PropertyGetterDefinitionNode(),
                // private set — domain state changes only through the Create factory /
                // action effects (no same-assembly backdoor mutability).
                Setter: new PropertySetterDefinitionNode(
                    AccessModifier: AccessModifier.Private),
                Initializer: initializer,
                Constraints: constraints
            ));

            // Defaulted props become OPTIONAL ctor params (the DSL default is the C#
            // default) so `create in { DefaultedProp: value }` overrides flow through
            // construction — no setters, no post-create assignment.
            var defaultValue = prop.Constraints.OfType<DefaultValueConstraint>().FirstOrDefault();
            var paramName = ToCamelCase(prop.Name);
            // Gym dogfood: Visits default(0) AND Active entry { assign Visits to 0 }
            // made a dead ctor param (assign param, then overwrite with entry).
            if (entryAssignedProps.Contains(prop.Name))
                continue;
            if (defaultValue is not null) {
                var runtimeExpr = EffectLoweringPass.LowerDefaultExpression(
                    defaultValue.Expression,
                    new NamedTypeReference(prop.Type.TypeName),
                    RuntimeAnalysisCache.MeaningFor(domain),
                    RuntimeAnalysisCache.FormsFor(domain));
                if (runtimeExpr is not null) {
                    // Runtime default (now/today/guid) can't be a compile-time default —
                    // T? = null sentinel; body applies the runtime default when null.
                    defaultedCtorParams.Add(new Parameter(paramName,
                        new OptionalTypeReference(propRef),
                        DefaultValue: new Constant(null)));
                    ctorAssignments.Add(new Assignment(
                        new Member(new ThisReference(), prop.Name),
                        new Syntactic.Coalesce(new Parameter(paramName), runtimeExpr)));
                }
                else {
                    var defaultNode = LowerDefaultConstantNode(defaultValue, prop, domain, metadata);
                    defaultedCtorParams.Add(new Parameter(paramName, propertyType, DefaultValue: defaultNode));
                    ctorAssignments.Add(new Assignment(
                        new Member(new ThisReference(), prop.Name),
                        new Parameter(paramName)));
                }
                continue; // skip ctor param — handled as a trailing optional param
            }

            // Optional with no default: a trailing `T? = null` parameter assigned
            // directly, so an omitted value stores null (not ""/0).
            if (!isRequired) {
                defaultedCtorParams.Add(new Parameter(paramName,
                    new OptionalTypeReference(propRef),
                    DefaultValue: new Constant(null)));
                ctorAssignments.Add(new Assignment(
                    new Member(new ThisReference(), prop.Name),
                    new Parameter(paramName)));
                continue;
            }

            // Required with no default — full constructor param + assignment.
            ctorParams.Add(new Parameter(paramName, propRef));
            ctorAssignments.Add(new Assignment(
                new Member(new ThisReference(), prop.Name),
                new Parameter(paramName)));
        }
    }

    /// <summary>
    /// Scalar constructor slots in the published ESM order: required scalars first,
    /// then optional assign-only scalars. Defaulted properties are not in this list;
    /// they stay in the trailing defaulted params.
    /// </summary>
    private static void OrderScalarCtorSlots(
        EntityStructureMetadata esm, List<Parameter> ctorParams, List<Node> ctorAssignments) {
        // ── Scalar constructor slots in the published ESM order ──
        // Required scalars first, then optional assign-only scalars. Defaulted
        // properties are not in this list; they stay in defaultedCtorParams.
        var scalarOrder = esm.ConstructorParameters.Where(p => !p.IsNavigation).ToList();
        if (scalarOrder.Count > 0 && ctorParams.Count > 0) {
            var paramsByName = ctorParams.ToDictionary(p => p.Name, StringComparer.Ordinal);
            var scalarProps = scalarOrder.Select(o => o.Name).ToHashSet(StringComparer.Ordinal);
            var assignmentsByProp = ctorAssignments
                .OfType<Assignment>()
                .Where(a => a.Destination is Member { MemberName: var name }
                    && scalarProps.Contains(name))
                .GroupBy(a => ((Member)a.Destination).MemberName, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var retainedAssignments = ctorAssignments
                .Where(a => a is not Assignment {
                    Destination: Member { MemberName: var name }
                } || !scalarProps.Contains(name))
                .ToList();
            var reordered = new List<Parameter>();
            var assignments = new List<Node>();
            foreach (var order in scalarOrder) {
                var name = ToCamelCase(order.Name);
                if (!paramsByName.TryGetValue(name, out var parameter))
                    continue;
                if (order.IsOptional)
                    parameter = parameter with { DefaultValue = EmptyDefault(order.Type) };
                reordered.Add(parameter);
                assignments.Add(assignmentsByProp.TryGetValue(order.Name, out var assignment)
                    ? assignment
                    : new Assignment(
                        new Member(new ThisReference(), order.Name),
                        new Parameter(name)));
            }
            ctorParams.Clear();
            ctorParams.AddRange(reordered);
            ctorAssignments.Clear();
            ctorAssignments.AddRange(assignments);
            ctorAssignments.AddRange(retainedAssignments);
        }
    }
}