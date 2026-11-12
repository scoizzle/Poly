using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Effects;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// S-04: <see cref="StageEntryFacts.EntryAssignedProperties"/> is the deep walk over the
/// initial stage's entry chain. These tests pin the contract the later switch-over (S-14 /
/// L-09) relies on, and pin the flat helper as a subset of it so the two cannot drift while
/// both exist.
/// <para>
/// Every fixture is applied with <see cref="DomainEvolution.Apply"/> (mutation only, no
/// analysis): the fact is pure over <see cref="Entity"/>, so no analyzer may mask it.
/// </para>
/// </summary>
public class StageEntryFactsTests {
    private static Domain Apply(string dsl) {
        var changes = new PolyDslParser(dsl).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        if (!result.Succeeded)
            throw new InvalidOperationException(result.FailureSummary);
        return result.Root;
    }

    private static Entity EntityNamed(Domain domain, string name) =>
        domain.Types.OfType<Entity>().Single(e => string.Equals(e.Name, name, StringComparison.Ordinal));

    private static IReadOnlySet<string> EntryAssigned(string dsl, string entityName) =>
        EntryAssigned(Apply(dsl), entityName);

    private static IReadOnlySet<string> EntryAssigned(Domain domain, string entityName) =>
        StageEntryFacts.EntryAssignedProperties(EntityNamed(domain, entityName), domain);

    // Replaces a stage's entry effects with API-built effects the parser cannot produce.
    private static Domain WithEntryEffects(Domain domain, string entityName, string stageName, params Effect[] effects) =>
        domain with {
            Types = domain.Types.Select(t => t is Entity e && string.Equals(e.Name, entityName, StringComparison.Ordinal)
                ? e with {
                    Stages = e.Stages
                        .Select(s => string.Equals(s.Name, stageName, StringComparison.Ordinal)
                            ? s with { OnEntryEffects = effects }
                            : s)
                        .ToList()
                }
                : t).ToList()
        };

    [Test]
    public async Task EntryAssignedProperties_IncludesTopLevelAssigns() {
        var assigned = EntryAssigned("""
            domain Pin
            Ticket: entity {
              N: Number
              Draft: stage { entry { assign N to 1 } }
            }
            """, "Ticket");

        await Assert.That(assigned).IsEquivalentTo(new[] { "N" }, StringComparer.Ordinal);
    }

    [Test]
    public async Task EntryAssignedProperties_FindsNestedConditionalAndForAssigns() {
        var assigned = EntryAssigned("""
            domain Nested
            Tag: entity { Label: Text }
            Ticket: entity {
              N: Number
              M: Number
              tags: many Tag
              Draft: stage {
                entry {
                  if (N > 0) { assign N to 1 } else { assign M to 2 }
                  for tag in tags { assign M to 3 }
                }
              }
            }
            """, "Ticket");

        await Assert.That(assigned).IsEquivalentTo(new[] { "N", "M" }, StringComparer.Ordinal);
    }

    [Test]
    public async Task EntryAssignedProperties_FollowsTransitionChainFromInitialStage() {
        var assigned = EntryAssigned("""
            domain Chain
            Order: entity {
              M: Number
              Draft: stage { entry { transition to Reserved } }
              Reserved: stage { entry { assign M to 2 } }
            }
            """, "Order");

        await Assert.That(assigned).IsEquivalentTo(new[] { "M" }, StringComparer.Ordinal);
    }

    [Test]
    public async Task EntryAssignedProperties_IgnoresSecondStageEntry_NotReachedByTransition() {
        var assigned = EntryAssigned("""
            domain SecondStage
            Order: entity {
              N: Number
              M: Number
              Draft: stage { entry { assign N to 1 } }
              Done: stage { entry { assign M to 2 } }
            }
            """, "Order");

        await Assert.That(assigned).IsEquivalentTo(new[] { "N" }, StringComparer.Ordinal);
    }

    [Test]
    public async Task EntryAssignedProperties_IgnoresExitAndActionAssigns() {
        var assigned = EntryAssigned("""
            domain ExitAndAction
            Order: entity {
              N: Number
              M: Number
              Go: action { assign M to 2 }
              Draft: stage {
                entry { assign N to 1 }
                exit { assign M to 3 }
              }
            }
            """, "Order");

        await Assert.That(assigned).IsEquivalentTo(new[] { "N" }, StringComparer.Ordinal);
    }

    [Test]
    public async Task EntryAssignedProperties_IgnoresActionTransitionTargets() {
        var assigned = EntryAssigned("""
            domain ActionTransition
            Order: entity {
              N: Number
              M: Number
              Go: action { transition to Done }
              Draft: stage { entry { assign N to 1 } }
              Done: stage { entry { assign M to 2 } }
            }
            """, "Order");

        await Assert.That(assigned).IsEquivalentTo(new[] { "N" }, StringComparer.Ordinal);
    }

    // N1: an unconditional entry cycle is a transition-loop analysis error, and this fixture
    // runs without analysis, so one edge is guarded the way C1bConstraintFailureTests guards
    // it. The guard is a conditional, which the walk flattens, so both assigns still count.
    [Test]
    public async Task EntryAssignedProperties_TransitionCycle_Terminates() {
        var assigned = EntryAssigned("""
            domain Cycle
            Pair: entity {
              N: Number
              M: Number
              Flag: Boolean
              A: stage {
                entry {
                  assign N to 1
                  transition to B
                }
              }
              B: stage {
                entry {
                  assign M to 2
                  if (Flag is true) { transition to A }
                }
              }
            }
            """, "Pair");

        await Assert.That(assigned).IsEquivalentTo(new[] { "N", "M" }, StringComparer.Ordinal);
    }

    // Pin (negative): no stages means no entry chain. Green on the empty stub too.
    [Test]
    public async Task EntryAssignedProperties_EntityWithoutStages_IsEmpty() {
        var assigned = EntryAssigned("""
            domain Stageless
            Note: entity { Body: Text }
            """, "Note");

        await Assert.That(assigned).IsNotNull();
        await Assert.That(assigned).IsEmpty();
    }

    // Pin (N7): paired with a real top-level assign so it is red on the empty stub while still
    // showing a navigation target is skipped. The parser writes only bare targets, so the
    // navigation assign is planted through the model API.
    [Test]
    public async Task EntryAssignedProperties_AssignThroughNavigation_IsNotAPropertyName() {
        var domain = WithEntryEffects(Apply("""
            domain Navigation
            Owner: entity { Balance: Number }
            Ticket: entity {
              N: Number
              owner: Owner
              Draft: stage { entry { assign N to 1 } }
            }
            """), "Ticket", "Draft",
            new AssignEffect(new PropertyAccess("N"), DomainExpression.Literal(1L)),
            new AssignEffect(
                new RelationshipNavigation("owner", DomainExpression.Property("Balance")),
                DomainExpression.Literal(0L)));

        var assigned = EntryAssigned(domain, "Ticket");

        await Assert.That(assigned).IsEquivalentTo(new[] { "N" }, StringComparer.Ordinal);
    }

    // N2: the parity oracle. Flat is the existing top-level-only walk, deep is the fact. On a
    // top-level-only entry the two must be equal; with a nested assign the flat walk must stay
    // a strict subset. Both halves fail against an empty stub.
    [Test]
    public async Task EntryAssignedProperties_IsSupersetOfFlatHelper() {
        var domainOnly = Apply("""
            domain ParityFlat
            Ticket: entity {
              N: Number
              Draft: stage { entry { assign N to 1 } }
            }
            """);
        var topLevelOnly = EntityNamed(domainOnly, "Ticket");
        var domainNested = Apply("""
            domain ParityNested
            Ticket: entity {
              N: Number
              M: Number
              Draft: stage { entry { assign N to 1  if (N > 0) { assign M to 2 } } }
            }
            """);
        var nested = EntityNamed(domainNested, "Ticket");

        var flatOnly = EntityStructureAnalyzer.ComputeEntryAssignedPropertyNames(topLevelOnly);
        var deepOnly = StageEntryFacts.EntryAssignedProperties(topLevelOnly, domainOnly);
        var flatNested = EntityStructureAnalyzer.ComputeEntryAssignedPropertyNames(nested);
        var deepNested = StageEntryFacts.EntryAssignedProperties(nested, domainNested);

        await Assert.That(deepOnly).IsEquivalentTo(flatOnly, StringComparer.Ordinal);
        await Assert.That(flatNested).IsEquivalentTo(new[] { "N" }, StringComparer.Ordinal);
        await Assert.That(deepNested).IsEquivalentTo(new[] { "N", "M" }, StringComparer.Ordinal);
        await Assert.That(flatNested.All(deepNested.Contains)).IsTrue();
    }

    // N2: the corpus sweep is the "no behaviour change" parity the switch-over relies on.
    // Every entity in every parseable .poly must satisfy flat ⊆ deep. Files that do not parse
    // or apply are recorded and capped, so the sweep cannot pass by skipping the corpus.
    [Test]
    public async Task EntryAssignedProperties_IsSupersetOfFlatHelper_AcrossCorpus() {
        var root = FindRepoRoot();
        var mismatches = new List<string>();
        var skipped = new List<string>();
        var entities = 0;

        foreach (var path in Directory.EnumerateFiles(root, "*.poly", SearchOption.AllDirectories)
                     .Where(p => !IsBuildOutput(root, p))
                     .Order(StringComparer.Ordinal)) {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            MutationResult applied;
            try {
                applied = new DomainEvolution(DomainTestFactory.Create("_", [], []))
                    .Apply(new PolyDslParser(File.ReadAllText(path)).Parse());
            }
            catch (Exception ex) {
                skipped.Add($"{relative}: {ex.GetType().Name}");
                continue;
            }
            if (!applied.Succeeded) {
                skipped.Add($"{relative}: {applied.FailureSummary}");
                continue;
            }
            foreach (var entity in applied.Root.Types.OfType<Entity>()) {
                entities++;
                var flat = EntityStructureAnalyzer.ComputeEntryAssignedPropertyNames(entity);
                var deep = StageEntryFacts.EntryAssignedProperties(entity, applied.Root);
                var flatOnly = flat.Where(name => !deep.Contains(name)).Order(StringComparer.Ordinal).ToList();
                if (flatOnly.Count > 0)
                    mismatches.Add($"{relative}: {entity.Name}: flat-only {{{string.Join(", ", flatOnly)}}}");
            }
        }

        await Assert.That(mismatches).IsEmpty();
        await Assert.That(entities).IsGreaterThanOrEqualTo(50);
        await Assert.That(skipped.Count).IsLessThanOrEqualTo(20);
    }

    // N3 (known gap, characterized not fixed): the constructor inlines the initial entry, so an
    // entry transition runs the departed stage's exit effects at create time. Q20 and the #681
    // contract say OnEntry only, so this fact excludes them; S-14 and L-09 cannot rely on
    // either answer until the owner rules. This test pins the current choice.
    [Test]
    public async Task EntryAssignedProperties_EntryTransition_ExcludesDepartedStageExitAssigns() {
        var assigned = EntryAssigned("""
            domain DepartedExit
            Order: entity {
              M: Number
              X: Number
              Draft: stage {
                entry { transition to Reserved }
                exit { assign X to 1 }
              }
              Reserved: stage { entry { assign M to 2 } }
            }
            """, "Order");

        await Assert.That(assigned).IsEquivalentTo(new[] { "M" }, StringComparer.Ordinal);
        await Assert.That(assigned.Contains("X")).IsFalse();
    }

    private static bool IsBuildOutput(string root, string path) =>
        Path.GetRelativePath(root, path)
            .Split(Path.DirectorySeparatorChar)
            .Any(segment => segment is "bin" or "obj" or "node_modules" or ".git");

    private static string FindRepoRoot() {
        var dir = AppContext.BaseDirectory;
        while (dir is not null) {
            if (File.Exists(Path.Combine(dir, "docs/CORE.md")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("Could not find repo root from " + AppContext.BaseDirectory);
    }
}