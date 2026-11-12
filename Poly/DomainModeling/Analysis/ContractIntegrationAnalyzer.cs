using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Contract;

using Action = Poly.DomainModeling.Ontology.Action;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Imported-contract / binding facts and checks, plus exported-surface closure checks.
/// Writes two facts the rest of the pipeline reads: <see cref="ContractSurfaceMetadata"/> on
/// each contract (the resolved surface, its fingerprint, and the pin outcome) and
/// <see cref="ContractBindingMetadata"/> on each bind (the resolved endpoint identity and
/// status). Lower splices or erases the recorded identity; it does not resolve again.
/// </summary>
internal sealed class ContractIntegrationAnalyzer : INodeAnalyzer {
    public const string Id = "DomainContractIntegrationAnalyzer";
    public string PassName => Id;
    public void Analyze(AnalysisContext context, Node node) {

        if (node is Domain domain) {
            ValidateDomain(context, domain);
            return;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void ValidateDomain(AnalysisContext context, Domain domain) {
        ReportTypeNameClashes(context, domain);

        foreach (var contract in domain.ImportedContracts) {
            ValidateContract(context, domain, contract);
        }

        foreach (var entity in domain.Types.OfType<Entity>()) {
            foreach (var prop in entity.Properties) {
                if (FindContractOwningType(domain, prop.Type.TypeName) is { } owner) {
                    context.ReportError(
                        prop,
                        $"Entity '{entity.Name}' property '{prop.Name}' is typed as contract value type '{prop.Type.TypeName}' from '{owner.Name}'. Stored state must use parent-domain types; use the contract type only on a bound action parameter.",
                        DomainModelDiagnosticCodes.ContractIntegration);
                }
            }
        }

        ValidateBindScope(context, domain);

        foreach (var binding in domain.ContractBindings) {
            ValidateBinding(context, domain, binding);
        }

        ValidatePublicSurface(context, domain);
    }

    /// <summary>
    /// An exported signature must be closed (decision 12, #417): a public action's parameter
    /// and result types and a public value type's field types may not name a type internal to
    /// this domain. A public action must also be reachable through public (#418): its owning
    /// entity must be public, because a public entity exports its name and its public actions.
    /// A public entity may navigate to an internal entity (L-22b): the navigation prints <c>internal</c> and the
    /// pack maps it explicitly. Still an error (<see cref="ValidatePublicEntityInternalTargets"/>): an entity-typed
    /// property, and a many-to-many navigation to an internal entity (until row L-22d).
    /// Not checked: a public entity's properties (decision 13 exports its name and
    /// public actions only), an imported contract's ACL value types (that contract's surface),
    /// primitives, and enums (no visibility mark; #418 gives them a digest entry).
    /// </summary>
    private static void ValidatePublicSurface(AnalysisContext context, Domain domain) {
        foreach (var valueType in domain.Types.OfType<ValueType>()) {
            if (valueType.Visibility != Visibility.Public)
                continue;
            foreach (var property in valueType.Properties) {
                ReportUnclosedSignature(
                    context, domain, valueType, property.Type,
                    $"Public value type '{valueType.Name}'",
                    $"in property '{property.Name}'");
            }
        }

        foreach (var entity in domain.Types.OfType<Entity>()) {
            if (entity.Visibility == Visibility.Public)
                ValidatePublicEntityInternalTargets(context, domain, entity);
            if (entity.Visibility == Visibility.Public)
                ValidatePublicStageGroups(context, domain, entity);
            foreach (var action in entity.Actions) {
                if (action.Visibility == Visibility.Public)
                    ValidatePublicAction(context, domain, entity, action);
            }
            foreach (var stage in entity.Stages) {
                foreach (var action in stage.Actions) {
                    if (action.Visibility == Visibility.Public)
                        ValidatePublicAction(context, domain, entity, action);
                }
            }
        }
    }

    /// <summary>
    /// L-22 (#604): stage copies of one action name print as ONE method that is public when any copy is
    /// public. A non-public copy of such a group whose result names an internal type would therefore be
    /// exported through the public method, so it is reported on that copy like any public signature.
    /// (Parameter lists must agree across copies, so the public copy's own check already covers them.)
    /// </summary>
    private static void ValidatePublicStageGroups(AnalysisContext context, Domain domain, Entity entity) {
        var variants = entity.Actions.Concat(entity.Stages.SelectMany(s => s.Actions));
        foreach (var group in variants.GroupBy(a => a.Name, StringComparer.Ordinal)) {
            if (!group.Any(a => a.Visibility == Visibility.Public))
                continue;

            foreach (var variant in group.Where(a => a.Visibility != Visibility.Public)) {
                foreach (var member in variant.Result.Members) {
                    ReportUnclosedSignature(
                        context, domain, variant, member.Type,
                        $"Public action '{variant.Name}' on entity '{entity.Name}' (one method across its stage copies)",
                        "as the result of a stage copy");
                }
            }
        }
    }

    /// <summary>
    /// L-22 (#604): an entity-typed property (the DSL reads one as a navigation; the model API can build one) of a
    /// public entity names an internal entity. Persistence ignores such a property, so demoting it would lose its data
    /// silently, and printing it public is CS0053: an error (not acknowledgeable), positioned on the property.
    /// </summary>
    private static void ValidatePublicEntityInternalTargets(AnalysisContext context, Domain domain, Entity entity) {
        // A many-to-many navigation to an internal entity prints internal, but the pack's explicit mapping cannot
        // prove an EF skip navigation over a non-public member, so it fails closed rather than map it as one-to-many.
        foreach (var navigation in entity.Navigations) {
            if (navigation.Cardinality is not RelationshipCardinality.ManyToMany
                || context.GetMetadata<ResolvedTypeReferenceMetadata>(navigation.Target)
                    is not ResolvedTypeReferenceMetadata { Type: Entity target }
                || !IsDeclaredInDomain(domain, target)
                || target.Visibility == Visibility.Public)
                continue;

            context.ReportError(
                navigation,
                $"Public entity '{entity.Name}' many-to-many navigation '{navigation.Name}' targets internal entity " +
                $"'{target.Name}'; that mapping is not supported. Mark '{target.Name}' public, or remove the public mark from '{entity.Name}'.",
                DomainModelDiagnosticCodes.PublicEntityPropertyTypedInternalEntity);
        }

        foreach (var property in entity.Properties) {
            var target = context.GetMetadata<ResolvedTypeReferenceMetadata>(property.Type)?.Type as Entity
                ?? domain.Types.OfType<Entity>().FirstOrDefault(e => e.Name == property.Type.TypeName);
            if (target is null || !IsDeclaredInDomain(domain, target) || target.Visibility == Visibility.Public)
                continue;

            context.ReportError(
                property,
                $"Public entity '{entity.Name}' property '{property.Name}' is typed as internal entity " +
                $"'{target.Name}'. Mark '{target.Name}' public, or remove the public mark from '{entity.Name}'.",
                DomainModelDiagnosticCodes.PublicEntityPropertyTypedInternalEntity);
        }
    }

    private static void ValidatePublicAction(
        AnalysisContext context,
        Domain domain,
        Entity entity,
        Action action) {
        if (entity.Visibility != Visibility.Public) {
            ReportContractFinding(
                context, domain, action,
                DomainModelDiagnosticCodes.PublicMemberOnInternalOwner,
                $"Public action '{action.Name}' on entity '{entity.Name}' is not reachable: " +
                $"entity '{entity.Name}' is internal. Mark '{entity.Name}' public, or remove " +
                $"the public mark from '{action.Name}'.");
            return;
        }

        var subject = $"Public action '{action.Name}' on entity '{entity.Name}'";

        foreach (var parameter in action.Parameters) {
            ReportUnclosedSignature(
                context, domain, action, parameter.Type,
                subject, $"in parameter '{parameter.Name}'");
        }

        foreach (var member in action.Result.Members) {
            ReportUnclosedSignature(
                context, domain, action, member.Type,
                subject, "as its result");
        }
    }

    /// <summary>
    /// Reports one finding when <paramref name="typeReference"/> resolves to a type this
    /// domain declares with <see cref="Visibility.Internal"/>. An unresolved reference is
    /// skipped: <see cref="DomainCatalogPass"/> already reported the unknown type.
    /// </summary>
    private static void ReportUnclosedSignature(
        AnalysisContext context,
        Domain domain,
        Node reportNode,
        DomainTypeReference typeReference,
        string subject,
        string position) {
        if (context.GetMetadata<ResolvedTypeReferenceMetadata>(typeReference)
            is not ResolvedTypeReferenceMetadata resolved)
            return;

        if (!IsDeclaredInDomain(domain, resolved.Type))
            return;

        var isPublic = resolved.Type switch {
            Entity entity => entity.Visibility == Visibility.Public,
            ValueType valueType => valueType.Visibility == Visibility.Public,
            _ => true
        };
        if (isPublic)
            return;

        var kind = resolved.Type is Entity ? "entity" : "value";
        ReportContractFinding(
            context, domain, reportNode,
            DomainModelDiagnosticCodes.PublicSurfaceNotClosed,
            $"{subject} names internal {kind} type '{resolved.Type.Name}' {position}. " +
            $"Mark '{resolved.Type.Name}' public, or remove the public mark.");
    }

    /// <summary>
    /// Reference identity, not record equality: a contract ACL value type with the same shape
    /// as a domain value type must not count as declared here.
    /// </summary>
    private static bool IsDeclaredInDomain(Domain domain, DomainType type) =>
        domain.Types.Any(candidate => ReferenceEquals(candidate, type));

    private static void ReportTypeNameClashes(AnalysisContext context, Domain domain) {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var t in domain.Types.OfType<ValueType>())
            seen[t.Name] = $"domain value type '{t.Name}'";
        foreach (var t in domain.Types.OfType<EnumType>())
            seen[t.Name] = $"enum '{t.Name}'";
        foreach (var t in domain.Types.OfType<Entity>())
            seen[t.Name] = $"entity '{t.Name}'";

        foreach (var contract in domain.ImportedContracts) {
            foreach (var vt in contract.Types) {
                if (seen.TryGetValue(vt.Name, out var prior)) {
                    context.ReportError(
                        vt,
                        $"Value type '{vt.Name}' on contract '{contract.Name}' clashes with {prior}.",
                        DomainModelDiagnosticCodes.ContractIntegration);
                }
                else {
                    seen[vt.Name] = $"contract '{contract.Name}' value type '{vt.Name}'";
                }
            }
        }
    }

    private static ImportedContract? FindContractOwningType(Domain domain, string typeName) {
        foreach (var contract in domain.ImportedContracts) {
            if (contract.Types.Any(t => string.Equals(t.Name, typeName, StringComparison.Ordinal)))
                return contract;
        }
        return null;
    }

    private static bool PayloadTypeExists(Domain domain, ImportedContract contract, string typeName) {
        if (typeName is "Text" or "Number" or "Boolean")
            return true;
        if (contract.Types.Any(t => string.Equals(t.Name, typeName, StringComparison.Ordinal)))
            return true;
        return domain.Types.Any(t => string.Equals(t.Name, typeName, StringComparison.Ordinal));
    }

    private static void ValidateContract(AnalysisContext context, Domain domain, ImportedContract contract) {
        var pin = contract.Pin;
        var pinKind = ContractPin.KindOf(pin);
        var pinMatched = pin is null || ContractPin.Matches(contract, pin);
        context.SetMetadata(contract, new ContractSurfaceMetadata(
            contract.UsesId,
            contract.Name,
            contract.SourceIdentifier,
            contract.Version,
            contract.Fingerprint,
            pin,
            pinKind,
            pinMatched));

        if (string.IsNullOrWhiteSpace(contract.SourceIdentifier)) {
            context.ReportError(
                contract,
                $"Imported contract '{contract.Name}' is missing a source identifier.",
                DomainModelDiagnosticCodes.ContractIntegration);
        }

        if (string.IsNullOrWhiteSpace(contract.Version)) {
            context.ReportError(
                contract,
                $"Imported contract '{contract.Name}' is missing a version.",
                DomainModelDiagnosticCodes.ContractIntegration);
        }

        ValidatePlatformTarget(context, domain, contract);

        if (pin is not null && !pinMatched) {
            var pinnedBy = contract.UsesId is null ? "on the contract" : $"on `uses {contract.UsesId}`";
            ReportContractFinding(
                context, domain, contract,
                DomainModelDiagnosticCodes.ContractPinMismatch,
                $"Contract '{contract.Name}' is pinned to '{pin}' {pinnedBy}, but the loaded surface is " +
                $"version '{contract.Version}' with fingerprint '{contract.Fingerprint}'. A pin is the " +
                "public surface only; re-pin to the loaded surface.");
        }

        foreach (var endpoint in contract.Endpoints) {
            if (!PayloadTypeExists(domain, contract, endpoint.PayloadType.TypeName)) {
                context.ReportError(
                    endpoint,
                    $"Contract '{contract.Name}' endpoint '{endpoint.Name}' payload type '{endpoint.PayloadType.TypeName}' is not a primitive or a value type on that contract (or the parent domain).",
                    DomainModelDiagnosticCodes.ContractIntegration);
            }
        }
    }

    /// <summary>
    /// A seeded contract whose package declares host targets must match the host target named
    /// on the session (spike #413 decision 3, #424). The target is a host fact: <c>uses posix</c>
    /// names the package, never the OS it runs on. An absent host target is a refusal too, not a
    /// skip: a platform package cannot be proven sound on an unnamed target. Read only for a
    /// domain that has a seeded contract, so a standalone pass over a contract-free tree never
    /// reaches the session fallback.
    /// </summary>
    private static void ValidatePlatformTarget(
        AnalysisContext context, Domain domain, ImportedContract contract) {
        if (contract.UsesId is null)
            return;

        var session = RuntimeAnalysisCache.Session(domain);
        var targets = session.FindContractContribution(contract.UsesId)?.Targets;
        if (targets is not { Count: > 0 })
            return;

        var declared = string.Join(", ", targets.Select(target => $"'{target}'"));
        if (session.Target is null) {
            ReportContractFinding(
                context, domain, contract,
                DomainModelDiagnosticCodes.PlatformTargetMismatch,
                $"Contract '{contract.Name}' comes from library '{contract.UsesId}', which is for host target {declared}, but the host named no target. Pass the host target for this compile; 'uses {contract.UsesId}' does not choose it.");
            return;
        }

        if (targets.Contains(session.Target, StringComparer.Ordinal))
            return;

        ReportContractFinding(
            context, domain, contract,
            DomainModelDiagnosticCodes.PlatformTargetMismatch,
            $"Contract '{contract.Name}' comes from library '{contract.UsesId}', which is for host target {declared}; the host target is '{session.Target}'. Pass the host target this compilation is for; 'uses {contract.UsesId}' does not choose it.");
    }

    /// <summary>
    /// #602, #603: the bind scope. A bind name is a domain-level name, so it must be unique:
    /// diagnostics and binding facts are keyed by it, and the printer emits a duplicate twice.
    /// Lower splices one endpoint body per bound action, so a second bind on that action is
    /// dropped without a word. Both refusals are errors positioned on the second declaration,
    /// naming the first, so the lowered module never disagrees with what the author wrote.
    /// </summary>
    private static void ValidateBindScope(AnalysisContext context, Domain domain) {
        var names = new Dictionary<string, ContractBinding>(StringComparer.Ordinal);
        var actions = new Dictionary<string, ContractBinding>(StringComparer.Ordinal);

        foreach (var binding in domain.ContractBindings) {
            if (names.TryGetValue(binding.Name, out var sameName)) {
                context.ReportError(
                    binding,
                    $"Contract binding '{binding.Name}' ({DescribeBinding(binding)}) reuses the name of the earlier bind '{sameName.Name}' ({DescribeBinding(sameName)}). Each bind name must be unique; rename one of them.",
                    DomainModelDiagnosticCodes.ContractBindDuplicateName);
            }
            else {
                names[binding.Name] = binding;
            }

            if (actions.TryGetValue(binding.ActionName, out var boundFirst)) {
                context.ReportError(
                    binding,
                    $"Action '{binding.ActionName}' is bound twice: '{boundFirst.Name}' ({DescribeBinding(boundFirst)}, declared first) and '{binding.Name}' ({DescribeBinding(binding)}). Lower splices one endpoint body and drops the other; remove or retarget one bind.",
                    DomainModelDiagnosticCodes.ContractBindActionAlreadyBound);
            }
            else {
                actions[binding.ActionName] = binding;
            }
        }
    }

    private static string DescribeBinding(ContractBinding binding) =>
        $"endpoint '{binding.EndpointName}' to action '{binding.ActionName}'";

    private static void ValidateBinding(AnalysisContext context, Domain domain, ContractBinding binding) {
        var contract = domain.ImportedContracts.FirstOrDefault(c =>
            string.Equals(c.Name, binding.ContractName, StringComparison.Ordinal));
        if (contract is null) {
            RecordBinding(context, binding, contract: null, ContractBindingStatus.Unresolved, endpoint: null);
            ReportContractFinding(
                context, domain, binding,
                DomainModelDiagnosticCodes.ContractReferenceUnresolved,
                $"Contract binding '{binding.Name}' references imported contract '{binding.ContractName}' that is not registered in the domain.");
            return;
        }

        // Before the action lookup: a missing endpoint keeps its historical precedence over a
        // missing action in the diagnostic. The resolver repeats the same match below.
        if (!HasEndpointCandidate(contract, binding.EndpointName)) {
            RecordBinding(context, binding, contract, ContractBindingStatus.NotOnSurface, endpoint: null);
            ReportContractFinding(
                context, domain, binding,
                DomainModelDiagnosticCodes.ContractEndpointNotOnSurface,
                $"Contract binding '{binding.Name}' references endpoint '{binding.EndpointName}' that does not belong to contract '{binding.ContractName}'{PinSuffix(contract)}.");
            return;
        }

        var action = DomainAnalysis.FindAnyNamedActionOnAnyEntity(domain, binding.ActionName);
        if (action is null) {
            RecordBinding(context, binding, contract, ContractBindingStatus.Unresolved, endpoint: null);
            context.ReportError(
                binding,
                $"Contract binding '{binding.Name}' references action '{binding.ActionName}' that was not found on any entity in the domain.",
                DomainModelDiagnosticCodes.ContractIntegration);
            return;
        }

        // One bind covers every variant of the name (entity-level and each stage). If those
        // variants disagree on a parameter's name or type, there is no single signature to
        // bind; first-wins would splice a body whose payload the other stage never accepted.
        if (DisagreeingSignatures(domain, binding.ActionName) is { } signatures) {
            RecordBinding(context, binding, contract, ContractBindingStatus.Unresolved, endpoint: null);
            context.ReportError(
                binding,
                $"Contract binding '{binding.Name}' references action '{binding.ActionName}', whose variants do not share one parameter list (name and type): {signatures}. A bind needs one signature across stage variants.",
                DomainModelDiagnosticCodes.ContractIntegration);
            return;
        }

        var localParam = action.Parameters.FirstOrDefault(p =>
            string.Equals(p.Name, binding.LocalParameterName, StringComparison.Ordinal));
        if (localParam is null) {
            RecordBinding(context, binding, contract, ContractBindingStatus.Unresolved, endpoint: null);
            context.ReportError(
                binding,
                $"Contract binding '{binding.Name}' references missing local parameter '{binding.LocalParameterName}' on action '{binding.ActionName}'.",
                DomainModelDiagnosticCodes.ContractIntegration);
            return;
        }

        var resolution = ContractEndpointResolver.Resolve(
            contract, domain, binding.EndpointName, localParam.Type.TypeName);
        var classified = ClassifyResolution(
            context, domain, binding, $"Contract binding '{binding.Name}'", contract, resolution, localParam);
        var boundary = classified.Boundary;
        var boundaryEndpoint = classified.Endpoint;
        RecordBinding(context, binding, contract, classified.Status, boundaryEndpoint?.Name);
        if (boundary is not null)
            context.SetMetadata(binding, boundary);

        switch (resolution.Status) {
            case ContractBindingStatus.Incompatible when boundary is null:
                context.ReportError(
                    binding,
                    $"Contract binding '{binding.Name}' parameter '{localParam.Name}' type '{localParam.Type.TypeName}' is incompatible with {DescribeCandidates(resolution.Candidates)} on contract '{contract.Name}'.",
                    DomainModelDiagnosticCodes.ContractIntegration);
                break;

            case ContractBindingStatus.Ambiguous:
                context.ReportError(
                    binding,
                    $"Contract binding '{binding.Name}' names '{binding.EndpointName}', which matches {DescribeCandidates(resolution.Candidates)} and no payload is more constrained than the others. Name the exported endpoint instead.",
                    DomainModelDiagnosticCodes.ContractOverloadAmbiguous);
                break;

            case ContractBindingStatus.Bound when resolution.RequiredChoice:
                context.ReportWarning(
                    binding,
                    $"Contract binding '{binding.Name}' names '{binding.EndpointName}', which matches {DescribeCandidates(resolution.Candidates)}. Selected '{resolution.Endpoint!.Name}' as the more constrained payload; name the exported endpoint to make the choice explicit.",
                    DomainModelDiagnosticCodes.ContractOverloadSelected);
                break;
        }

        if (boundaryEndpoint is { } payloadOwner
            && !PayloadTypeExists(domain, contract, payloadOwner.PayloadType.TypeName)) {
            context.ReportError(
                payloadOwner,
                $"Contract '{contract.Name}' endpoint '{payloadOwner.Name}' payload type '{payloadOwner.PayloadType.TypeName}' is not a primitive or a value type on that contract (or the parent domain).",
                DomainModelDiagnosticCodes.ContractIntegration);
        }

        foreach (var map in binding.FieldMaps) {
            if (string.IsNullOrWhiteSpace(map.RemoteFieldName) || string.IsNullOrWhiteSpace(map.LocalFieldName)) {
                context.ReportError(
                    binding,
                    $"Contract binding '{binding.Name}' has a field map with empty remote or local field name.",
                    DomainModelDiagnosticCodes.ContractIntegration);
            }
        }
    }

    /// <summary>
    /// The post-resolve classification shared by a bind and an expression call (#426): the pin
    /// check, the #425 boundary check (which reports DMCON014/DMCON015 on <paramref name="site"/>),
    /// and the status mapping. The bind and the call differ only in how they report a plain
    /// incompatibility, an ambiguity and a name that is not on the surface.
    /// </summary>
    internal static ContractClassification ClassifyResolution(
        AnalysisContext context,
        Domain domain,
        Node site,
        string subject,
        ImportedContract contract,
        ContractEndpointResolution resolution,
        Property localParameter) {
        var pinMismatch = contract.Pin is not null && !ContractPin.Matches(contract, contract.Pin);

        // #425: a same-shape constraint mismatch is a boundary verdict, not a generic
        // incompatibility. The resolver refuses a wider caller; the boundary check decides
        // whether that is a violation (DMCON014) or cannot-decide (DMCON015). A shape
        // mismatch stays Incompatible: there is no constraint pair to compare.
        var boundaryEndpoint = resolution.Endpoint
            ?? (resolution.Status == ContractBindingStatus.Incompatible && resolution.Candidates.Count == 1
                ? resolution.Candidates[0]
                : null);
        var boundary = boundaryEndpoint is not null
                       && ContractPayloadRelation.SameShape(
                           contract, domain, localParameter.Type.TypeName, boundaryEndpoint.PayloadType.TypeName)
            ? ContractBoundaryCheck.Check(
                context, site, subject, contract, domain, boundaryEndpoint, localParameter,
                HasContributedBody(domain, contract, boundaryEndpoint))
            : null;

        var resolvedByBoundary = boundary is not null
                                 && resolution.Status == ContractBindingStatus.Incompatible;
        var status = pinMismatch && boundaryEndpoint is not null
            ? ContractBindingStatus.PinMismatch
            : resolvedByBoundary ? ContractBindingStatus.Bound : resolution.Status;
        return new ContractClassification(status, boundaryEndpoint, boundary);
    }

    /// <summary>The outcome of <see cref="ClassifyResolution"/>.</summary>
    internal sealed record ContractClassification(
        ContractBindingStatus Status,
        ContractEndpoint? Endpoint,
        ContractBoundaryMetadata? Boundary);

    /// <summary>
    /// #425: whether the loaded library contributed an implementation body for this endpoint.
    /// A surface-only contribution (a sibling export) has none and stays unknown on the
    /// postcondition half. A present body is walked by <c>ContractBoundaryCheck</c>.
    /// Reads the session the same way <see cref="ValidatePlatformTarget"/> does; a domain whose
    /// <c>uses</c> id the catalog cannot load throws in <c>DomainEvolution.ResolveSession</c>
    /// before either runs.
    /// </summary>
    private static bool HasContributedBody(Domain domain, ImportedContract contract, ContractEndpoint endpoint) {
        if (contract.UsesId is null)
            return false;
        var contribution = RuntimeAnalysisCache.Session(domain).FindContractContribution(contract.UsesId);
        return contribution is not null && contribution.Bodies.ContainsKey(endpoint.Name);
    }

    private static bool HasEndpointCandidate(ImportedContract contract, string authoredEndpoint) =>
        contract.Endpoints.Any(endpoint =>
            string.Equals(endpoint.Name, authoredEndpoint, StringComparison.Ordinal)
            || string.Equals(endpoint.SourceName ?? endpoint.Name, authoredEndpoint, StringComparison.Ordinal));

    /// <summary>
    /// The distinct parameter lists of the named action's variants on the entity that owns the
    /// name (the same first-entity rule as <see cref="DomainAnalysis.FindAnyNamedActionOnAnyEntity"/>),
    /// formatted for the diagnostic, or null when every variant agrees.
    /// </summary>
    private static string? DisagreeingSignatures(Domain domain, string actionName) {
        foreach (var type in domain.Types) {
            if (type is not Entity entity)
                continue;
            var actions = DomainAnalysis.FindNamedActions(entity, actionName);
            if (actions.Count == 0)
                continue;
            var signatures = actions
                .Select(DomainAnalysis.ParameterSignature)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            return signatures.Count > 1
                ? string.Join(" vs ", signatures.Select(signature => $"'{signature}'"))
                : null;
        }
        return null;
    }

    /// <summary>
    /// Reports one #422 contract finding. A matching acknowledged allowance downgrades it to a
    /// warning and stores the fact that makes Lower refuse; without a match it is an error. Only
    /// the decision-8 set is routed here, never <see cref="DomainModelDiagnosticCodes.ContractIntegration"/>
    /// metadata errors, <see cref="DomainModelDiagnosticCodes.ContractOverloadAmbiguous"/>, or a flow code.
    /// </summary>
    internal static void ReportContractFinding(
        AnalysisContext context, Domain domain, Node finding, string code, string message) {
        if (!DiagnosticAllowanceMatch.TryConsume(context, domain, finding, code)) {
            context.ReportError(finding, message, code);
            return;
        }

        context.ReportWarning(finding, message, code);
        context.GetOrAddMetadata(domain, () => new ContractToleranceMetadata())
            .Add(new ContractToleranceFact(code, DomainElementPath.Resolve(domain, finding), message));
    }

    private static void RecordBinding(
        AnalysisContext context,
        ContractBinding binding,
        ImportedContract? contract,
        ContractBindingStatus status,
        string? endpoint) {
        context.SetMetadata(binding, new ContractBindingMetadata(
            contract?.UsesId,
            contract?.Name ?? binding.ContractName,
            contract?.SourceIdentifier,
            contract?.Version,
            contract?.Fingerprint,
            binding.EndpointName,
            endpoint,
            binding.ActionName,
            binding.LocalParameterName,
            status));
    }

    private static string PinSuffix(ImportedContract contract) =>
        contract.Pin is { Length: > 0 } pin ? $" (pinned '{pin}')" : "";

    internal static string DescribeCandidates(IReadOnlyList<ContractEndpoint> candidates) =>
        candidates.Count switch {
            0 => "no endpoint",
            1 => $"endpoint '{candidates[0].Name}' ({candidates[0].PayloadType.TypeName})",
            _ => "endpoints " + string.Join(", ",
                candidates.Select(candidate => $"'{candidate.Name}' ({candidate.PayloadType.TypeName})")),
        };
}