using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Lowering;

namespace Poly.Packs.Product;

// MinimalApiGenerator (Dtos) — entity and action DTO type builders and their validation attributes.
public sealed partial class MinimalApiGenerator {
    /// <summary>Plain action DTO names that must be entity-qualified: an action name
    /// with parameters emitted more than once (stage-scoped actions are listed once
    /// per declaring stage — CRM <c>Log</c>/<c>AddLine</c> on Opportunity's
    /// Qualify/Propose/Commit stages — or the same name on several entities) or
    /// colliding with an emitted <c>{Entity}Dto</c>. The route itself is registered
    /// once per action name. Lazy: the entity-DTO side needs constructor metadata,
    /// which missing-metadata callers fail on at Generate time.</summary>
    private readonly Lazy<HashSet<string>> _actionDtoCollisions;

    private HashSet<string> ComputeActionDtoCollisions() {
        var reserved = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entity in _entities) {
            if (!GetStorageEntity(entity).IsRoot || !CreatesDirectly(_entities, entity)) continue;
            if (GetRequiredScalarSlots(entity).Count == 0) continue;
            reserved.Add($"{entity.Name}Dto");
        }

        var actionDtoCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entity in _entities) {
            foreach (var ia in GetBehaviorActions(entity)) {
                if (ia.Parameters.Count == 0) continue;
                var name = $"{Pascalize(ia.Name)}Dto";
                actionDtoCounts[name] = actionDtoCounts.GetValueOrDefault(name) + 1;
            }
        }

        var collisions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, count) in actionDtoCounts) {
            if (count > 1 || reserved.Contains(name))
                collisions.Add(name);
        }
        return collisions;
    }

    /// <summary>DTO type names already emitted into the current Program.cs unit — keeps
    /// stage-scoped duplicate actions from emitting the same type twice (CS0101).</summary>
    private readonly HashSet<string> _emittedDtoNames = new(StringComparer.Ordinal);

    private static bool IsNumericClrType(string clrType) => clrType switch {
        "long" or "int" or "short" or "byte" or "double" or "float" or "decimal" => true,
        _ => false,
    };

    /// <summary>True when the CLR type is a non-nullable value type (a primitive scalar or
    /// enum) — the only CLR kinds that do not need a <c>default!</c> initializer on an
    /// init-only DTO property. Value types (record classes) and <c>byte[]</c> are reference
    /// types and must be initialized to silence CS8618.</summary>
    private bool IsPrimitiveValueTypeClr(string domainType, string clrType) =>
        clrType switch {
            "long" or "int" or "short" or "byte" or "double" or "float" or "decimal" or "bool"
                or "DateTime" or "DateOnly" or "TimeOnly" or "TimeSpan" or "Guid" => true,
            _ => _enumLookup.ContainsKey(domainType),
        };

    /// <summary>Builds Syntax IR for action DTO types.</summary>
    private void BuildActionDtoTypes(List<TypeDefinitionNode> dtoTypes, Entity entity) {
        foreach (var ia in GetBehaviorActions(entity)) {
            if (ia.Parameters.Count == 0) continue;
            var dtoName = GetActionDtoName(entity, ia.Name);
            // Stage-scoped actions are listed once per declaring stage; the same
            // (entity, action) pair emits one DTO that every stage's endpoint binds.
            if (!_emittedDtoNames.Add(dtoName)) continue;
            var domainAction = entity.Actions.FirstOrDefault(a =>
                string.Equals(a.Name, ia.Name, StringComparison.Ordinal));
            var props = new List<PropertyDefinitionNode>();
            foreach (var param in ia.Parameters) {
                if (param.IsEntityRef) {
                    props.Add(new PropertyDefinitionNode(
                        ActionDtoPropertyName(param),
                        ClrTypeNode("string"),
                        Getter: new PropertyGetterDefinitionNode(),
                        Initializer: new PropertyInitializerDefinitionNode(new NullForgiving(new Default()))));
                    continue;
                }
                var clrType = GetClrTypeName(param.DomainType);
                var prop = new PropertyDefinitionNode(
                    ActionDtoPropertyName(param),
                    ClrTypeNode(clrType),
                    Getter: new PropertyGetterDefinitionNode(),
                    Initializer: IsPrimitiveValueTypeClr(param.DomainType, clrType)
                        ? new PropertyInitializerDefinitionNode()
                        : new PropertyInitializerDefinitionNode(new NullForgiving(new Default())));
                if (domainAction is not null) {
                    // The implicit envelope is the analysis ActionParameterEnvelopeMetadata fact: what the
                    // action's own unconditional effects prove about the parameter.
                    var attrs = BuildConstraintAttributes(
                        clrType, _analysis.GetActionParameterEnvelope(domainAction, param.Name) ?? []);
                    if (IsEnumTypeName(param.DomainType))
                        attrs = [EnumDataTypeAttribute(param.DomainType), .. attrs];
                    if (attrs.Count > 0) prop = prop with { Attributes = attrs };
                }
                props.Add(prop);
            }
            dtoTypes.Add(new TypeDefinitionNode(
                GetActionDtoName(entity, ia.Name),
                Properties: props,
                Semantics: new TypeDefinitionSemantics(
                    TypeDefinitionMutability.Immutable,
                    TypeDefinitionEqualitySemantics.Value
                ),
                AccessModifier: DomainAccess.ForInfrastructure(
                    _domain, [entity.Name, .. ia.Parameters.Select(p => p.DomainType)])
            ));
        }
    }

    /// <summary>
    /// Resolves the effective range for a property: the analysis-verified envelope when
    /// the invariant analysis proved no effect can produce an out-of-range value, else
    /// the declared constraint.
    /// </summary>
    private RangeConstraint? GetPropertyRange(Entity entity, string propertyName) {
        var column = GetStorageEntity(entity).Columns.FirstOrDefault(c =>
            string.Equals(c.Name, propertyName, StringComparison.Ordinal));
        if (column?.VerifiedRange is { } verified && column.IsRangeVerified)
            return new RangeConstraint(verified.Min, verified.Max);
        return entity.Properties.FirstOrDefault(p =>
            string.Equals(p.Name, propertyName, StringComparison.Ordinal))
            ?.Constraints.OfType<RangeConstraint>().FirstOrDefault();
    }

    /// <summary>Maps effective constraints to DataAnnotations validation attributes, when
    /// the CLR type can express them.</summary>
    private static IReadOnlyList<AttributeNode> BuildConstraintAttributes(string clrType, IEnumerable<Constraint> constraints) {
        var attrs = new List<AttributeNode>();
        foreach (var constraint in constraints) {
            switch (constraint) {
                case RangeConstraint r when IsNumericClrType(clrType):
                    // Open bounds (range(0, ) / range(, 100)) cap at the CLR type's
                    // representable range — the DTO member can never exceed it — instead
                    // of emitting a raw double.MaxValue literal. The integral-vs-double
                    // constructor choice lives in RangeAttributeArguments.
                    var rangeArgs = RangeAttributeArguments.Build(clrType, r.Minimum, r.Maximum);
                    attrs.Add(new AttributeNode("Range", rangeArgs));
                    break;
                case LengthConstraint l when clrType == "string":
                    attrs.Add(new AttributeNode("MinLength", [new Constant(l.MinLength)]));
                    attrs.Add(new AttributeNode("MaxLength", [new Constant(l.MaxLength)]));
                    break;
                case PatternConstraint p when clrType == "string":
                    attrs.Add(new AttributeNode("RegularExpression", [new Constant(p.Pattern)]));
                    break;
                case RequiredConstraint when clrType == "string":
                    attrs.Add(new AttributeNode("Required", []));
                    break;
                // Value-set union: equals(v) → [AllowedValues(...)] — the member must equal
                // the pinned value. (Enum unions are enforced by the CLR enum type; see the
                // enum [EnumDataType] propagation.)
                case EqualityConstraint eq:
                    attrs.Add(new AttributeNode("AllowedValues", [new Constant(eq.ExpectedValue)]));
                    break;
            }
        }
        return attrs;
    }

    /// <summary>The <c>[EnumDataType(typeof(EnumName))]</c> attribute declaring an enum-typed
    /// member's allowed-value union on the transport contract.</summary>
    private AttributeNode EnumDataTypeAttribute(string enumName) =>
        new("EnumDataType", [new TypeOf(new TypeReference(VisibleType(enumName)))]);

    private bool IsEnumTypeName(string domainTypeName) =>
        _enumLookup.ContainsKey(domainTypeName);

    /// <summary>Builds Syntax IR for a DTO type definition.</summary>
    private void BuildDtoTypes(List<TypeDefinitionNode> dtoTypes, Entity entity) {
        // The DTO carries the entity's Required scalar create slots (the POST endpoint
        // passes them into Entity.Create(...); every other slot is null). The SAME
        // published slots the endpoint reads, not a re-derived property set.
        var scalarParams = GetRequiredScalarSlots(entity);

        if (scalarParams.Count == 0) return;
        if (!GetStorageEntity(entity).IsRoot || !CreatesDirectly(_entities, entity)) return;

        // Emit as a record with get/init properties so transport validation attributes
        // attach to properties and are enforced by ASP.NET model binding, and the
        // contract stays immutable. An init-only accessor is a property with no setter
        // but an initializer present; non-nullable reference scalars get `= default!;`
        // for CS8618 hygiene. Declared constraints map to validation attributes: range →
        // [Range] (using the analysis-VERIFIED envelope when proven, else declared),
        // length → [MinLength]/[MaxLength], pattern → [RegularExpression], required → [Required].
        var props = scalarParams.Select(param => {
            var clrType = GetClrTypeName(param.Type.TypeName);
            var prop = new PropertyDefinitionNode(
                param.Name,
                ClrTypeNode(clrType),
                Getter: new PropertyGetterDefinitionNode(),
                Initializer: IsPrimitiveValueTypeClr(param.Type.TypeName, clrType)
                    ? new PropertyInitializerDefinitionNode()
                    : new PropertyInitializerDefinitionNode(new NullForgiving(new Default())));

            var effective = new List<Constraint>();
            var range = GetPropertyRange(entity, param.Name);
            if (range is not null) effective.Add(range);
            var declared = entity.Properties.FirstOrDefault(p =>
                string.Equals(p.Name, param.Name, StringComparison.Ordinal))?.Constraints ?? [];
            effective.AddRange(declared.Where(c =>
                c is LengthConstraint or PatternConstraint or RequiredConstraint
                    or EqualityConstraint));

            var attrs = BuildConstraintAttributes(clrType, effective);
            // Every member of the create DTO is a required ctor slot (optional
            // properties are absent from the DTO), so every string member is [Required].
            if (clrType == "string" && attrs.All(a => a.Name != "Required"))
                attrs = [new AttributeNode("Required", []), .. attrs];
            // Enum-typed members: the CLR enum type enforces membership at binding; the
            // [EnumDataType] attribute additionally declares the allowed-value union on the
            // contract and fails loud at validation if binding ever bypasses the type.
            if (IsEnumTypeName(param.Type.TypeName))
                attrs = [EnumDataTypeAttribute(param.Type.TypeName), .. attrs];
            return attrs.Count == 0 ? prop : prop with { Attributes = attrs };
        }).ToList();

        dtoTypes.Add(new TypeDefinitionNode(
            $"{entity.Name}Dto",
            Properties: props,
            Semantics: new TypeDefinitionSemantics(
                TypeDefinitionMutability.Mutable,
                TypeDefinitionEqualitySemantics.Value
            ),
            AccessModifier: DomainAccess.ForInfrastructure(
                _domain, [entity.Name, .. scalarParams.Select(p => p.Type.TypeName)])
        ));
    }
}