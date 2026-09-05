using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Contract;

using AccessModifier = Poly.Introspection.AccessModifier;
using Action = Poly.DomainModeling.Ontology.Action;
using PrimType = Poly.Introspection.PrimitiveType;
using Syntactic = Poly.Ast.Nodes;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.DomainModeling.Lowering;

public sealed partial class DomainToCSharpExporter {
    // ── Action method builder ───────────────────────────────────

    private static void AddActionMethods(Entity entity, List<MethodDefinitionNode> methods,
        string? stageEnumTypeName,
        IReadOnlyDictionary<string, IReadOnlyList<Node>>? postTransitionNodes,
        Domain? domain, INodeMetadataProvider? analysis) {
        var variants = new List<(Action Action, string? SourceStage)>();
        foreach (var action in entity.Actions)
            variants.Add((action, null));
        foreach (var stage in entity.Stages)
            foreach (var action in stage.Actions)
                variants.Add((action, stage.Name));

        foreach (var group in variants.GroupBy(v => v.Action.Name, StringComparer.Ordinal)) {
            var items = group.ToList();
            if (items.Count == 1) {
                var (action, source) = items[0];
                AddActionMethod(entity, action, methods, stageEnumTypeName, postTransitionNodes,
                    source, domain, analysis);
                continue;
            }

            AddStageDispatchedActionMethod(entity, items, methods, stageEnumTypeName,
                postTransitionNodes, domain, analysis);
        }
    }

    private static void AddActionMethod(Entity entity, Action action,
        List<MethodDefinitionNode> methods, string? stageEnumTypeName = null,
        IReadOnlyDictionary<string, IReadOnlyList<Node>>? postTransitionNodes = null,
        string? sourceStageName = null, Domain? domain = null,
        INodeMetadataProvider? analysis = null) {
        var isVoid = action.Result is not { Members.Count: > 0 };
        var body = BuildFullActionBody(entity, action, stageEnumTypeName, postTransitionNodes,
            loweringSourceStage: sourceStageName, guardSourceStage: sourceStageName,
            domain, analysis, isVoid);

        // All actions return DomainResult (void) or DomainResult<T> (typed).
        // This lets callers pattern-match on IsSuccess without exceptions.
        Node returnType;
        if (isVoid) {
            returnType = new NamedTypeReference("DomainResult");
        }
        else {
            var innerType = MapDomainTypeRef(action.Result.Members[0].Type, domain, analysis);
            returnType = new NamedTypeReference("DomainResult",
                TypeArguments: [innerType]);
        }

        methods.Add(new MethodDefinitionNode(
            action.Name,
            returnType,
            Parameters: action.Parameters
                .Select(p => new Parameter(p.Name, MapDomainTypeRef(p.Type, domain, analysis)))
                .ToList(),
            Body: body,
            AccessModifier: AccessModifier.Public
        ));
    }

    private static Block BuildFullActionBody(Entity entity, Action action,
        string? stageEnumTypeName,
        IReadOnlyDictionary<string, IReadOnlyList<Node>>? postTransitionNodes,
        string? loweringSourceStage, string? guardSourceStage, Domain? domain,
        INodeMetadataProvider? analysis, bool isVoid) {
        var paramNames = new HashSet<string>(
            action.Parameters.Select(p => p.Name), StringComparer.Ordinal);
        var effectsBody = LowerActionToMethodBody(entity, action, paramNames, stageEnumTypeName,
            postTransitionNodes, loweringSourceStage, domain, analysis, isVoid);
        effectsBody = PrependAdapterInvocation(domain, action, effectsBody);
        return BuildActionBodyWithGuards(action, entity, effectsBody, domain,
            guardSourceStage, stageEnumTypeName, isVoid, analysis);
    }

    private static void AddStageDispatchedActionMethod(Entity entity,
        List<(Action Action, string? SourceStage)> variants,
        List<MethodDefinitionNode> methods, string? stageEnumTypeName,
        IReadOnlyDictionary<string, IReadOnlyList<Node>>? postTransitionNodes,
        Domain? domain, INodeMetadataProvider? analysis) {
        var representative = variants[0].Action;
        var paramSignature = string.Join(",", representative.Parameters.Select(p => p.Name));
        foreach (var (action, _) in variants) {
            var sig = string.Join(",", action.Parameters.Select(p => p.Name));
            if (!string.Equals(sig, paramSignature, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Action '{action.Name}' on entity '{entity.Name}' has incompatible parameter lists across stages.");
        }

        var isVoid = representative.Result is not { Members.Count: > 0 };
        Node actionResultType = isVoid
            ? new NamedTypeReference("DomainResult")
            : new NamedTypeReference("DomainResult",
                TypeArguments: [MapDomainTypeRef(representative.Result!.Members[0].Type, domain, analysis)]);

        var nodes = new List<Node>();
        Action? entityLevel = null;
        foreach (var (action, sourceStage) in variants) {
            if (sourceStage is null)
                entityLevel = action;
        }
        foreach (var (action, sourceStage) in variants) {
            if (sourceStage is null)
                continue;

            if (stageEnumTypeName is null)
                throw new InvalidOperationException(
                    $"Action '{action.Name}' is stage-scoped on '{entity.Name}' but no stage enum was emitted.");

            // SA empty stage-copy (no effects/policies) → entity body. Same as TryResolveAction.
            var branchAction = action.Effects.Count == 0
                && action.Policies.Count == 0
                && entityLevel is not null
                ? entityLevel
                : action;
            var branchBody = BuildFullActionBody(entity, branchAction, stageEnumTypeName, postTransitionNodes,
                loweringSourceStage: sourceStage, guardSourceStage: null, domain, analysis, isVoid);
            nodes.Add(new IfStatement(
                new Equal(
                    new Member(new ThisReference(), "CurrentStage"),
                    new Member(new NamedTypeReference(stageEnumTypeName), sourceStage)),
                branchBody));
        }

        if (entityLevel is not null) {
            nodes.AddRange(BuildFullActionBody(entity, entityLevel, stageEnumTypeName,
                postTransitionNodes, loweringSourceStage: null, guardSourceStage: null,
                domain, analysis, isVoid).Nodes);
        }
        else {
            nodes.Add(new Return(
                new Invoke(
                    new Member(actionResultType, "Failure"),
                    new Constant($"'{representative.Name}' is not valid for the current stage on entity '{entity.Name}'."))));
        }

        Node returnType = isVoid
            ? new NamedTypeReference("DomainResult")
            : actionResultType;

        methods.Add(new MethodDefinitionNode(
            representative.Name,
            returnType,
            Parameters: representative.Parameters
                .Select(p => new Parameter(p.Name, MapDomainTypeRef(p.Type, domain, analysis)))
                .ToList(),
            Body: new Block(nodes),
            AccessModifier: AccessModifier.Public
        ));
    }

    /// <summary>
    /// Builds a method body with require gate guard clauses prepended before the effects.
    /// Always references entity-level policy methods (<c>bool PolicyName()</c>), not synthetic
    /// action-scoped policy copies. For <c>require not PolicyName</c> (which the parser encodes
    /// as a synthetic <c>not_PolicyName</c> policy on the action), we strip the prefix and
    /// invert the condition so the generated code calls the real entity-level method.
    ///
    /// All guards return <c>DomainResult.Failure("message")</c> (void) or
    /// <c>DomainResult&lt;T&gt;.Failure("message")</c> (typed) instead of throwing
    /// or returning default — enabling clean pattern matching at call sites.
    ///
    /// Generated patterns:
    ///   Stage-scoped:          <c>return DomainResult.Failure("'CheckOut' is not valid for stage 'Active'...");</c>
    ///   <c>require AtLimit</c>     → <c>return DomainResult.Failure("Policy 'AtLimit' not satisfied.");</c>
    ///   <c>require not AtLimit</c> → <c>return DomainResult.Failure("Policy 'AtLimit' not satisfied.");</c>
    ///
    /// Returns a <see cref="Block"/> with at least an empty body — never null —
    /// so the C# generator emits <c>{ }</c> rather than an invalid semicolon.
    /// </summary>
    private static Block BuildActionBodyWithGuards(
        Action action, Entity entity, Node? effectsBody,
        Domain? domain = null, string? sourceStageName = null, string? stageEnumTypeName = null,
        bool isVoid = true, INodeMetadataProvider? analysis = null) {

        // Build the DomainResult type reference for failure/success returns
        Node actionResultType;
        Node resultTypeRef;
        if (isVoid) {
            actionResultType = new NamedTypeReference("DomainResult");
            resultTypeRef = new NamedTypeReference("DomainResult");
        }
        else {
            resultTypeRef = MapDomainTypeRef(action.Result!.Members[0].Type, domain, analysis);
            actionResultType = new NamedTypeReference("DomainResult",
                TypeArguments: [resultTypeRef]);
        }

        // Helper: DomainResult[<T>].Failure("message")
        Node FailureReturn(string message) => new Return(
            new Invoke(
                new Member(actionResultType, "Failure"),
                new Constant(message)));

        // Collect all nodes: require guards first, then effects
        var nodes = new List<Node>();
        var locals = new List<Node>();

        // Emit stage guard for stage-scoped actions.
        // Returns DomainResult[<T>].Failure("'CheckOut' requires stage 'Active' on entity 'Patron'.")
        if (sourceStageName is not null && stageEnumTypeName is not null) {
            nodes.Add(new IfStatement(
                new NotEqual(
                    new Member(new ThisReference(), "CurrentStage"),
                    new Member(
                        new NamedTypeReference(stageEnumTypeName),
                        sourceStageName)),
                new Block([
                    FailureReturn(
                        $"'{action.Name}' requires stage '{sourceStageName}' on entity '{entity.Name}'.")
                ])));
        }

        // Emit require guard clauses. Named entity policies are predicates, not
        // always-on invariants — they gate an action only when the action `require`s
        // them (FieldService AtCapacity must not block ClockIn / GoOffDuty).
        foreach (var policy in action.Policies) {
            if (policy.Name.StartsWith("not_", StringComparison.Ordinal)) {
                var realName = policy.Name.Substring(4);
                var guardCall = new Invoke(
                    new Member(new ThisReference(), realName));
                nodes.Add(new IfStatement(
                    guardCall,
                    new Block([FailureReturn(
                        $"'{action.Name}' blocked by policy '{realName}'.")])));
            }
            else {
                var guardCall = new Invoke(
                    new Member(new ThisReference(), policy.Name));
                nodes.Add(new IfStatement(
                    new Syntactic.Not(guardCall),
                    new Block([FailureReturn(
                        $"'{action.Name}' blocked by policy '{policy.Name}'.")])));
            }
        }

        // Append the effects body
        if (effectsBody is Block block) {
            nodes.AddRange(block.Nodes);
            locals.AddRange(block.Variables);
        }
        else if (effectsBody is not null) {
            nodes.Add(effectsBody);
        }

        if (isVoid) {
            // Void actions end with return DomainResult.Success(); — but not when the
            // body already ended in a throw (quantified-invoke fail-loud), which would
            // make the return unreachable (CS0162).
            if (nodes.Count == 0 || nodes[^1] is not ThrowStatement) {
                nodes.Add(new Return(
                    new Invoke(
                        new Member(
                            new NamedTypeReference("DomainResult"), "Success"))));
            }
        }
        else {
            // Non-void actions: wrap the last effect node in DomainResult<T>.Success(value).
            // If the last effect produces a value (Assignment, Invoke, New, etc.),
            // wrap it: return DomainResult<T>.Success(expr).
            // If the last effect is already a Return, leave it.
            // If there are no effects (body was empty), emit a structural error.
            if (nodes.Count > 0) {
                var lastIdx = nodes.Count - 1;
                var last = nodes[lastIdx];

                if (last is Return) {
                    // Already wrapped — leave as-is.
                }
                else if (last is Syntactic.Assignment a) {
                    Node produced = a.Destination;
                    if (!isVoid)
                        produced = new TypeCast(produced, resultTypeRef);
                    nodes.Add(new Return(
                        new Invoke(
                            new Member(actionResultType, "Success"),
                            [produced])));
                }
                else if (last is Syntactic.Invoke
                         or Syntactic.Member or Syntactic.Constant
                         or Syntactic.New or Syntactic.UnaryMinus
                         or Syntactic.Not or Syntactic.Add or Syntactic.Subtract
                         or Syntactic.Multiply or Syntactic.Divide) {
                    nodes[lastIdx] = new Return(
                        new Invoke(
                            new Member(actionResultType, "Success"),
                            [last]));
                }
                else {
                    // Non-returnable last node — structural error (still throw)
                    nodes.Add(new ThrowStatement(
                        new New(
                            new NamedTypeReference("NotSupportedException"),
                            new Constant(
                                $"Action '{action.Name}' has return type but its last effect " +
                                $"does not produce a value. Use an 'assign' statement as the " +
                                $"final effect, or remove the -> return type declaration."))));
                }
            }
            else {
                // Empty action body with declared return type — structural error
                nodes.Add(new ThrowStatement(
                    new New(
                        new NamedTypeReference("NotSupportedException"),
                        new Constant(
                            $"Action '{action.Name}' has return type but has no effects."))));
            }
        }

        // Block is a sequence; an empty action body is an empty block.
        return new Block(nodes, locals);
    }

    // ── Lowering helpers ────────────────────────────────────────

    /// <summary>
    /// When <paramref name="action"/> is bound to a contract endpoint, prepends the adapter
    /// invocation (<c>{Contract}Adapters.{Endpoint}({param})</c>) to the effects body. The
    /// binding is exported as a call into the emitted adapter (which throws until an
    /// in-process adapter is registered) — never dropped, never a silent no-op. Unknown
    /// contract/endpoint/binding are left untouched: analysis already rejected them, and the
    /// projection never second-guesses a valid model.
    /// </summary>
    private static Node? PrependAdapterInvocation(Domain? domain, Action action, Node? effectsBody) {
        if (domain is null) return effectsBody;
        var binding = domain.ContractBindings.FirstOrDefault(b =>
            string.Equals(b.ActionName, action.Name, StringComparison.Ordinal));
        if (binding is null) return effectsBody;
        var contract = domain.ImportedContracts.FirstOrDefault(c =>
            string.Equals(c.Name, binding.ContractName, StringComparison.Ordinal));
        if (contract is null) return effectsBody;
        var endpoint = contract.Endpoints.FirstOrDefault(e =>
            string.Equals(e.Name, binding.EndpointName, StringComparison.Ordinal));
        if (endpoint is null) return effectsBody;

        var call = new Invoke(
            new Member(new TypeReference($"{contract.Name}Adapters"), endpoint.Name),
            new Variable(binding.LocalParameterName));
        return effectsBody is Block block
            ? new Block([call, .. block.Nodes], block.Variables)
            : new Block([call]);
    }

    /// <summary>
    /// Builds the fail-closed adapter class for a contract with at least one bound endpoint.
    /// One static method per bound endpoint; each throws <c>NotImplementedException</c> at
    /// runtime until an in-process adapter is registered. There is no second parse of the
    /// child domain — the produced contract endpoint has no callable in the exported root,
    /// so an unimplemented binding fails loud instead of silently succeeding.
    /// </summary>
    internal static TypeDefinitionNode BuildContractAdapterTypeDef(
        ImportedContract contract, IReadOnlyList<ContractEndpoint> boundEndpoints) {
        var methods = new List<MethodDefinitionNode>();
        foreach (var endpoint in boundEndpoints) {
            var payload = new NamedTypeReference(
                DomainTypeMapping.ToClrTypeName(endpoint.PayloadType.TypeName));
            methods.Add(new MethodDefinitionNode(
                endpoint.Name,
                new NamedTypeReference("void"),
                Parameters: [new Parameter("request", payload)],
                Body: new Block([
                    new ThrowStatement(
                        new New(
                            new NamedTypeReference("NotImplementedException"),
                            new Constant(
                                $"Contract endpoint '{contract.Name}.{endpoint.Name}' has no implementation. " +
                                "The export emits a fail-closed adapter; register an in-process adapter to serve bound calls."))),
                ]),
                IsStatic: true,
                AccessModifier: AccessModifier.Public
            ));
        }

        return new TypeDefinitionNode(
            $"{contract.Name}Adapters",
            Constructors: [
                new ConstructorDefinitionNode(
                    Parameters: [],
                    Body: new Block([]),
                    AccessModifier: AccessModifier.Private),
            ],
            Methods: methods,
            Semantics: Syntactic.TypeDefinitionSemantics.MutableReference
        );
    }

    internal static Node? LowerActionToMethodBody(
        Entity entity, Action action,
        HashSet<string>? paramNames = null, string? stageEnumTypeName = null,
        IReadOnlyDictionary<string, IReadOnlyList<Node>>? postTransitionNodes = null,
        string? sourceStageName = null, Domain? domain = null,
        INodeMetadataProvider? analysis = null, bool isVoid = true) {
        if (action.Effects.Count == 0) return null;
        var enumProps = GetEnumPropertyNames(entity, domain, analysis);
        Node actionResultType = isVoid
            ? new NamedTypeReference("DomainResult")
            : new NamedTypeReference("DomainResult",
                TypeArguments: [MapDomainTypeRef(action.Result.Members[0].Type, domain, analysis)]);
        var context = new LoweringContext(
            new Parameter("entity", new TypeReference(entity.Name)),
            Analysis: analysis,
            UseThisReference: true,
            ActionParameterNames: paramNames,
            StageEnumTypeName: stageEnumTypeName,
            PostTransitionNodes: postTransitionNodes,
            SourceStageName: sourceStageName,
            Domain: domain,
            EnumPropertyNames: enumProps,
            ActionResultType: actionResultType,
            EmitInstanceNotify: false);
        var effectPass = new EffectLoweringPass(entity, context);
        return effectPass.LowerActionBody(action.Effects);
    }

    internal static Node? LowerExpressionToMethodBody(
        DomainExpression expr, Entity entity, Domain? domain = null,
        INodeMetadataProvider? analysis = null) {
        var enumProps = GetEnumPropertyNames(entity, domain, analysis);
        var context = new LoweringContext(
            new Parameter("entity", new TypeReference(entity.Name)),
            Analysis: analysis,
            UseThisReference: true,
            EnumPropertyNames: enumProps,
            NavigationNameResolver: EffectLoweringPass.BuildNavigationNameResolver(entity, domain, analysis),
            IsCollectionNavigation: EffectLoweringPass.BuildIsCollectionNavigation(entity, domain, analysis),
            PropertyTypeResolver: EffectLoweringPass.BuildPropertyTypeResolver(entity));
        var pass = new DomainExpressionLoweringPass(context);
        var lowered = pass.Lower(expr, new Parameter("entity"));
        return lowered is not null
            ? new Block([new Return(lowered)])
            : null;
    }

    internal static bool TryResolveEnumType(Domain? domain, INodeMetadataProvider? analysis, string typeName, out EnumType? enumType) {
        enumType = null;

        // Catalog primary when analysis present.
        if (analysis is not null) {
            var lookup = analysis.GetTypeLookup(domain);
            if (lookup is not null
                && lookup.Types.TryGetValue(typeName, out var resolvedType)
                && resolvedType is EnumType resolvedEnum) {
                enumType = resolvedEnum;
                return true;
            }
            // Analysis present: fail closed (no domain tree rescan).
            return false;
        }

        // Null-analysis residual for non-product/test callers only.
        if (domain is not null) {
            enumType = domain.Types.OfType<EnumType>()
                .FirstOrDefault(e => string.Equals(e.Name, typeName, StringComparison.Ordinal));
            return enumType is not null;
        }

        return false;
    }

    /// <summary>
    /// Builds a map from property name → enum type name for all properties of
    /// <paramref name="entity"/> whose type resolves to an <see cref="EnumType"/>.
    /// Used by the expression lowering pass to emit qualified enum member access.
    /// Catalog-only when <paramref name="analysis"/> is present.
    /// </summary>
    /// <summary>
    /// Returns the entity's enum-typed property map (property name → enum type name),
    /// preferring the published <see cref="EntityStructureMetadata.EnumPropertyNames"/>
    /// bag and falling back to a catalog re-scan only for the null-analysis path.
    /// </summary>
    internal static IReadOnlyDictionary<string, string>? GetEnumPropertyNames(
        Entity entity, Domain? domain, INodeMetadataProvider? analysis) {
        if (analysis is not null
            && analysis.GetStructure(entity) is { EnumPropertyNames: not null } esm)
            return esm.EnumPropertyNames;
        return domain is not null ? BuildEnumPropertyNames(entity, domain, analysis) : null;
    }

    internal static Dictionary<string, string>? BuildEnumPropertyNames(
        Entity entity, Domain domain, INodeMetadataProvider? analysis = null) {
        Dictionary<string, string>? map = null;
        IReadOnlyDictionary<string, DomainType> types;
        if (analysis is not null) {
            var lookup = analysis.GetTypeLookup(domain)
                ?? throw new InvalidOperationException(
                    "Domain catalog type lookup is required for enum property mapping when analysis is present.");
            types = lookup.Types;
        }
        else {
            types = domain.Types.ToDictionary(t => t.Name, StringComparer.Ordinal);
        }

        foreach (var prop in entity.Properties) {
            if (types.TryGetValue(prop.Type.TypeName, out var resolved)
                && resolved is EnumType) {
                (map ??= new(StringComparer.Ordinal))[prop.Name] = prop.Type.TypeName;
            }
        }
        return map;
    }

    // ── Type mapping ────────────────────────────────────────────

    internal static Node MapDomainTypeRef(DomainTypeReference domainType,
        Domain? domain = null, INodeMetadataProvider? analysis = null) {
        var typeName = domainType.TypeName;

        // Enum types and entity references both emit NamedTypeReference(typeName),
        // so with analysis present the catalog is the single source of truth and no
        // tree scan is performed (amu-w3-1). Only null-analysis residuals rescan.
        if (analysis is null && domain is not null) {
            var enumType = domain.Types.OfType<EnumType>()
                .FirstOrDefault(e => string.Equals(e.Name, typeName, StringComparison.Ordinal));
            if (enumType is not null)
                return new NamedTypeReference(typeName);
        }

        return typeName switch {
            "Text" => new PrimitiveTypeReference(PrimType.String),
            "Number" or "Int" => new PrimitiveTypeReference(PrimType.Int64),
            "Boolean" or "Bool" => new PrimitiveTypeReference(PrimType.Boolean),
            "DateTime" or "Timestamp" => new PrimitiveTypeReference(PrimType.DateTime),
            "Date" or "DateOnly" => new PrimitiveTypeReference(PrimType.DateOnly),
            "Time" or "TimeOnly" => new PrimitiveTypeReference(PrimType.TimeOnly),
            "Duration" or "TimeSpan" => new PrimitiveTypeReference(PrimType.TimeSpan),
            "Uuid" or "Guid" => new PrimitiveTypeReference(PrimType.Guid),
            "Decimal" => new PrimitiveTypeReference(PrimType.Decimal),
            "Float" or "Double" => new PrimitiveTypeReference(PrimType.Float64),
            _ => new NamedTypeReference(typeName)
        };
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
    /// Builds the <c>DomainResult&lt;T&gt;</c> generic record struct: a typed result
    /// for non-void actions. Returns <c>DomainResult&lt;T&gt;.Success(value)</c> or
    /// <c>DomainResult&lt;T&gt;.Failure(message)</c>.
    /// </summary>
    internal static TypeDefinitionNode BuildDomainResultGenericTypeDef() {
        // public readonly record struct DomainResult<T>
        // {
        //     public bool IsSuccess { get; }
        //     public T Value { get; }
        //     public string? ErrorMessage { get; }
        //
        //     private DomainResult(bool isSuccess, T value, string? errorMessage)
        //     {
        //         IsSuccess = isSuccess;
        //         Value = value;
        //         ErrorMessage = errorMessage;
        //     }
        //
        //     public static DomainResult<T> Success(T value) => new(true, value, null);
        //     public static DomainResult<T> Failure(string message) => new(false, default!, message);
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
                    new Member(new ThisReference(), "Value"),
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
        };

        return new TypeDefinitionNode(
            "DomainResult",
            GenericParameters: [new Parameter("T")],
            Properties: props,
            Constructors: [ctor],
            Methods: methods,
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
        Domain? domain,
        INodeMetadataProvider? metadata) {
        if (defaultValue.Expression is Literal lit)
            return new Constant(lit.Value);
        if (defaultValue.Expression is PropertyAccess pa) {
            if (domain is not null
                && TryResolveEnumType(domain, metadata, prop.Type.TypeName, out var enumType)
                && enumType is not null
                && enumType.MemberNames.Contains(pa.Name, StringComparer.Ordinal)) {
                return new Member(new NamedTypeReference(enumType.Name), pa.Name);
            }
            // Non-keyword, non-enum-member PropertyAccess default cannot be lowered —
            // fail loud instead of silently dropping the constraint (which would turn
            // the property into a required Create parameter).
            if (domain is not null) {
                throw new NotSupportedException(
                    $"default({pa.Name}) on property '{prop.Name}' (type '{prop.Type.TypeName}') cannot be lowered: " +
                    $"'{pa.Name}' is not a member of an enum that '{prop.Name}' is typed with.");
            }
        }
        return null;
    }

    // ── String helpers ──────────────────────────────────────────

    internal static string ToCamelCase(string name) => DomainTypeMapping.ToCamelCase(name);

    internal static string ToPascalCase(string name) => DomainTypeMapping.ToPascalCase(name);

    /// <summary>
    /// The auto-wire back-reference for a <c>create in Rel</c> export: the single
    /// singular navigation on <paramref name="targetEntity"/> whose target is
    /// <paramref name="sourceEntityName"/>. Returns null when ambiguous (multiple
    /// singular back-refs), when the back-ref is a collection, or for
    /// self-relationships (which use the existing <c>IsBackReference</c> path).
    /// Single source of truth for both <see cref="AddCreateNavMethod"/> (factory
    /// signature + <c>this</c> wiring) and the call-site arg list
    /// (<c>EffectLoweringPass.CreateEntityInRelationship</c>).
    /// </summary>
    internal static Relationship? FindAutoWireBackReference(Entity targetEntity, string sourceEntityName) {
        if (string.Equals(sourceEntityName, targetEntity.Name, StringComparison.Ordinal))
            return null; // self-relationships use the existing IsBackReference path

        Relationship? found = null;
        var count = 0;
        foreach (var nav in targetEntity.Navigations) {
            if (nav.Cardinality is RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany)
                continue;
            if (string.Equals(nav.Target.TypeName, sourceEntityName, StringComparison.Ordinal)) {
                found = nav;
                count++;
            }
        }
        return count == 1 ? found : null;
    }

    /// <summary>
    /// Unique collection on <paramref name="peerEntity"/> whose target is
    /// <paramref name="childEntityName"/> — the C# inverse of a create-in to-one
    /// initializer (same rule as runtime <c>TryLinkInverseCollection</c>).
    /// </summary>
    internal static Relationship? FindInverseCollection(Entity peerEntity, string childEntityName) {
        Relationship? found = null;
        var count = 0;
        foreach (var nav in peerEntity.Navigations) {
            if (nav.Cardinality is not (RelationshipCardinality.OneToMany
                or RelationshipCardinality.ManyToMany))
                continue;
            if (string.Equals(nav.Target.TypeName, childEntityName, StringComparison.Ordinal)) {
                found = nav;
                count++;
            }
        }
        return count == 1 ? found : null;
    }
}