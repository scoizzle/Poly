using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Effects;
using Poly.Tests.TestHelpers;

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
    public async Task EntryAssignedProperties_IgnoresNeverDepartedExitAndUninvokedActionAssigns() {
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

    // N3 (flipped by S-04b, Q12 option (b)): the constructor inlines the initial entry, so an
    // entry transition runs the departed stage's exit effects at create time, and the fact
    // follows Lower's source cursor to include them.
    [Test]
    public async Task EntryAssignedProperties_EntryTransition_InlinesDepartedStageExitAssigns() {
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

        await Assert.That(assigned).IsEquivalentTo(new[] { "M", "X" }, StringComparer.Ordinal);
    }

    // R1: each hop exits its own source (A -> B -> C), so the chain inlines exit(A) and exit(B)
    // but never exit(C), which no hop leaves. Supersedes the pre-#765 "exit(A) twice" quirk.
    [Test]
    public async Task EntryAssignedProperties_NestedEntryChain_ExitsEachSourceOnce() {
        var assigned = EntryAssigned("""
            domain Chain3
            Z: entity {
              N: Number
              X: Number
              Y: Number
              Q: Number
              A: stage {
                entry { assign N to 1  transition to B }
                exit { assign X to 1 }
              }
              B: stage {
                entry { transition to C }
                exit { assign Y to 1 }
              }
              C: stage { exit { assign Q to 1 } }
            }
            """, "Z");

        await Assert.That(assigned).IsEquivalentTo(new[] { "N", "X", "Y" }, StringComparer.Ordinal);
        await Assert.That(assigned.Contains("Q")).IsFalse();
    }

    // R2: G7 adds the lifecycle name once per resolved hop, even when the target's entry never
    // assigns it, and only when a hop resolves.
    [Test]
    public async Task EntryAssignedProperties_LifecycleProperty_AddedPerResolvedHop() {
        const string Hop = """
            domain Lifecycle
            OrderState: enum { Pending, Paid }
            Z: entity {
              N: Number
              State: OrderState default(Pending)
              Pending: stage { entry { assign N to 1  transition to Paid } }
              Paid: stage { }
            }
            """;
        const string TargetAssigns = """
            domain LifecycleAssigned
            OrderState: enum { Pending, Paid }
            Z: entity {
              N: Number
              State: OrderState default(Pending)
              Pending: stage { entry { assign N to 1  transition to Paid } }
              Paid: stage { entry { assign State to Paid } }
            }
            """;
        const string NoHop = """
            domain LifecycleNoHop
            OneState: enum { Only }
            Z: entity {
              N: Number
              State: OneState default(Only)
              Only: stage { entry { assign N to 1 } }
            }
            """;

        await Assert.That(EntryAssigned(Hop, "Z")).IsEquivalentTo(new[] { "N", "State" }, StringComparer.Ordinal);
        await Assert.That(EntryAssigned(TargetAssigns, "Z")).IsEquivalentTo(new[] { "N", "State" }, StringComparer.Ordinal);
        await Assert.That(EntryAssigned(NoHop, "Z")).IsEquivalentTo(new[] { "N" }, StringComparer.Ordinal);
    }

    // R3: a self invoke contributes the callee's own assigns.
    [Test]
    public async Task EntryAssignedProperties_SelfInvoke_FollowsCalleeAssigns() {
        var assigned = EntryAssigned("""
            domain Invoke
            Z: entity {
              N: Number
              M: Number
              Seed: action { assign M to 2 }
              Draft: stage { entry { assign N to 1  invoke Seed } }
            }
            """, "Z");

        await Assert.That(assigned).IsEquivalentTo(new[] { "N", "M" }, StringComparer.Ordinal);
    }

    // R4: the invoke closure is transitive and a mutually recursive pair terminates. Fact-only:
    // the cycle may not analyze, so it has no Lower oracle.
    [Test]
    [Timeout(10_000)]
    public async Task EntryAssignedProperties_InvokeCycle_TerminatesWithClosure(CancellationToken cancellationToken) {
        _ = cancellationToken;
        var domain = Apply("""
            domain InvokeCycle
            Z: entity {
              N: Number
              M: Number
              Init: action { invoke Seed }
              Seed: action {
                assign M to 2
                invoke Init
              }
              Draft: stage { entry { assign N to 1  invoke Init } }
            }
            """);

        var assigned = await Task.Run(() => EntryAssigned(domain, "Z"), cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

        await Assert.That(assigned).IsEquivalentTo(new[] { "N", "M" }, StringComparer.Ordinal);
    }

    // C3: more than one lifecycle candidate yields none from the fact, and analysis rejects the
    // entity first, so the fact's state is unreachable through the pipeline.
    [Test]
    public async Task LifecyclePropertyName_TwoCandidates_IsNoneAndUnreachableThroughAnalysis() {
        const string dsl = """
            domain TwoCandidates
            S: enum { A, B }
            Z: entity {
              One: S default(A)
              Two: S default(A)
              A: stage { entry { transition to B } }
              B: stage { }
            }
            """;
        var domain = Apply(dsl);

        await Assert.That(StageEntryFacts.LifecyclePropertyName(EntityNamed(domain, "Z"), domain)).IsNull();
        await Assert.That(() => EvolvedDomain.FromDsl(dsl)).Throws<InvalidOperationException>();
    }

    // B3: the fact's resolver must equal the analyzer's published lifecycle property on every
    // entity of every corpus file that analyzes. The counts keep the pass from being vacuous.
    [Test]
    public async Task LifecyclePropertyName_MatchesAnalyzer_AcrossCorpus() {
        var root = FindRepoRoot();
        var mismatches = new List<string>();
        var staged = 0;
        var withLifecycle = 0;

        foreach (var path in Directory.EnumerateFiles(root, "*.poly", SearchOption.AllDirectories)
                     .Where(p => !IsBuildOutput(root, p))
                     .Order(StringComparer.Ordinal)) {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            Domain domain;
            AnalysisResult analysis;
            try {
                var evolved = new DomainEvolution(DomainTestFactory.Create("_", [], []))
                    .ApplyAndAnalyze(new PolyDslParser(File.ReadAllText(path)).Parse());
                if (!evolved.Succeeded)
                    continue;
                domain = evolved.Root!;
                analysis = DomainModelAnalyzer.Analyze(domain);
            }
            catch (Exception) {
                continue;
            }
            foreach (var entity in domain.Types.OfType<Entity>()) {
                if (analysis.GetStructure(entity) is not { HasStages: true } structure)
                    continue;
                staged++;
                var resolved = StageEntryFacts.LifecyclePropertyName(entity, domain);
                if (resolved is not null)
                    withLifecycle++;
                if (!string.Equals(resolved, structure.LifecyclePropertyName, StringComparison.Ordinal))
                    mismatches.Add($"{relative}: {entity.Name}: fact '{resolved}' vs analyzer '{structure.LifecyclePropertyName}'");
            }
        }

        await Assert.That(mismatches).IsEmpty();
        await Assert.That(staged).IsGreaterThan(0);
        Console.WriteLine($"LIFECYCLE_LOCKSTEP staged={staged} withLifecycle={withLifecycle}");
    }

    // B3: the corpus has no lifecycle-enum entity (withLifecycle=0), so the lockstep is also pinned
    // on fixtures with, and without, the lifecycle enum.
    [Test]
    public async Task LifecyclePropertyName_MatchesAnalyzer_OnFixtures() {
        var withEnum = EvolvedDomain.FromDsl("""
            domain LockstepEnum
            OrderState: enum { Pending, Paid }
            Z: entity {
              N: Number
              State: OrderState default(Pending)
              Pending: stage { entry { assign N to 1  transition to Paid } }
              Paid: stage { }
            }
            """);
        var noEnum = EvolvedDomain.FromDsl("""
            domain LockstepNoEnum
            Z: entity {
              N: Number
              Pending: stage { entry { assign N to 1  transition to Paid } }
              Paid: stage { }
            }
            """);

        foreach (var (domain, analysis, expected) in new[] { (withEnum.Domain, withEnum.Analysis, "State"), (noEnum.Domain, noEnum.Analysis, (string?)null) }) {
            var entity = EntityNamed(domain, "Z");
            await Assert.That(analysis.GetStructure(entity)!.LifecyclePropertyName).IsEqualTo(expected);
            await Assert.That(StageEntryFacts.LifecyclePropertyName(entity, domain)).IsEqualTo(expected);
        }
    }

    // S-04's OnEntry-chain-only walk, kept test-local so the corpus delta can be computed.
    private static HashSet<string> OnEntryChainOnly(Entity entity) {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (entity.InitialStage() is not { } initial)
            return names;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<Stage>();
        pending.Enqueue(initial);
        while (pending.Count > 0) {
            var stage = pending.Dequeue();
            if (!visited.Add(stage.Name))
                continue;
            foreach (var effect in EffectHelpers.FlattenEffects(stage.OnEntryEffects)) {
                switch (effect) {
                    case AssignEffect { Target: PropertyAccess property }:
                        names.Add(property.Name);
                        break;
                    case StageTransitionEffect transition when entity.Stages.FirstOrDefault(
                        candidate => string.Equals(candidate.Name, transition.TargetStage.StageName, StringComparison.Ordinal)) is { } next:
                        pending.Enqueue(next);
                        break;
                }
            }
        }
        return names;
    }

    // S7: the corpus delta. Entities whose create-time set now exceeds S-04's OnEntry-chain-only
    // set, i.e. the models whose verdict changes when S-14 consumes the fact.
    [Test]
    public async Task EntryAssignedProperties_CorpusDelta_AgainstOnEntryChainOnly() {
        var root = FindRepoRoot();
        var delta = new List<string>();
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
            catch (Exception) {
                continue;
            }
            if (!applied.Succeeded)
                continue;
            foreach (var entity in applied.Root.Types.OfType<Entity>()) {
                entities++;
                var widened = StageEntryFacts.EntryAssignedProperties(entity, applied.Root)
                    .Where(name => !OnEntryChainOnly(entity).Contains(name))
                    .Order(StringComparer.Ordinal)
                    .ToList();
                if (widened.Count > 0)
                    delta.Add($"{relative}:{entity.Name}:{{{string.Join(",", widened)}}}");
            }
        }

        await Assert.That(entities).IsGreaterThanOrEqualTo(50);
        await Assert.That(delta).IsEquivalentTo(Array.Empty<string>(), StringComparer.Ordinal);
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