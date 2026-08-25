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
                                        $"Register{info.SourceEntity.Name}{info.StageName}Subscriber"),
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
                                $"Register{info.SourceEntity.Name}{info.StageName}Subscriber"),
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
    /// public Loan CreateLoans(Book book, string status, ...)
    /// {
    ///     var loan = Loan.Create(book: book, borrower: this, status: status, ...);
    ///     _loans.Add(loan);
    ///     loan.RegisterPatronOverdueSubscriber(this);
    ///     return loan;
    /// }
    /// </code>
    /// </summary>
    private static void AddCreateNavMethod(
        Entity entity,
        Relationship rel,
        Domain domain,
        List<SubscriptionInfo>? subscriberSubs,
        string fieldName,
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
                createArgs.Add(new ThisReference());
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
            var propRef = parameter.IsNavigation
                ? (Node)new OptionalTypeReference(mapped)
                : mapped;
            methodParams.Add(new Parameter(paramName, propRef));
            createArgs.Add(new Parameter(paramName));
        }

        // NOTE: properties with DefaultValueConstraint are NOT included in
        // the Target.Create() parameter list — they are set directly in the
        // constructor body from their default expression. Do NOT append them
        // to createArgs here; the Create factory already handles them.

        // Defaulted props of the target become TRAILING optional method params (C#
        // optional params must be last) and are forwarded to Target.Create — so a
        // `create in { DefaultedProp: value }` override flows through construction.
        foreach (var prop in targetEntity.Properties.OrderBy(p => p.Name)) {
            var defaultConstraint = prop.Constraints.OfType<DefaultValueConstraint>().FirstOrDefault();
            if (defaultConstraint is null) continue;

            var paramName = ToCamelCase(prop.Name);
            var mapped = MapDomainTypeRef(prop.Type, domain, metadata);
            var runtimeExpr = EffectLoweringPass.LowerDefaultExpression(
                defaultConstraint.Expression, new NamedTypeReference(prop.Type.TypeName));
            if (runtimeExpr is not null) {
                methodParams.Add(new Parameter(paramName,
                    new OptionalTypeReference(mapped),
                    DefaultValue: new Constant(null)));
            }
            else {
                methodParams.Add(new Parameter(paramName, mapped,
                    DefaultValue: LowerDefaultConstantNode(defaultConstraint, prop, domain, metadata)));
            }
            createArgs.Add(new Parameter(paramName));
        }

        var bodyNodes = new List<Node>();
        var localResultName = $"{ToCamelCase(targetTypeName)}Result";
        var localName = ToCamelCase(targetTypeName);

        // var loanResult = Loan.Create(book: book, borrower: this, ...);
        bodyNodes.Add(new Variable(localResultName,
            new Invoke(
                new Member(targetType, "Create"),
                [.. createArgs])));

        // Unwrap: the Create factory now returns DomainResult<T>, and it may
        // reject invalid inputs via constraint checks. Since CreateNav methods
        // are only called from action bodies with controlled defaults, a
        // failure here is a programmer error — assert fast.
        bodyNodes.Add(new IfStatement(
            new Syntactic.Not(
                new Member(
                    new Variable(localResultName), "IsSuccess")),
            new Block([
                new ThrowStatement(
                    new New(
                        new NamedTypeReference("InvalidOperationException"),
                        new Member(
                            new Variable(localResultName), "ErrorMessage")))
            ])));

        // var loan = loanResult.Value;
        bodyNodes.Add(new Variable(localName,
            new Member(
                new Variable(localResultName), "Value")));

        // _loans.Add(loan);
        bodyNodes.Add(new Invoke(
            new Member(
                new Member(new ThisReference(), fieldName), "Add"),
            [new Variable(localName)]));

        // Subscription registration: loan.RegisterPatronOverdueSubscriber(this)
        // One registration per stage — multiple subscriptions on the same relation+stage
        // (any/all/Each) share the target's single registry list.
        if (subscriberSubs is { Count: > 0 }) {
            foreach (var info in subscriberSubs
                .Where(i => string.Equals(i.Relationship.Name, rel.Name, StringComparison.Ordinal))
                .GroupBy(s => s.StageName)
                .Select(g => g.First())) {
                bodyNodes.Add(new Invoke(
                    new Member(
                        new Variable(localName),
                        $"Register{info.SourceEntity.Name}{info.StageName}Subscriber"),
                    [new ThisReference()]));
            }
        }

        // return loan;
        bodyNodes.Add(new Return(new Variable(localName)));

        methods.Add(new MethodDefinitionNode(
            methodName,
            targetType,
            Parameters: methodParams.Count > 0 ? methodParams : null,
            Body: new Block(bodyNodes),
            AccessModifier: AccessModifier.Private
        ));
    }

    private static IReadOnlyList<ConstructorParameterOrder> GetConstructorParameters(
        Entity targetEntity,
        INodeMetadataProvider analysis) {
        if (analysis.GetStructure(targetEntity) is EntityStructureMetadata esm)
            return esm.ConstructorParameters;

        throw new InvalidOperationException(
            $"EntityStructureMetadata is required for constructor ordering on entity '{targetEntity.Name}'.");
    }

    /// <summary>
    /// Builds constraint-validation guard clauses for the <c>Create</c> factory method.
    /// Each constraint on a constructor-parameter property produces an early-return
    /// guard: <c>if (violation) return DomainResult&lt;T&gt;.Failure("'Prop' ...");</c>
    ///
    /// Only entity properties (not navigation properties) are validated — navs do not
    /// carry constraints in the current domain model. Defaulted props ARE constructor
    /// params (optional overrides), so their constraints are validated too; only
    /// entry-assigned props (body-initialized, never ctor params) are skipped.
    /// </summary>
    private static List<Node> BuildCreateConstraintChecks(
        Entity entity, Domain? domain, IReadOnlySet<string> entryAssignedProps) {

        var checks = new List<Node>();
        var entityTypeRef = new NamedTypeReference(entity.Name);
        var resultType = new NamedTypeReference("DomainResult",
            TypeArguments: [entityTypeRef]);

        foreach (var prop in entity.Properties.OrderBy(p => p.Name)) {
            // Entry-assigned props are body-initialized by the ctor's stage-entry effects
            // — not constructor params, so no guard to attach.
            if (entryAssignedProps.Contains(prop.Name)) continue;

            var paramName = ToCamelCase(prop.Name);
            var paramRef = new Parameter(paramName);
            var isText = string.Equals(prop.Type.TypeName, "Text", StringComparison.Ordinal)
                      || string.Equals(prop.Type.TypeName, "String", StringComparison.Ordinal);
            var isNumber = string.Equals(prop.Type.TypeName, "Number", StringComparison.Ordinal)
                        || string.Equals(prop.Type.TypeName, "Int", StringComparison.Ordinal);

            Node Failure(string msg) => new Return(
                new Invoke(
                    new Member(resultType, "Failure"),
                    new Constant(msg)));

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
                                        new NamedTypeReference("string"),
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
                                        $"'{prop.Name}' must be >= {FormatConstraintValue(r.Minimum)}.")])));
                            }
                        }
                        if (isNumber && r.Maximum is not null) {
                            var maxVal = ConvertToConstant(r.Maximum);
                            if (maxVal is not null) {
                                checks.Add(new IfStatement(
                                    new GreaterThan(paramRef, maxVal),
                                    new Block([Failure(
                                        $"'{prop.Name}' must be <= {FormatConstraintValue(r.Maximum)}.")])));
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

                        // DefaultValueConstraint and UniqueConstraint
                        // are not validated at factory time:
                        //   • Default → already handled (only non-default props are params)
                        //   • Unique  → requires store awareness
                }
            }
        }

        return checks;
    }

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

    /// <summary>Formats a constraint boundary value for error messages.</summary>
    private static string FormatConstraintValue(object? value) => value switch {
        null => "?",
        double d => d == Math.Floor(d) ? d.ToString("F0") : d.ToString("G"),
        _ => value.ToString() ?? "?"
    };

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
        return typeName switch {
            "Text" or "String" => true,  // handled separately via IsNullOrEmpty
            "Number" or "Int" or "Int64" or "Int32" => false,
            "Boolean" or "Bool" => false,
            "DateTime" or "Timestamp" => false,
            "Date" or "DateOnly" => false,
            "Time" or "TimeOnly" => false,
            "Duration" or "TimeSpan" => false,
            "Decimal" => false,
            "Float" or "Double" => false,
            "Guid" or "Uuid" => false,
            _ => true, // entity reference types (Book, Patron, etc.) are nullable
        };
    }
}
