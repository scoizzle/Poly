using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Contract;
using Poly.DomainModeling.Runtime;

using AccessModifier = Poly.Introspection.AccessModifier;
using PrimType = Poly.Introspection.PrimitiveType;
using Syntactic = Poly.Ast.Nodes;

namespace Poly.DomainModeling.Lowering;

/// <summary>
/// Produces Syntax AST <see cref="TypeDefinitionNode"/> trees from a
/// <see cref="Domain"/>, suitable for C# code generation via
/// <see cref="Interpretation.CSharp.CSharpWriter"/>.
///
/// Each entity becomes a class with properties, navigation properties (as
/// <c>IReadOnlyList&lt;T&gt;</c> for collections), lifecycle stages (as enum +
/// <c>CurrentStage</c> field), actions (as void methods), and policies (as
/// bool methods). Constructor parameters are auto-generated for every property.
///
/// Stage subscriptions (<c>when RelName Stage</c>) generate cross-entity notification:
/// the subscriber entity declares a <c>When{Target}{Stage}</c> handler method
/// (peer parameter when <c>when … as name</c>; <c>when all</c> also takes the
/// transitioning record's previous stage), and the target entity emits a
/// subscriber list + notify call after each stage transition
/// (<c>sub.When…(previousStage)</c>, <c>sub.When…(this)</c>, or both).
/// </summary>
public sealed partial class DomainToCSharpExporter {
    private static Node StageEnumMember(string enumName, string stageName) =>
        new Member(new NamedTypeReference(enumName), stageName);

    private static Node EqualToStage(Node currentStage, string enumName, string stageName) =>
        new Equal(currentStage, StageEnumMember(enumName, stageName));

    private static Node NotEqualToStage(Node currentStage, string enumName, string stageName) =>
        new NotEqual(currentStage, StageEnumMember(enumName, stageName));

    private static Node InWatchedStages(Node currentStage, string enumName, IEnumerable<string> stageNames) {
        Node? acc = null;
        foreach (var stageName in stageNames) {
            var eq = EqualToStage(currentStage, enumName, stageName);
            acc = acc is null ? eq : new Syntactic.Or(acc, eq);
        }
        return acc!;
    }

    private static Node NotInWatchedStages(Node currentStage, string enumName, IEnumerable<string> stageNames) {
        Node? acc = null;
        foreach (var stageName in stageNames) {
            var neq = NotEqualToStage(currentStage, enumName, stageName);
            acc = acc is null ? neq : new Syntactic.And(acc, neq);
        }
        return acc!;
    }

    // ── Per-entity builder ──────────────────────────────────────

    private static Node EmptyDefault(DomainTypeReference type) => type.TypeName switch {
        "Text" or "String" => new Constant(""),
        "Boolean" => new Constant(false),
        _ => new Constant(0L),
    };

    internal static IReadOnlyList<TypeDefinitionNode> BuildTypeDefsForEntity(
        Entity entity,
        Domain domain,
        IReadOnlyList<Relationship> domainRelationships,
        IReadOnlyDictionary<string, Entity> entityLookup,
        INodeMetadataProvider metadata,
        List<SubscriptionInfo>? targetSubs = null,
        List<SubscriptionInfo>? subscriberSubs = null,
        IReadOnlyDictionary<SubscriptionInfo, string>? handlerNames = null,
        Dictionary<(string Entity, string Policy), Node>? policyBodies = null,
        Dictionary<SubscriptionDispatchPlanEntry, Dictionary<string, Node>>? subscriptionBodies = null,
        ContractSplicePlan? splice = null) {

        ArgumentNullException.ThrowIfNull(metadata);

        var esm = metadata.GetStructure(entity)
            ?? throw new InvalidOperationException(
                $"EntityStructureMetadata is required for entity '{entity.Name}'.");

        var typeDefs = new List<TypeDefinitionNode>();
        var props = new List<PropertyDefinitionNode>();
        var methods = new List<MethodDefinitionNode>();
        var fields = new List<FieldDefinitionNode>();
        var ctorParams = new List<Parameter>();
        var ctorAssignments = new List<Node>();
        // Defaulted props become TRAILING optional ctor params (C# requires optional
        // params after all required ones); collected here, appended after nav params.
        var defaultedCtorParams = new List<Parameter>();

        // Stage enum name — the published fact (EntityStructureMetadata.StageEnumTypeName)
        // is the single source; fall back to the derivation only when the bag is absent.
        var stageEnumTypeName = esm.HasStages
            ? esm.StageEnumTypeName ?? $"{entity.Name}Stage"
            : null;

        // Properties assigned by the initial stage's entry effects are body-initialized
        // (the ctor already runs those effects after setting CurrentStage) — they must
        // NOT also be ctor params, or the value is written twice (param then effect)
        // and the param is dead (e.g. StartedAt: param, then entry `assign StartedAt to now`).
        // The set is a published analysis fact (EntityStructureMetadata) — shared with
        // the constructor-signature computation so both stay in lockstep.
        var entryAssignedProps = esm.EntryAssignedPropertyNames;

        // ── Properties ────────────────────────────────────────────
        AddEntityProperties(entity, domain, metadata, esm, entryAssignedProps,
            props, ctorParams, ctorAssignments, defaultedCtorParams);
        OrderScalarCtorSlots(esm, ctorParams, ctorAssignments);

        // ── Relationships ─────────────────────────────────────────
        AddNavigationMembers(entity, domain, domainRelationships, metadata, subscriberSubs,
            props, fields, methods);
        AddNavigationCtorParameters(esm, domain, metadata, ctorParams, ctorAssignments);

        // Defaulted props trail the required scalar + nav params (optional params must
        // come last). Appended here so the Create/ctor signatures carry them as overridable
        // defaults — `create in { DefaultedProp: value }` flows through construction.
        ctorParams.AddRange(defaultedCtorParams);

        // Watched stages get Notify{Stage}Subscribers; EffectLoweringPass captures
        // the previous stage only when a when-all handler reads it.
        IReadOnlySet<string>? postTransitionNotifyStages = null;
        IReadOnlySet<string>? previousStageNotifyStages = null;
        if (targetSubs is { Count: > 0 }) {
            postTransitionNotifyStages = targetSubs
                .Select(s => s.StageName)
                .ToHashSet(StringComparer.Ordinal);
            previousStageNotifyStages = targetSubs
                .Where(NeedsAllPreviousStage)
                .Select(s => s.StageName)
                .ToHashSet(StringComparer.Ordinal);
        }

        // ── Actions as void methods ───────────────────────────────
        // Same action name on multiple stages is one C# method that dispatches on
        // CurrentStage. Emitting one method per stage produced illegal duplicate members
        // (FieldService WorkOrder.Cancel on Draft/Scheduled/Blocked).
        AddActionMethods(entity, methods, stageEnumTypeName, postTransitionNotifyStages,
            previousStageNotifyStages, domain, metadata, splice);

        // ── Policies as bool methods ──────────────────────────────
        AddPolicyMethods(entity, domain, metadata, methods, policyBodies);

        // ── Notify dispatch and handlers ──────────────────────────
        AddSubscriptionRegistry(targetSubs, stageEnumTypeName, handlerNames, fields, methods);
        AddSubscriptionHandlers(entity, domain, metadata, esm, subscriberSubs, handlerNames,
            postTransitionNotifyStages, previousStageNotifyStages, stageEnumTypeName,
            subscriptionBodies, methods);

        // ── Stage enum + CurrentStage property ────────────────────
        AddStageMembers(entity, esm, stageEnumTypeName, typeDefs, props);

        // ── Private constructor + public static Create factory ─────
        var ctors = AddCreateFactory(entity, domain, metadata, esm, stageEnumTypeName,
            postTransitionNotifyStages, previousStageNotifyStages, subscriberSubs,
            ctorParams, ctorAssignments, methods);

        // Generated C# fans out via Notify{Stage}Subscribers; do not emit a
        // no-op Notify(string) (Fleet dogfood).

        // ── InitializeSubscriptions — for post-load subscription wiring ──
        AddSubscriptionInitializer(subscriberSubs, methods);

        if (entity.Properties.Any(p => p.Constraints.OfType<UniqueConstraint>().Any())) {
            methods.Add(EnsureUniqueAssignMethod(entity));
        }

        AddAutomaticTransitionLoopGuard(entity, fields, methods);
        if (targetSubs is { Count: > 0 })
            AddNotifyCascadeDepthGuard(entity, fields, methods);

        typeDefs.Add(new TypeDefinitionNode(
            entity.Name,
            Constructors: ctors,
            Properties: props.Count > 0 ? props : null,
            Methods: methods.Count > 0 ? methods : null,
            Fields: fields.Count > 0 ? fields : null,
            Semantics: Syntactic.TypeDefinitionSemantics.MutableReference
        ));

        return typeDefs;
    }

    // ── Type mapping ────────────────────────────────────────────

    internal static Node MapDomainTypeRef(DomainTypeReference domainType,
        Domain domain, INodeMetadataProvider analysis) {
        var typeName = domainType.TypeName;

        // Enum types and entity references both emit NamedTypeReference(typeName):
        // the catalog is the single source of truth and no tree scan is performed (amu-w3-1).
        var clr = RuntimeAnalysisCache.ClrTypeName(domain, typeName);
        if (DomainTypeMapping.TryPrimitiveType(clr, out var prim))
            return new PrimitiveTypeReference(prim);
        return new NamedTypeReference(typeName);
    }

    internal static bool TryResolveEnumType(Domain domain, INodeMetadataProvider analysis, string typeName, out EnumType? enumType) {
        enumType = null;

        // Catalog only: fail closed (no domain tree rescan).
        var lookup = analysis.GetTypeLookup(domain);
        if (lookup is not null
            && lookup.Types.TryGetValue(typeName, out var resolvedType)
            && resolvedType is EnumType resolvedEnum) {
            enumType = resolvedEnum;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Builds a map from property name → enum type name for all properties of
    /// <paramref name="entity"/> whose type resolves to an <see cref="EnumType"/>.
    /// Used by the expression lowering pass to emit qualified enum member access.
    /// Catalog-only.
    /// </summary>
    /// <summary>
    /// Returns the entity's enum-typed property map (property name → enum type name),
    /// preferring the published <see cref="EntityStructureMetadata.EnumPropertyNames"/>
    /// bag and falling back to the catalog type lookup when the bag is absent.
    /// </summary>
    internal static IReadOnlyDictionary<string, string>? GetEnumPropertyNames(
        Entity entity, Domain domain, INodeMetadataProvider analysis) {
        if (analysis.GetStructure(entity) is { EnumPropertyNames: not null } esm)
            return esm.EnumPropertyNames;
        return BuildEnumPropertyNames(entity, domain, analysis);
    }

    internal static Dictionary<string, string>? BuildEnumPropertyNames(
        Entity entity, Domain domain, INodeMetadataProvider analysis) {
        Dictionary<string, string>? map = null;
        var types = (analysis.GetTypeLookup(domain)
            ?? throw new InvalidOperationException(
                "Domain catalog type lookup is required for enum property mapping.")).Types;

        foreach (var prop in entity.Properties) {
            if (types.TryGetValue(prop.Type.TypeName, out var resolved)
                && resolved is EnumType) {
                (map ??= new(StringComparer.Ordinal))[prop.Name] = prop.Type.TypeName;
            }
        }
        return map;
    }

    // ── DomainResult infrastructure type builders ───────────────

    /// <summary>
    /// Builds the <c>DomainResult</c> record struct: a discrimated-union-like
    /// result type for void actions. Methods return <c>DomainResult.Success()</c>
    /// or <c>DomainResult.Failure(message)</c> instead of throwing or returning void.
    /// Consumers switch on <see cref="IsSuccess"/> to handle success/failure.
    /// </summary>
    internal static TypeDefinitionNode BuildDomainResultTypeDef() {
        // public readonly record struct DomainResult
        // {
        //     public bool IsSuccess { get; }
        //     public string? ErrorMessage { get; }
        //
        //     private DomainResult(bool isSuccess, string? errorMessage)
        //     {
        //         IsSuccess = isSuccess;
        //         ErrorMessage = errorMessage;
        //     }
        //
        //     public static DomainResult Success() => new(true, null);
        //     public static DomainResult Failure(string message) => new(false, message);
        // }

        var props = new List<PropertyDefinitionNode>
        {
            new("IsSuccess",
                new PrimitiveTypeReference(PrimType.Boolean),
                Getter: new PropertyGetterDefinitionNode()),
            new("ErrorMessage",
                new OptionalTypeReference(
                    new PrimitiveTypeReference(PrimType.String)),
                Getter: new PropertyGetterDefinitionNode()),
        };

        var ctor = new ConstructorDefinitionNode(
            Parameters: [
                new Parameter("isSuccess",
                    new PrimitiveTypeReference(PrimType.Boolean)),
                new Parameter("errorMessage",
                    new OptionalTypeReference(
                        new PrimitiveTypeReference(PrimType.String))),
            ],
            Body: new Block([
                new Assignment(
                    new Member(new ThisReference(), "IsSuccess"),
                    new Parameter("isSuccess")),
                new Assignment(
                    new Member(new ThisReference(), "ErrorMessage"),
                    new Parameter("errorMessage")),
            ]),
            AccessModifier: AccessModifier.Private
        );

        var methods = new List<MethodDefinitionNode>
        {
            new("Success",
                new NamedTypeReference("DomainResult"),
                Body: new Block([
                    new Return(
                        new New(
                            new NamedTypeReference("DomainResult"),
                            new Constant(true),
                            new Constant(null)))
                ]),
                IsStatic: true,
                AccessModifier: AccessModifier.Public),
            new("Failure",
                new NamedTypeReference("DomainResult"),
                Parameters: [
                    new Parameter("message",
                        new PrimitiveTypeReference(PrimType.String))
                ],
                Body: new Block([
                    new Return(
                        new New(
                            new NamedTypeReference("DomainResult"),
                            new Constant(false),
                            new Parameter("message")))
                ]),
                IsStatic: true,
                AccessModifier: AccessModifier.Public),
        };

        return new TypeDefinitionNode(
            "DomainResult",
            Properties: props,
            Constructors: [ctor],
            Methods: methods,
            Semantics: Syntactic.TypeDefinitionSemantics.ImmutableValue
        );
    }

    /// <summary>
    /// Printed twin of runtime <c>ConstraintFailureException</c>: a
    /// string-message exception that void OnEntry / ctor / subscription trees throw.
    /// </summary>
    internal static TypeDefinitionNode BuildConstraintFailureExceptionTypeDef() {
        var ctor = new ConstructorDefinitionNode(
            Parameters: [
                new Parameter("message",
                    new PrimitiveTypeReference(PrimType.String))
            ],
            BaseCall: [new Parameter("message")]
        );
        return new TypeDefinitionNode(
            "ConstraintFailureException",
            Constructors: [ctor],
            BaseType: new NamedTypeReference("System.Exception")
        );
    }

    /// <summary>
    /// Builds the <c>DomainResult&lt;T&gt;</c> generic record: a typed result
    /// for non-void actions. Returns <c>DomainResult&lt;T&gt;.Success(value)</c> or
    /// <c>DomainResult&lt;T&gt;.Failure(message)</c>. <c>Value</c> stays non-nullable
    /// <c>T</c> and throws when the result failed. The getter is an expression over
    /// a private method because <c>CSharpReader</c> accepts <c>get;</c> and
    /// <c>get =&gt; expr</c>, not a block-bodied getter.
    /// </summary>
    internal static TypeDefinitionNode BuildDomainResultGenericTypeDef() {
        // public record DomainResult<T>
        // {
        //     private T _value;
        //     public bool IsSuccess { get; }
        //     public T Value => this.GetValue();
        //     public string? ErrorMessage { get; }
        //
        //     private DomainResult(bool isSuccess, T value, string? errorMessage)
        //     {
        //         IsSuccess = isSuccess;
        //         _value = value;
        //         ErrorMessage = errorMessage;
        //     }
        //
        //     public static DomainResult<T> Success(T value) => new(true, value, null);
        //     public static DomainResult<T> Failure(string message) => new(false, default!, message);
        //     private T GetValue()
        //     {
        //         if (!this.IsSuccess)
        //             throw new System.InvalidOperationException("A failed DomainResult has no Value.");
        //         return this._value;
        //     }
        // }

        var tParam = new NamedTypeReference("T");
        var actionResultT = new NamedTypeReference("DomainResult",
            TypeArguments: [tParam]);

        var props = new List<PropertyDefinitionNode>
        {
            new("IsSuccess",
                new PrimitiveTypeReference(PrimType.Boolean),
                Getter: new PropertyGetterDefinitionNode()),
            new("Value", tParam,
                Getter: new PropertyGetterDefinitionNode(
                    new Invoke(new Member(new ThisReference(), "GetValue")))),
            new("ErrorMessage",
                new OptionalTypeReference(
                    new PrimitiveTypeReference(PrimType.String)),
                Getter: new PropertyGetterDefinitionNode()),
        };

        var ctor = new ConstructorDefinitionNode(
            Parameters: [
                new Parameter("isSuccess",
                    new PrimitiveTypeReference(PrimType.Boolean)),
                new Parameter("value", tParam),
                new Parameter("errorMessage",
                    new OptionalTypeReference(
                        new PrimitiveTypeReference(PrimType.String))),
            ],
            Body: new Block([
                new Assignment(
                    new Member(new ThisReference(), "IsSuccess"),
                    new Parameter("isSuccess")),
                new Assignment(
                    new Member(new ThisReference(), "_value"),
                    new Parameter("value")),
                new Assignment(
                    new Member(new ThisReference(), "ErrorMessage"),
                    new Parameter("errorMessage")),
            ]),
            AccessModifier: AccessModifier.Private
        );

        var methods = new List<MethodDefinitionNode>
        {
            new("Success", actionResultT,
                Parameters: [
                    new Parameter("value", tParam)
                ],
                Body: new Block([
                    new Return(
                        new New(actionResultT,
                            new Constant(true),
                            new Parameter("value"),
                            new Constant(null)))
                ]),
                IsStatic: true,
                AccessModifier: AccessModifier.Public),
            new("Failure", actionResultT,
                Parameters: [
                    new Parameter("message",
                        new PrimitiveTypeReference(PrimType.String))
                ],
                Body: new Block([
                    new Return(
                        new New(actionResultT,
                            new Constant(false),
                            new NullForgiving(new Default()),
                            new Parameter("message")))
                ]),
                IsStatic: true,
                AccessModifier: AccessModifier.Public),
            new("GetValue", tParam,
                Body: new Block([
                    new IfStatement(
                        new Syntactic.Not(new Member(new ThisReference(), "IsSuccess")),
                        new ThrowStatement(
                            new New(
                                new NamedTypeReference("System.InvalidOperationException"),
                                new Constant("A failed DomainResult has no Value.")))),
                    new Return(new Member(new ThisReference(), "_value")),
                ]),
                AccessModifier: AccessModifier.Private),
        };

        return new TypeDefinitionNode(
            "DomainResult",
            GenericParameters: [new Parameter("T")],
            Properties: props,
            Constructors: [ctor],
            Methods: methods,
            Fields: [
                new FieldDefinitionNode(
                    "_value",
                    tParam,
                    AccessModifier: AccessModifier.Private),
            ],
            Semantics: Syntactic.TypeDefinitionSemantics.ImmutableValue
        );
    }

    // ── String helpers ──────────────────────────────────────────

    internal static string ToCamelCase(string name) => DomainTypeMapping.ToCamelCase(name);

    internal static string ToPascalCase(string name) => DomainTypeMapping.ToPascalCase(name);
}