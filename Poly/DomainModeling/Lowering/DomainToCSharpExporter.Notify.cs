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
    /// <summary>
    /// Appends subscription registration statements to the constructor body.
    /// For each subscription on this entity's stage, emits code that registers
    /// this instance as a subscriber on each related target entity.
    /// </summary>
    private static void AddSubscriberRegistrationNodes(
        List<SubscriptionInfo>? subscriberSubs,
        List<Node> bodyNodes) {
        if (subscriberSubs is { Count: > 0 }) {
            foreach (var group in subscriberSubs.GroupBy(s => s.Relationship.Name)) {
                var rel = group.First().Relationship;
                var pascalNavName = ToPascalCase(rel.Name);
                var isMany = rel.Cardinality is RelationshipCardinality.OneToMany
                             or RelationshipCardinality.ManyToMany;

                // Multiple subscriptions on the same relation+stage (any/all/Each) share
                // ONE registry list on the target — register once per stage, or the
                // subscriber is added N times and each handler fires N times.
                var perStage = group.GroupBy(s => s.StageName).Select(g => g.First());

                if (isMany) {
                    foreach (var info in perStage) {
                        var subVarName = "target";
                        bodyNodes.Add(new ForEachLoop(
                            new Variable(subVarName),
                            new Member(new ThisReference(), pascalNavName),
                            new Block([
                                new Invoke(
                                    new Member(
                                        new Variable(subVarName),
                                        SubscriberRegisterMethodName(info)),
                                    [new ThisReference()])
                            ])
                        ));
                    }
                }
                else {
                    foreach (var info in perStage) {
                        bodyNodes.Add(new Invoke(
                            new Member(
                                new Member(new ThisReference(), pascalNavName),
                                SubscriberRegisterMethodName(info)),
                            [new ThisReference()])
                        );
                    }
                }
            }
        }
    }

    /// <summary>
    /// Generates a <c>Create{NavName}(args)</c> method on the source entity
    /// that constructs the target entity, adds it to the collection field,
    /// and wires subscription registration.
    ///
    /// The method parameters correspond to the target entity's constructor
    /// parameters (regular properties + singular nav properties), minus
    /// the back-reference to the source entity which is auto-wired as <c>this</c>.
    ///
    /// For DSL <c>create in loans { book: book }</c>, this produces:
    /// <code>
    /// public DomainResult&lt;Loan&gt; CreateLoans(Book book, string status, ...)
    /// {
    ///     var loanResult = Loan.Create(book: book, borrower: this, status: status, ...);
    ///     // Loan.Create Attaches via borrower:this (Add + Register*) — no duplicate here.
    ///     if (!loanResult.IsSuccess) return Failure(...);
    ///     return Success(loanResult.Value);
    /// }
    /// </code>
    /// </summary>
    private static void AddCreateSingularNavMethod(
        Entity entity,
        Relationship rel,
        Domain domain,
        List<MethodDefinitionNode> methods,
        INodeMetadataProvider metadata) =>
        AddCreateNavMethod(entity, rel, domain, subscriberSubs: null, fieldName: null, methods, metadata);

    private static void AddCreateNavMethod(
        Entity entity,
        Relationship rel,
        Domain domain,
        List<SubscriptionInfo>? subscriberSubs,
        string? fieldName,
        List<MethodDefinitionNode> methods,
        INodeMetadataProvider metadata) {
        ArgumentNullException.ThrowIfNull(metadata);

        var pascalName = ToPascalCase(rel.Name);
        var targetTypeName = rel.Target.TypeName;
        var targetType = new NamedTypeReference(targetTypeName);
        var lookup = metadata.GetTypeLookup(domain)
            ?? throw new InvalidOperationException(
                "Domain catalog type lookup is required for CreateNav export.");
        if (!lookup.Types.TryGetValue(targetTypeName, out var resolvedType)
            || resolvedType is not Entity targetEntity)
            return;

        var methodName = $"Create{pascalName}";

        // Collect all target entity constructor-level parameters from the COMPLETE
        // signature (EntityStructureMetadata now includes collection navs):
        // regular entity properties (minus defaults) + navs (singular + collection)
        // in relationship order. One source of truth — no per-consumer re-scan of
        // domain.Relationships (the CS7036 class of bugs).
        var methodParams = new List<Parameter>();
        var createArgs = new List<Node>();
        var parameterMetadata = GetConstructorParameters(targetEntity, metadata);

        // Cross-entity auto-wire back-ref: exactly one singular nav on the target
        // pointing back to the source → wired with `this`, excluded from the factory
        // signature. (Self-relationship back-refs are already excluded via IsBackReference.)
        var autoWireBackRef = FindAutoWireBackReference(targetEntity, entity.Name);

        foreach (var parameter in parameterMetadata) {
            if (parameter.IsNavigation && parameter.IsBackReference) {
                // Self-rel slot (Account.parent) is `this` only when *this* entity is
                // that type. Contact.create-in account must not pass a Contact as Account.
                createArgs.Add(string.Equals(entity.Name, targetEntity.Name, StringComparison.Ordinal)
                    ? (Node)new ThisReference()
                    : TypedNull(parameter.Type.TypeName));
                continue;
            }

            if (autoWireBackRef is not null
                && string.Equals(parameter.Name, autoWireBackRef.Name, StringComparison.Ordinal)) {
                createArgs.Add(new ThisReference());
                continue;
            }

            if (parameter.IsCollection) {
                // Collection nav: ctor param is IEnumerable<T>; the CreateNav factory
                // starts every child with an empty collection.
                createArgs.Add(new New(
                    new NamedTypeReference("List",
                        TypeArguments: [new NamedTypeReference(parameter.Type.TypeName)])));
                continue;
            }

            var paramName = ToCamelCase(parameter.Name);
            var mapped = MapDomainTypeRef(parameter.Type, domain, metadata);
            // Nav params are optional references (unbound initializers pass null) —
            // emit nullable to match the property/ctor and avoid CS8625.
            // A non-required value type is the same: omitted create-in passes null.
            var optionalValue = !parameter.IsNavigation
                && IsUnrequiredDomainValue(domain, targetEntity, parameter.Name);
            var propRef = parameter.IsNavigation || optionalValue
                ? (Node)new OptionalTypeReference(mapped)
                : mapped;
            // Same node in the signature and the Create call. A second untyped
            // Parameter does not resolve, so the VM cannot tell scalar from reference.
            // A navigation the caller did not write is omitted. It needs a default
            // so that omission is a legal trailing argument.
            var param = parameter.IsNavigation
                ? new Parameter(paramName, propRef, DefaultValue: new Constant(null))
                : new Parameter(paramName, propRef);
            methodParams.Add(param);
            createArgs.Add(param);
        }

        // NOTE: properties with DefaultValueConstraint are NOT included in
        // the Target.Create() parameter list — they are set directly in the
        // constructor body from their default expression. Do NOT append them
        // to createArgs here; the Create factory already handles them.

        // Defaulted props of the target become TRAILING optional method params (C#
        // optional params must be last) and are forwarded to Target.Create — so a
        // `create in { DefaultedProp: value }` override flows through construction.
        var entryAssigned = metadata.GetStructure(targetEntity)?.EntryAssignedPropertyNames
            ?? EntityStructureAnalyzer.ComputeEntryAssignedPropertyNames(targetEntity);
        foreach (var prop in targetEntity.Properties.OrderBy(p => p.Name)) {
            var defaultConstraint = prop.Constraints.OfType<DefaultValueConstraint>().FirstOrDefault();
            if (defaultConstraint is null) continue;
            if (entryAssigned.Contains(prop.Name)) continue;

            var paramName = ToCamelCase(prop.Name);
            var mapped = MapDomainTypeRef(prop.Type, domain, metadata);
            var runtimeExpr = EffectLoweringPass.LowerDefaultExpression(
                defaultConstraint.Expression,
                new NamedTypeReference(prop.Type.TypeName),
                RuntimeAnalysisCache.MeaningFor(domain),
                RuntimeAnalysisCache.FormsFor(domain));
            var trailing = runtimeExpr is not null
                ? new Parameter(paramName,
                    new OptionalTypeReference(mapped),
                    DefaultValue: new Constant(null))
                : new Parameter(paramName, mapped,
                    DefaultValue: LowerDefaultConstantNode(defaultConstraint, prop, domain, metadata));
            methodParams.Add(trailing);
            createArgs.Add(trailing);
        }

        var bodyNodes = new List<Node>();
        var localResult = new Variable($"{ToCamelCase(targetTypeName)}Result");
        var local = new Variable(ToCamelCase(targetTypeName));

        bodyNodes.Add(new Assignment(localResult,
            new Invoke(
                new Member(targetType, "Create"),
                [.. createArgs])));

        var createResultType = new NamedTypeReference("DomainResult", TypeArguments: [targetType]);
        bodyNodes.Add(new IfStatement(
            new Syntactic.Not(
                new Member(localResult, "IsSuccess")),
            new Block([
                new Return(
                    new Invoke(
                        new Member(createResultType, "Failure"),
                        new Syntactic.Coalesce(
                            new Member(localResult, "ErrorMessage"),
                            new Constant(""))))
            ])));

        bodyNodes.Add(new Assignment(local, new Member(localResult, "Value")));

        // Target.Create already Attaches via singular nav args (including auto-wired
        // `this` back-ref): peer collections + WhenEach* registries. Do not duplicate
        // _field.Add / Register* / peer Attach when Create wired this collection.
        var (wiredInverse, _) = FindInverseCollectionInfo(entity, targetTypeName);
        var createWiredThisCollection = autoWireBackRef is not null
            && fieldName is not null
            && wiredInverse is not null
            && string.Equals(wiredInverse.Name, rel.Name, StringComparison.Ordinal);

        if (!createWiredThisCollection) {
            if (fieldName is not null) {
                bodyNodes.Add(new Invoke(
                    new Member(
                        new Member(new ThisReference(), fieldName), "Add"),
                    [local]));
            }
            else {
                bodyNodes.Add(new Assignment(
                    new Member(new ThisReference(), pascalName),
                    local));
            }

            // Subscription registration when Create did not Attach this relation.
            if (subscriberSubs is { Count: > 0 }) {
                foreach (var info in subscriberSubs
                    .Where(i => string.Equals(i.Relationship.Name, rel.Name, StringComparison.Ordinal))
                    .GroupBy(s => s.StageName)
                    .Select(g => g.First())) {
                    bodyNodes.Add(new Invoke(
                        new Member(
                            local,
                            SubscriberRegisterMethodName(info)),
                        [new ThisReference()]));
                }
            }
        }

        // A create-in lands the child in its first stage. When a subscription watches
        // that stage, no transition occurs, so no previousStage notify runs. One call
        // here covers both wired and unwired cases.
        if (targetEntity.Stages.Count > 0) {
            var firstStage = targetEntity.Stages[0].Name;
            if (subscriberSubs is { Count: > 0 }
                && subscriberSubs.Any(i =>
                    string.Equals(i.Relationship.Name, rel.Name, StringComparison.Ordinal)
                    && string.Equals(i.StageName, firstStage, StringComparison.Ordinal))) {
                bodyNodes.Add(new Invoke(
                    new Member(local, $"Notify{firstStage}Subscribers"),
                    [new Constant(null)]));
            }
        }

        bodyNodes.Add(new Return(
            new Invoke(
                new Member(
                    new NamedTypeReference("DomainResult", TypeArguments: [targetType]),
                    "Success"),
                local)));

        methods.Add(new MethodDefinitionNode(
            methodName,
            new NamedTypeReference("DomainResult", TypeArguments: [targetType]),
            Parameters: methodParams.Count > 0 ? methodParams : null,
            Body: new Block(bodyNodes, [localResult, local]),
            AccessModifier: AccessModifier.Private
        ));
    }

    /// <summary>
    /// Inverse of a create-in to-one initializer: add an already-constructed child
    /// to this collection and register this entity's subscriptions on it.
    /// </summary>
    private static void AddAttachNavMethod(
        Entity entity,
        Relationship rel,
        List<SubscriptionInfo>? subscriberSubs,
        string fieldName,
        List<MethodDefinitionNode> methods) {
        var targetType = new NamedTypeReference(rel.Target.TypeName);
        var child = new Parameter("child", targetType);
        var body = new List<Node> {
            new Invoke(
                new Member(new Member(new ThisReference(), fieldName), "Add"),
                [child])
        };
        if (subscriberSubs is { Count: > 0 }) {
            foreach (var info in subscriberSubs
                .Where(i => string.Equals(i.Relationship.Name, rel.Name, StringComparison.Ordinal))
                .GroupBy(s => s.StageName)
                .Select(g => g.First())) {
                body.Add(new Invoke(
                    new Member(child, SubscriberRegisterMethodName(info)),
                    [new ThisReference()]));
            }
        }
        methods.Add(new MethodDefinitionNode(
            $"Attach{ToPascalCase(rel.Name)}",
            new TypeReference("void"),
            Parameters: [child],
            Body: new Block(body),
            AccessModifier: AccessModifier.Internal
        ));
    }

    /// <summary>
    /// Public parameters of <c>Create{Nav}</c>: constructor slots the method
    /// asks the caller for. Back-references, the auto-wired peer, and
    /// collections are filled inside the method.
    /// </summary>
    internal static List<ConstructorParameterOrder> CreateNavParameters(
        Entity source, Entity target, INodeMetadataProvider analysis) {
        var slots = new List<ConstructorParameterOrder>();
        var autoWire = FindAutoWireBackReference(target, source.Name);
        foreach (var parameter in GetConstructorParameters(target, analysis)) {
            if (parameter.IsNavigation && parameter.IsBackReference)
                continue;
            if (autoWire is not null
                && string.Equals(parameter.Name, autoWire.Name, StringComparison.Ordinal))
                continue;
            if (parameter.IsCollection)
                continue;
            slots.Add(parameter);
        }
        return slots;
    }

    internal static IReadOnlyList<ConstructorParameterOrder> GetConstructorParameters(
        Entity targetEntity,
        INodeMetadataProvider analysis) {
        if (analysis.GetStructure(targetEntity) is EntityStructureMetadata esm)
            return esm.ConstructorParameters;

        throw new InvalidOperationException(
            $"EntityStructureMetadata is required for constructor ordering on entity '{targetEntity.Name}'.");
    }

    /// <summary>
    /// Trailing optional Create/ctor parameters: defaulted properties that are
    /// not entry-assigned, in the order the Create factory appends them.
    /// </summary>
    internal static List<Property> TrailingDefaultedProperties(
        Entity entity,
        INodeMetadataProvider metadata) {
        var entryAssigned = metadata.GetStructure(entity)?.EntryAssignedPropertyNames
            ?? EntityStructureAnalyzer.ComputeEntryAssignedPropertyNames(entity);
        var list = new List<Property>();
        foreach (var prop in entity.Properties.OrderBy(p => p.Name)) {
            if (!prop.Constraints.OfType<DefaultValueConstraint>().Any())
                continue;
            if (entryAssigned.Contains(prop.Name))
                continue;
            list.Add(prop);
        }
        return list;
    }

    /// <summary>
    /// Null <c>IEnumerable&lt;T&gt;?</c> for an omitted collection create argument.
    /// The printed constructor already coalesces that null into an empty list.
    /// A synthetic <c>new List&lt;T&gt;()</c> is a different collection instance the
    /// VM cannot assign or construct.
    /// </summary>
    internal static Node NullCollectionNode(string elementTypeName) =>
        new TypeCast(
            new Constant(null),
            new OptionalTypeReference(
                new NamedTypeReference("IEnumerable",
                    TypeArguments: [new NamedTypeReference(elementTypeName)])));

    /// <summary>
    /// Builds constraint-validation guard clauses for the <c>Create</c> factory method.
    /// Each constraint on a constructor-parameter property produces an early-return
    /// guard: <c>if (violation) return DomainResult&lt;T&gt;.Failure("'Prop' ...");</c>
    /// Enum-typed properties get a not-any-member guard from the same Member + NotEqual
    /// nodes as assign.
    ///
    /// Only entity properties (not navigation properties) are validated — navs do not
    /// carry constraints in the current domain model. Defaulted props ARE constructor
    /// params (optional overrides), so their constraints are validated too; only
    /// entry-assigned props (body-initialized, never ctor params) are skipped.
    /// </summary>
    private static List<Node> BuildCreateConstraintChecks(
        Entity entity, Domain? domain, IReadOnlySet<string> entryAssignedProps,
        INodeMetadataProvider analysis, IReadOnlyList<Parameter> signature) {

        var checks = new List<Node>();
        var entityTypeRef = new NamedTypeReference(entity.Name);
        var resultType = new NamedTypeReference("DomainResult",
            TypeArguments: [entityTypeRef]);
        var enumProps = GetEnumPropertyNames(entity, domain, analysis);

        foreach (var prop in entity.Properties.OrderBy(p => p.Name)) {
            // Entry-assigned props are body-initialized by the ctor's stage-entry effects
            // — not constructor params, so no guard to attach.
            if (entryAssignedProps.Contains(prop.Name)) continue;

            var paramName = ToCamelCase(prop.Name);
            // The signature parameter, not a same-named copy. A copy has no
            // type, so name.Length does not resolve and the VM rejects Create.
            var paramRef = signature.FirstOrDefault(p =>
                string.Equals(p.Name, paramName, StringComparison.Ordinal));
            if (paramRef is null)
                continue;
            var isText = string.Equals(prop.Type.TypeName, "Text", StringComparison.Ordinal)
                      || string.Equals(prop.Type.TypeName, "String", StringComparison.Ordinal);
            var isNumber = string.Equals(prop.Type.TypeName, "Number", StringComparison.Ordinal)
                        || string.Equals(prop.Type.TypeName, "Int", StringComparison.Ordinal);

            Node Failure(string msg) => FailureNode(new Constant(msg));
            Node FailureNode(Node msg) => new Return(
                new Invoke(
                    new Member(resultType, "Failure"),
                    msg));

            foreach (var constraint in prop.Constraints) {
                switch (constraint) {
                    case RequiredConstraint:
                        // Required: skip for value types (Number, Boolean,
                        // DateTime, etc.) — they can never be null at runtime.
                        // Only Text/String and entity reference types benefit
                        // from runtime required checks.
                        if (isText) {
                            checks.Add(new IfStatement(
                                new Invoke(
                                    new Member(
                                        new PrimitiveTypeReference(PrimType.String),
                                        "IsNullOrEmpty"),
                                    [paramRef]),
                                new Block([Failure(
                                    $"'{prop.Name}' is required.")])));
                        }
                        else if (IsNullableDomainType(prop.Type.TypeName, domain)) {
                            checks.Add(new IfStatement(
                                new Equal(paramRef, new Constant(null)),
                                new Block([Failure(
                                    $"'{prop.Name}' is required.")])));
                        }
                        break;

                    case RangeConstraint r:
                        if (isNumber && r.Minimum is not null) {
                            var minVal = ConvertToConstant(r.Minimum);
                            if (minVal is not null) {
                                checks.Add(new IfStatement(
                                    new LessThan(paramRef, minVal),
                                    new Block([Failure(
                                        $"'{prop.Name}' must be >= {EffectLoweringPass.FormatConstraintValue(r.Minimum)}.")])));
                            }
                        }
                        if (isNumber && r.Maximum is not null) {
                            var maxVal = ConvertToConstant(r.Maximum);
                            if (maxVal is not null) {
                                checks.Add(new IfStatement(
                                    new GreaterThan(paramRef, maxVal),
                                    new Block([Failure(
                                        $"'{prop.Name}' must be <= {EffectLoweringPass.FormatConstraintValue(r.Maximum)}.")])));
                            }
                        }
                        break;

                    case LengthConstraint l:
                        if (isText) {
                            var lenAccess = new Member(paramRef, "Length");
                            if (l.MinLength > 0) {
                                checks.Add(new IfStatement(
                                    new LessThan(lenAccess,
                                        new Constant((long)l.MinLength)),
                                    new Block([Failure(
                                        $"'{prop.Name}' must be at least {l.MinLength} characters.")])));
                            }
                            if (l.MaxLength < int.MaxValue) {
                                checks.Add(new IfStatement(
                                    new GreaterThan(lenAccess,
                                        new Constant((long)l.MaxLength)),
                                    new Block([Failure(
                                        $"'{prop.Name}' must be at most {l.MaxLength} characters.")])));
                            }
                        }
                        break;

                    case PatternConstraint p:
                        if (isText) {
                            checks.Add(new IfStatement(
                                new Syntactic.Not(
                                    new Invoke(
                                        new Member(
                                            new NamedTypeReference(
                                                "System.Text.RegularExpressions.Regex"),
                                            "IsMatch"),
                                        [paramRef,
                                         new Constant(p.Pattern)])),
                                new Block([Failure(
                                    $"'{prop.Name}' does not match the required pattern.")])));
                        }
                        break;

                    case EqualityConstraint eq:
                        if (eq.ExpectedValue is not null) {
                            checks.Add(new IfStatement(
                                new NotEqual(paramRef,
                                    new Constant(eq.ExpectedValue)),
                                new Block([Failure(
                                    $"'{prop.Name}' must equal {eq.ExpectedValue}.")])));
                        }
                        break;

                        // DefaultValueConstraint is not a factory If:
                        // defaulted props are optional params; the ctor applies the default.
                }
            }

            if (enumProps is not null
                && enumProps.TryGetValue(prop.Name, out var enumTypeName)
                && TryResolveEnumType(domain, analysis, enumTypeName, out var enumType)
                && enumType is { MemberNames.Count: > 0 }) {
                // No default(...) — the property is required at create (guide §3). The
                // VM factory slot is null when the caller omits it; ReferenceEquals
                // prints for a value-type param (boxing) and is true only for that slot.
                if (paramRef.DefaultValue is null) {
                    checks.Add(new IfStatement(
                        new Invoke(
                            new Member(new ClrTypeReference(typeof(object)), "ReferenceEquals"),
                            [paramRef, new Constant(null)]),
                        new Block([Failure($"'{prop.Name}' is required.")])));
                }
                Node? notMember = null;
                var enumTypeRef = new NamedTypeReference(enumType.Name);
                foreach (var member in enumType.MemberNames) {
                    var ne = new NotEqual(paramRef, new Member(enumTypeRef, member));
                    notMember = notMember is null ? ne : new Syntactic.And(notMember, ne);
                }
                checks.Add(new IfStatement(
                    notMember!,
                    new Block([FailureNode(
                        InvalidEnumMemberMessage(paramRef, enumType.Name, prop.Name))])));
            }
        }

        return checks;
    }

    /// <summary>
    /// <c>'Gold' is not a valid member of Tier for property 'Level'.</c> — a runtime
    /// <c>string.Concat</c> so the offending value is named, in the same lowered
    /// tree the C# factory prints and the VM create path executes.
    /// </summary>
    private static Node InvalidEnumMemberMessage(Node value, string enumName, string propName) =>
        new Invoke(
            new Member(new PrimitiveTypeReference(PrimType.String), "Concat"),
            [
                new Constant("'"),
                value,
                new Constant($"' is not a valid member of {enumName} for property '{propName}'.")
            ]);

    /// <summary>Converts a constraint value object to a Syntax Constant.</summary>
    private static Constant? ConvertToConstant(object? value) {
        if (value is null) return null;
        if (value is long l) return new Constant(l);
        if (value is int i) return new Constant((long)i);
        if (value is double d) return d == Math.Floor(d)
            ? new Constant((long)d)
            : new Constant(d);
        if (value is decimal m) return new Constant((double)m);
        if (value is string s) return new Constant(s);
        if (value is bool b) return new Constant(b);
        return new Constant(value?.ToString());
    }

    /// <summary>
    /// Returns true if the domain type name maps to a nullable CLR type
    /// (string or reference type), meaning null-check validation applies.
    /// Value types (Number, Boolean, DateTime, etc.) always have a value
    /// and cannot be null at runtime.
    /// </summary>
    private static bool IsNullableDomainType(string typeName, Domain? domain = null) {
        // Enum types are C# value types — never null at runtime.
        if (domain is not null
            && domain.Types.OfType<EnumType>().Any(e => string.Equals(e.Name, typeName, StringComparison.Ordinal)))
            return false;
        return !DomainTypeMapping.IsNonNullableClrValueType(
            RuntimeAnalysisCache.ClrTypeName(domain, typeName));
    }
}