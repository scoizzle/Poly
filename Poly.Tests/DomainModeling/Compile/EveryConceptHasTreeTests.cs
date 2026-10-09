using Poly.DomainModeling.Ontology;

using Artifact = Poly.DomainModeling.Compile.Artifact;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// H4: every sample concept has its tree by name inside the entity catalog
/// artifact. Known gaps live in <c>h4-known-gaps.txt</c>; a listed gap whose
/// tree now exists fails, and an unlisted concept with no tree fails.
/// </summary>
public sealed class EveryConceptHasTreeTests {
    private const string InvalidProbe = "nested-invoke-type-mismatch.poly";

    public static IEnumerable<string> SampleDomains() {
        var root = FindRepoRoot();
        var live = new[] { "docs/probes", "demo/live" }
            .Select(r => Path.Combine(root, r))
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.poly", SearchOption.AllDirectories))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'));
        return EmitGoldenTests.SampleDomains()
            .Concat(live)
            .Distinct(StringComparer.Ordinal)
            .Where(p => !string.Equals(Path.GetFileName(p), InvalidProbe, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);
    }

    [Test]
    public async Task SampleDomains_UnionsGoldensAndLiveRoots_AndSkipsTheInvalidProbe() {
        var samples = SampleDomains().ToList();
        await Assert.That(samples.Count).IsGreaterThanOrEqualTo(10);
        await Assert.That(samples.Any(p =>
            string.Equals(Path.GetFileName(p), InvalidProbe, StringComparison.Ordinal))).IsFalse();
        foreach (var golden in EmitGoldenTests.SampleDomains())
            await Assert.That(samples.Contains(golden)).IsTrue();
    }

    [Test]
    [MethodDataSource(nameof(SampleDomains))]
    public async Task Lower_EveryConcept_HasItsTreeByName(string relativePath) {
        var (session, domain, analysis) = EmitGoldenTests.AnalyzeSampleFile(relativePath);
        session.Lower(domain, analysis);
        var catalog = session.ArtifactCatalog;
        var gaps = LoadGaps();
        var missing = new List<string>();
        var stale = new List<string>();
        var entityTypes = new List<TypeDefinitionNode>();

        foreach (var entity in domain.Types.OfType<Entity>()) {
            var tree = catalog.Find(ArtifactId.Create([domain.Name, entity.Name], "entity"));
            if (tree?.Payload is not IReadOnlyList<TypeDefinitionNode> types) {
                missing.Add($"{entity.Name}#entity");
                continue;
            }

            var entityType = types.FirstOrDefault(t => t.Name == entity.Name);
            if (entityType is null) {
                missing.Add($"{entity.Name} type");
                continue;
            }
            entityTypes.Add(entityType);

            var methods = entityType.Methods ?? [];
            foreach (var name in ActionNames(entity)) {
                if (!methods.Any(m => m.Name == name))
                    missing.Add($"action {entity.Name}.{name}");
            }

            foreach (var name in PolicyNames(entity)) {
                if (!methods.Any(m => m.Name == name && IsBoolMethod(m)))
                    missing.Add($"policy {entity.Name}.{name}");
            }

            foreach (var name in HandlerNames(entity)) {
                if (!methods.Any(m => m.Name == name))
                    missing.Add($"subscription {entity.Name}.{name}");
            }

            var stagesWithEffects = entity.Stages
                .Where(s => s.OnEntryEffects.Count > 0 || s.OnExitEffects.Count > 0)
                .ToList();
            if (stagesWithEffects.Count > 0) {
                var stageEnum = types.FirstOrDefault(t => t.Name == $"{entity.Name}Stage");
                if (stageEnum is null) {
                    missing.Add($"{entity.Name}Stage");
                }
                else {
                    var fields = stageEnum.Fields ?? [];
                    foreach (var stage in stagesWithEffects) {
                        if (!fields.Any(f => f.Name == stage.Name))
                            missing.Add($"stage {entity.Name}.{stage.Name}");
                    }
                }
            }

            foreach (var prop in entity.Properties) {
                foreach (var constraint in prop.Constraints) {
                    if (!ConstraintInTree(entityType, prop.Name, constraint))
                        missing.Add($"{constraint.GetType().Name} {entity.Name}.{prop.Name}");
                }
            }
        }

        foreach (var gap in gaps) {
            if (!GapNowHasTree(gap, catalog, entityTypes))
                continue;
            stale.Add($"{gap.Kind}\t{gap.Key}");
        }

        var expressionConstants = new List<string>();
        foreach (var artifact in catalog.Artifacts.Where(IsTree)) {
            if (artifact.Payload is not IReadOnlyList<TypeDefinitionNode> types)
                continue;
            expressionConstants.AddRange(DomainExpressionConstants(types));
        }

        await Assert.That(missing).IsEmpty();
        await Assert.That(stale).IsEmpty();
        await Assert.That(expressionConstants).IsEmpty();
    }

    [Test]
    public async Task ListedGaps_FireOnlyWhenTheirTreeExists() {
        var empty = new ArtifactCatalog();
        var clean = new TypeDefinitionNode("Item");
        await Assert.That(GapNowHasTree(new Gap("no-tree", "instance-delete"), empty, [clean])).IsFalse();
        await Assert.That(GapNowHasTree(new Gap("no-tree", "owned-or-aggregate"), empty, [clean])).IsFalse();
        await Assert.That(GapNowHasTree(new Gap("inside-entity-tree-only", "action"), empty, [clean])).IsFalse();

        var withDelete = new TypeDefinitionNode(
            "Item",
            Methods: [new MethodDefinitionNode("Delete", new TypeReference("void"))]);
        await Assert.That(GapNowHasTree(new Gap("no-tree", "instance-delete"), empty, [withDelete])).IsTrue();

        var owned = new ArtifactCatalog();
        owned.DeclareType("owned", mayPointAt: []);
        owned.Register(new Artifact(
            new ArtifactDescriptor(ArtifactId.Create(["Crm", "Account", "hq"], "owned"), "Lower"),
            null));
        await Assert.That(GapNowHasTree(new Gap("no-tree", "owned-or-aggregate"), owned, [clean])).IsTrue();

        var methods = new ArtifactCatalog();
        methods.DeclareType("method", mayPointAt: []);
        methods.Register(new Artifact(
            new ArtifactDescriptor(ArtifactId.Create(["Parking", "Permit", "Open"], "method"), "Lower"),
            null));
        await Assert.That(GapNowHasTree(new Gap("inside-entity-tree-only", "action"), methods, [clean])).IsTrue();
    }

    [Test]
    public async Task Planted_ConstantOfDomainExpression_IsReported() {
        var planted = new TypeDefinitionNode(
            "Probe",
            Methods: [
                new MethodDefinitionNode(
                    "M",
                    new PrimitiveTypeReference(Poly.Introspection.PrimitiveType.Int32),
                    Body: new Constant(DomainExpression.Literal(1)))
            ]);

        await Assert.That(DomainExpressionConstants([planted])).IsEquivalentTo(["Probe"]);
    }

    private static IEnumerable<string> ActionNames(Entity entity) =>
        entity.Actions.Select(a => a.Name)
            .Concat(entity.Stages.SelectMany(s => s.Actions).Select(a => a.Name))
            .Distinct(StringComparer.Ordinal);

    private static IEnumerable<string> PolicyNames(Entity entity) {
        var names = entity.Policies.Select(p => p.Name)
            .Concat(entity.Stages.SelectMany(s => s.Policies).Select(p => p.Name))
            .Concat(entity.Actions.SelectMany(a => a.Policies).Select(p => p.Name))
            .Concat(entity.Stages.SelectMany(s => s.Actions).SelectMany(a => a.Policies).Select(p => p.Name));
        return names
            .Where(n => !n.StartsWith("not_", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal);
    }

    private static IEnumerable<string> HandlerNames(Entity entity) {
        var counts = new Dictionary<(string Stage, string Target, string Quantifier, bool HasPeer), int>();
        foreach (var sub in entity.Subscriptions.Concat(entity.Stages.SelectMany(s => s.Subscriptions))) {
            var rel = entity.Navigations.FirstOrDefault(n =>
                string.Equals(n.Name, sub.RelationshipName, StringComparison.Ordinal));
            var target = rel?.Target.TypeName ?? sub.RelationshipName;
            var quantifier = sub.Quantifier switch {
                StageSubscriptionQuantifier.Any => "Any",
                StageSubscriptionQuantifier.All => "All",
                _ => "Each",
            };
            var hasPeer = sub.PeerBinding is { Length: > 0 };
            foreach (var stage in sub.StageNames) {
                var key = (stage, target, quantifier, hasPeer);
                counts.TryGetValue(key, out var occurrence);
                counts[key] = occurrence + 1;
                var suffix = occurrence == 0 ? "" : $"_{occurrence + 1}";
                yield return $"When{quantifier}{target}{stage}{suffix}";
            }
        }
    }

    private static bool ConstraintInTree(TypeDefinitionNode entityType, string propName, Constraint constraint) {
        return constraint switch {
            RequiredConstraint =>
                PropertyHasRequiredMarker(entityType, propName)
                || HasMessage(entityType, $"'{propName}' is required."),
            RangeConstraint =>
                HasMessage(entityType, $"'{propName}' must be >=")
                || HasMessage(entityType, $"'{propName}' must be <="),
            LengthConstraint =>
                HasMessage(entityType, $"'{propName}' must be at least")
                || HasMessage(entityType, $"'{propName}' must be at most"),
            PatternConstraint =>
                HasMessage(entityType, $"'{propName}' does not match"),
            EqualityConstraint =>
                HasMessage(entityType, $"'{propName}' must equal"),
            UniqueConstraint =>
                entityType.Methods?.Any(m => m.Name == "EnsureUnique") == true,
            DefaultValueConstraint =>
                HasDefaultValue(entityType, propName),
            _ => false,
        };
    }

    private static bool PropertyHasRequiredMarker(TypeDefinitionNode entityType, string propName) {
        var prop = entityType.Properties?.FirstOrDefault(p => p.Name == propName);
        return prop?.Constraints?.OfType<Constant>().Any(c => c.Value is "required") == true;
    }

    private static bool HasMessage(TypeDefinitionNode type, string fragment) {
        foreach (var node in FlattenSyntax(type)) {
            if (node is Constant { Value: string text }
                && text.Contains(fragment, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static bool HasDefaultValue(TypeDefinitionNode type, string propName) {
        var parameters = (type.Constructors ?? []).SelectMany(c => c.Parameters ?? [])
            .Concat((type.Methods ?? []).SelectMany(m => m.Parameters ?? []))
            .Where(p => string.Equals(p.Name, propName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (parameters.Count > 0)
            return parameters.Any(p => p.DefaultValue is not null);
        // Entry-assigned defaults skip the ctor param; first-stage entry is inlined in the ctor.
        foreach (var ctor in type.Constructors ?? []) {
            if (ctor.Body is null)
                continue;
            foreach (var node in FlattenSyntax(ctor.Body)) {
                if (node is Assignment { Destination: Member { MemberName: var name } }
                    && string.Equals(name, propName, StringComparison.Ordinal))
                    return true;
            }
        }
        return false;
    }

    private static bool IsBoolMethod(MethodDefinitionNode method) =>
        method.ReturnType is PrimitiveTypeReference { PrimitiveId: Poly.Introspection.PrimitiveType.Boolean };

    private static bool IsTree(Artifact artifact) =>
        artifact.Descriptor.Id.Type is "entity" or "scaffolding";

    private static bool GapNowHasTree(Gap gap, ArtifactCatalog catalog, IReadOnlyList<TypeDefinitionNode> entityTypes) {
        if (gap.Kind == "inside-entity-tree-only") {
            var types = CatalogTypesFor(gap.Key);
            return catalog.Artifacts.Any(a => types.Contains(a.Descriptor.Id.Type));
        }
        if (gap.Kind == "no-tree") {
            return gap.Key switch {
                "instance-delete" => entityTypes.Any(t =>
                    t.Methods?.Any(m => m.Name == "Delete") == true),
                "owned-or-aggregate" => catalog.Artifacts.Any(a =>
                    a.Descriptor.Id.Type is "owned" or "aggregate"),
                _ => catalog.Artifacts.Any(a => a.Descriptor.Id.Type == gap.Key),
            };
        }
        throw new InvalidOperationException($"h4-known-gaps.txt unknown kind '{gap.Kind}'.");
    }

    private static IReadOnlyList<string> CatalogTypesFor(string key) => key switch {
        "action" => ["method"],
        "policy" => ["policy"],
        "subscription" => ["handler"],
        "stage-entry-exit" => ["entry", "exit"],
        _ => [key],
    };

    private static IReadOnlyList<string> DomainExpressionConstants(IReadOnlyList<TypeDefinitionNode> types) {
        var hits = new List<string>();
        foreach (var type in types) {
            if (FlattenSyntax(type).Any(n => n is Constant { Value: DomainExpression }))
                hits.Add(type.Name);
        }
        return hits;
    }

    private static IEnumerable<Node> FlattenSyntax(Node node) {
        yield return node;
        foreach (var child in node.Children) {
            if (child is null)
                continue;
            foreach (var n in FlattenSyntax(child))
                yield return n;
        }
    }

    private static IReadOnlyList<Gap> LoadGaps() {
        var path = Path.Combine(FindRepoRoot(), "Poly.Tests", "DomainModeling", "Compile", "h4-known-gaps.txt");
        var gaps = new List<Gap>();
        foreach (var line in File.ReadAllLines(path)) {
            if (line.Length == 0 || line[0] == '#')
                continue;
            var parts = line.Split('\t');
            if (parts.Length < 3)
                throw new InvalidOperationException(
                    "h4-known-gaps.txt line needs kind, key, closer: " + line);
            gaps.Add(new Gap(parts[0], parts[1]));
        }
        return gaps;
    }

    private sealed record Gap(string Kind, string Key);

    private static string FindRepoRoot() {
        var dir = AppContext.BaseDirectory;
        while (dir is not null) {
            if (File.Exists(Path.Combine(dir, "Poly.sln"))
                || File.Exists(Path.Combine(dir, "docs/CORE.md")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("Could not find repo root from " + AppContext.BaseDirectory);
    }
}
