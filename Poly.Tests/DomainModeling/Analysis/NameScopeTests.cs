using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// S-01 (#528 #519): the generated C# surface of one entity is one namespace. A member whose
/// identity collides with another member's identity (DMSTR008) or with a name the exporter
/// generates itself (DMSTR009) does not compile, so analysis reports it. C# keywords are not
/// reserved here — escaping them is E-10's job (OQ-12b).
/// </summary>
public sealed class NameScopeTests {
    // A2: diagnostics come from the analysis result, and Root is null when analysis failed.
    private static AnalysisResult Analyze(string dsl) {
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(
            new PolyDslParser(dsl).Parse());
        return result.Succeeded ? DomainModelAnalyzer.Analyze(result.Root!) : result.Analysis;
    }

    private static IReadOnlyList<Diagnostic> Errors(AnalysisResult analysis, string code) =>
        analysis.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Code == code)
            .ToList();

    private static IReadOnlyList<Diagnostic> AllErrors(AnalysisResult analysis) =>
        analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

    private static string NodeName(Diagnostic diagnostic) =>
        diagnostic.Node is DomainMember member ? member.Name : diagnostic.Node.GetType().Name;

    [Test]
    public async Task PolicyNamedLikeProperty_IsOnePositionedError() {
        var analysis = Analyze("""
            domain Scope
            A: entity {
              N: Text
              N: policy { N is "a" }
            }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.StructuralDuplicate);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(NodeName(errors[0])).IsEqualTo("N");
        await Assert.That(errors[0].Message).Contains("collision");
    }

    [Test]
    public async Task MemberNamedLikeEntity_IsError() {
        var analysis = Analyze("""
            domain Scope
            A: entity {
              A: Text
            }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.ReservedMemberName);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(NodeName(errors[0])).IsEqualTo("A");
        await Assert.That(errors[0].Message).Contains("CS0542");
    }

    [Test]
    public async Task MemberNamedCreate_IsError() {
        var analysis = Analyze("""
            domain Scope
            A: entity {
              Create: Text
            }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.ReservedMemberName);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(NodeName(errors[0])).IsEqualTo("Create");
        await Assert.That(errors[0].Message).Contains("'Create'");
    }

    [Test]
    public async Task EntityNamedSystem_IsError() {
        var analysis = Analyze("""
            domain Scope
            System: entity {
              N: Text
            }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.ReservedGeneratedName);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).IsEqualTo(
            "Type 'System' shadows the System namespace the generated C# imports; rename it.");
    }

    [Test]
    public async Task PropertyAndNavigationDifferingByCase_IsDMSTR008() {
        var analysis = Analyze("""
            domain Scope
            A: entity {
              N: Text
              n: many B
            }
            B: entity { N: Text }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.GeneratedIdentifierCollision);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(NodeName(errors[0])).IsEqualTo("n");
    }

    [Test]
    public async Task CasedVariantsOfOneMember_IsDMSTR008() {
        var analysis = Analyze("""
            domain Scope
            A: entity {
              N: Text
              n: Text
            }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.GeneratedIdentifierCollision);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).Contains("constructor parameter");
    }

    [Test]
    public async Task DuplicateConstructorParameter_IsDMSTR008() {
        // ToCamelCase("ID") and ToCamelCase("Id") are both "id" — CS0100.
        var analysis = Analyze("""
            domain Scope
            A: entity {
              ID: Text
              Id: Text
            }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.GeneratedIdentifierCollision);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).Contains("'id'");
    }

    [Test]
    public async Task NavigationNamedLikeEntity_IsError() {
        var analysis = Analyze("""
            domain Scope
            Customer: entity {
              customer: many Address
            }
            Address: entity { N: Text }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.ReservedMemberName);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(NodeName(errors[0])).IsEqualTo("customer");
        await Assert.That(errors[0].Message).Contains("'Customer'");
    }

    [Test]
    public async Task PropertyCreateAndPolicyCreate_TwoErrorsOnePerNode() {
        // A3: property Create is reserved (DMSTR009); policy Create duplicates it (DMSTR001).
        var analysis = Analyze("""
            domain Scope
            A: entity {
              Create: Text
              Create: policy { Create is "a" }
            }
            """);

        var reserved = Errors(analysis, DomainModelDiagnosticCodes.ReservedMemberName);
        var duplicate = Errors(analysis, DomainModelDiagnosticCodes.StructuralDuplicate);
        await Assert.That(reserved.Count).IsEqualTo(1);
        await Assert.That(duplicate.Count).IsEqualTo(1);
        await Assert.That(reserved[0].Node is Property).IsTrue();
        await Assert.That(duplicate[0].Node is Policy).IsTrue();
        await Assert.That(reserved[0].Message).Contains("'Create'");
        await Assert.That(duplicate[0].Message).Contains("collision");
    }

    [Test]
    public async Task PropertyNamedClass_IsNotRejected() {
        // OQ-12b: C# keywords are E-10's, not a reserved-name row.
        var analysis = Analyze("""
            domain Scope
            A: entity {
              class: Text
            }
            """);

        await Assert.That(AllErrors(analysis).Count).IsEqualTo(0);
    }

    [Test]
    public async Task NavigationNamedEvent_IsNotRejected() {
        var analysis = Analyze("""
            domain Scope
            A: entity {
              event: many B
            }
            B: entity { N: Text }
            """);

        await Assert.That(AllErrors(analysis).Count).IsEqualTo(0);
    }

    [Test]
    public async Task EntityNamedProgram_IsNotRejected() {
        var analysis = Analyze("""
            domain Scope
            Program: entity {
              N: Text
            }
            """);

        await Assert.That(AllErrors(analysis).Count).IsEqualTo(0);
    }

    [Test]
    public async Task EntityNamedTask_IsNotRejected() {
        var analysis = Analyze("""
            domain Scope
            Task: entity {
              N: Text
            }
            """);

        await Assert.That(AllErrors(analysis).Count).IsEqualTo(0);
    }

    [Test]
    public async Task ConstraintFailureException_And_System_Texts() {
        var analysis = Analyze("""
            domain Clash
            System: entity { N: Text }
            ConstraintFailureException: entity { N: Text }
            """);

        var messages = Errors(analysis, DomainModelDiagnosticCodes.ReservedGeneratedName)
            .Select(d => d.Message)
            .OrderBy(m => m, StringComparer.Ordinal)
            .ToList();
        await Assert.That(messages.Count).IsEqualTo(2);
        await Assert.That(messages[0]).IsEqualTo(
            "Type 'ConstraintFailureException' collides with the scaffolding type generated by Emit; rename it.");
        await Assert.That(messages[1]).IsEqualTo(
            "Type 'System' shadows the System namespace the generated C# imports; rename it.");
    }

    [Test]
    public async Task ReservedModuleRows_IncludeTheScaffoldingTypes() {
        // Presence, not an exact list: other slices append module rows by PR.
        var moduleRows = ReservedNames.All
            .Where(r => r.Kind is ReservedKind.GeneratedType or ReservedKind.Namespace)
            .Select(r => r.Name)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var expected in new[] { "DomainResult", "ConstraintFailureException", "System" })
            await Assert.That(moduleRows.Contains(expected)).IsTrue();

        // {Entity}Stage is generated per entity, so it is not a fixed row.
        var evolved = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(
            new PolyDslParser("""
                domain Shop
                Order: entity {
                  Draft: stage { }
                  Placed: stage { }
                }
                """).Parse());
        await Assert.That(evolved.Succeeded).IsTrue();
        var order = evolved.Root!.Types.OfType<Entity>().First(e => e.Name == "Order");
        var stageRow = ReservedNames.ForEntity(order, evolved.Root)
            .FirstOrDefault(r => r.Name == "OrderStage" && r.Kind == ReservedKind.GeneratedType);
        await Assert.That(stageRow).IsNotNull();
    }

    [Test]
    public async Task EmitterParity_EntityMembers_AreReservedOrGap() {
        // One entity exercising every generated member shape: stages with an automatic
        // transition, a unique property, a collection nav with create-in, a singular nav
        // with create-in, and a watched stage. Every emitted member must be an authored
        // name, a ReservedNames row, or an exact subscription-gap name — a new gap fails.
        var evolved = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(
            new PolyDslParser("""
                domain Parity
                Item: entity {
                  Code: Text
                }
                Group: entity {
                  Code: Text unique
                  Label: Text
                  items: many Item
                  owner: Owner
                  Add: action (code: Text) {
                    create in items { Code: code }
                  }
                  AddOne: action (label: Text) {
                    create in owner { Label: label }
                  }
                  Draft: stage { }
                  Live: stage { entry { transition to Closed } }
                  Closed: stage { }
                }
                Owner: entity {
                  Label: Text
                  groups: many Group
                  when groups Live {
                    assign Label to "watched"
                  }
                }
                """).Parse());
        await Assert.That(evolved.Succeeded).IsTrue();
        var domain = evolved.Root!;
        var analysis = DomainModelAnalyzer.Analyze(domain);
        await Assert.That(AllErrors(analysis).Count).IsEqualTo(0);

        var group = domain.Types.OfType<Entity>().First(e => e.Name == "Group");
        var typeDef = DomainProgramProjection.ToSyntax(domain, analysis).First(t => t.Name == "Group");

        var authored = new HashSet<string>(StringComparer.Ordinal) { "Group" };
        authored.UnionWith(group.Properties.Select(p => p.Name));
        authored.UnionWith(group.Navigations.Select(n => n.Name));
        authored.UnionWith(group.Actions.Select(a => a.Name));
        authored.UnionWith(group.Stages.SelectMany(s => s.Actions).Select(a => a.Name));
        authored.UnionWith(group.Policies.Select(p => p.Name));

        // Named gap (S-05/L-14): subscription-derived names S-01 cannot compute.
        var subscriptionGap = new HashSet<string>(StringComparer.Ordinal) {
            "_ownerGroupsLiveSubscribers",
            "RegisterOwnerGroupsLiveSubscriber",
            "NotifyLiveSubscribers",
            "_notifyDepth",
            "EnterNotifyCascade",
            "ExitNotifyCascade",
        };
        var reserved = ReservedNames.ForEntity(group, domain).Select(r => r.Name).ToHashSet(StringComparer.Ordinal);

        var emitted = new List<string>();
        emitted.AddRange(typeDef.Properties?.Select(p => p.Name) ?? Enumerable.Empty<string>());
        emitted.AddRange(typeDef.Methods?.Select(m => m.Name) ?? Enumerable.Empty<string>());
        emitted.AddRange(typeDef.Fields?.Select(f => f.Name) ?? Enumerable.Empty<string>());

        var unexplained = emitted
            .Where(name => !authored.Contains(name) && !reserved.Contains(name) && !subscriptionGap.Contains(name))
            .ToList();
        await Assert.That(string.Join(",", unexplained)).IsEqualTo("");

        // The gap list is derived from the emitted members, not a wish: every gap name must
        // actually appear, so a stale entry fails here instead of silently widening the gap.
        var missingGap = subscriptionGap.Where(name => !emitted.Contains(name)).ToList();
        await Assert.That(string.Join(",", missingGap)).IsEqualTo("");
    }

    [Test]
    public async Task PropertyCreateAndNavigationNamedLikeEntity_TwoErrorsOneEach() {
        // RISK 3: a navigation's own generated property is not a name it collides with, so the
        // reserved check must not fire twice on `customer` of `Customer`.
        var analysis = Analyze("""
            domain Scope
            Customer: entity {
              Create: Text
              customer: many Address
            }
            Address: entity { N: Text }
            """);

        var reserved = Errors(analysis, DomainModelDiagnosticCodes.ReservedMemberName);
        await Assert.That(reserved.Count).IsEqualTo(2);
        await Assert.That(string.Join(",", reserved.Select(NodeName).OrderBy(n => n, StringComparer.Ordinal)))
            .IsEqualTo("Create,customer");
        await Assert.That(AllErrors(analysis).Count).IsEqualTo(2);
    }

    // ── Fix round 1: probe-proven false positives and negatives ──────────────

    private static Domain ParseDomain(string dsl) {
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(
            new PolyDslParser(dsl).Parse());
        return result.Root!;
    }

    [Test]
    public async Task PropertyNamedCurrentStage_OnImplicitSingleStageEntity_IsNotRejected() {
        // Probe (a): one stage with no effects/policies/transitions emits no stage enum and no
        // CurrentStage (EntityStructureAnalyzer.IsImplicitStage), so the property is free.
        var analysis = Analyze("""
            domain Scope
            A: entity {
              CurrentStage: Text
              Only: stage { }
            }
            """);

        await Assert.That(AllErrors(analysis).Count).IsEqualTo(0);

        var domain = ParseDomain("""
            domain Scope
            A: entity {
              CurrentStage: Text
              Only: stage { }
            }
            """);
        var entity = domain.Types.OfType<Entity>().First(e => e.Name == "A");
        var rows = ReservedNames.ForEntity(entity, domain).Select(r => r.Name).ToList();
        await Assert.That(rows.Contains("CurrentStage")).IsFalse();
        await Assert.That(rows.Contains("AStage")).IsFalse();
    }

    [Test]
    public async Task PropertyNamedCurrentStage_OnMultiStageEntity_IsRejected() {
        // Probe (g), the control: a generated stage enum makes the property CS0102.
        var analysis = Analyze("""
            domain Scope
            A: entity {
              CurrentStage: Text
              One: stage { }
              Two: stage { }
            }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.ReservedMemberName);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(NodeName(errors[0])).IsEqualTo("CurrentStage");
    }

    [Test]
    public async Task PropertyNamedSystem_IsNotRejected() {
        // Probe (b): a member named System compiles; the namespace row is module scope only.
        var analysis = Analyze("""
            domain Scope
            A: entity {
              System: Text
            }
            """);

        await Assert.That(AllErrors(analysis).Count).IsEqualTo(0);
    }

    [Test]
    public async Task PropertyNamedConstraintFailureException_IsNotRejected() {
        // Probe (c2): `new ConstraintFailureException(...)` in a void action body still binds
        // to the scaffolding type when a property carries the name.
        var analysis = Analyze("""
            domain Scope
            A: entity {
              ConstraintFailureException: Text
              N: Text
              Bs: many B
              Add: action (n: Text) {
                create in Bs { N: n }
              }
            }
            B: entity { N: Text }
            """);

        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.ReservedMemberName).Count).IsEqualTo(0);
        await Assert.That(AllErrors(analysis).Count).IsEqualTo(0);
    }

    [Test]
    public async Task PropertyNamedDomainResult_IsNotRejected() {
        // Probe (d): the generated scaffolding type is referenced as `DomainResult.X` at type
        // scope, which a property of that name does not shadow.
        var analysis = Analyze("""
            domain Scope
            A: entity {
              DomainResult: Text
              Make: action -> B {
                create B { N: "x" }
              }
            }
            B: entity { N: Text }
            """);

        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.ReservedMemberName).Count).IsEqualTo(0);
        await Assert.That(AllErrors(analysis).Count).IsEqualTo(0);
    }

    [Test]
    public async Task PropertyNamedLikeCollectionNavField_IsRejected() {
        // Probe (e): the property `_items` and the collection nav `items` both emit `_items`
        // (Relationships.cs:149) — CS0102, and no rule reported it before this fix.
        var analysis = Analyze("""
            domain Scope
            A: entity {
              _items: Text
              items: many B
            }
            B: entity { N: Text }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.ReservedMemberName);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(NodeName(errors[0])).IsEqualTo("_items");
        await Assert.That(errors[0].Message).Contains("_items");
    }

    [Test]
    public async Task PropertyCreateAndActionCreate_OneReportPerNode() {
        // One node reports once per pass, priority DMSTR001 → DMSTR008 → DMSTR009: the action
        // is reported by the cross-list pass and must not also take the reserved row.
        // (The plan's duplicate-property fixture is unreachable: the parser rejects a nav named
        // like a property, and a duplicate property key throws in ExpressionTypeAnalyzer, so
        // the same-kind skip is defensive.)
        var analysis = Analyze("""
            domain Scope
            A: entity {
              Create: Text
              Create: action { }
            }
            """);

        var duplicate = Errors(analysis, DomainModelDiagnosticCodes.StructuralDuplicate);
        var reserved = Errors(analysis, DomainModelDiagnosticCodes.ReservedMemberName);
        await Assert.That(duplicate.Count).IsEqualTo(1);
        await Assert.That(reserved.Count).IsEqualTo(1);
        await Assert.That(duplicate[0].Node is Poly.DomainModeling.Ontology.Action).IsTrue();
        await Assert.That(reserved[0].Node is Property).IsTrue();
        await Assert.That(AllErrors(analysis).Count).IsEqualTo(2);
    }

    [Test]
    public async Task PropertyActionAndPolicySameName_PropertyActionPairStillReported() {
        // The action↔policy carve-out is per-entry: the property↔action pair stays attributed.
        var analysis = Analyze("""
            domain Scope
            A: entity {
              X: Text
              X: action { }
              X: policy { X is "a" }
            }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.StructuralDuplicate);
        await Assert.That(errors.Count).IsEqualTo(2);
        await Assert.That(errors.Count(d => d.Node is Poly.DomainModeling.Ontology.Action)).IsEqualTo(1);
        await Assert.That(errors.Count(d => d.Node is Policy)).IsEqualTo(1);
    }

    [Test]
    public async Task EmitterParity_EveryMemberReservedRow_IsEmitted() {
        // The converse of EmitterParity_EntityMembers_AreReservedOrGap: a member-scope row
        // that applies to this fixture must name a member the emitter actually writes, so a
        // stale or over-broad row fails here.
        var evolved = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(
            new PolyDslParser("""
                domain Parity
                Item: entity {
                  Code: Text
                }
                Group: entity {
                  Code: Text unique
                  Label: Text
                  items: many Item
                  owner: Owner
                  Add: action (code: Text) {
                    create in items { Code: code }
                  }
                  Draft: stage { }
                  Live: stage { entry { transition to Closed } }
                  Closed: stage { }
                }
                Owner: entity {
                  Label: Text
                  groups: many Group
                }
                """).Parse());
        await Assert.That(evolved.Succeeded).IsTrue();
        var domain = evolved.Root!;
        var analysis = DomainModelAnalyzer.Analyze(domain);
        await Assert.That(AllErrors(analysis).Count).IsEqualTo(0);

        var group = domain.Types.OfType<Entity>().First(e => e.Name == "Group");
        var typeDef = DomainProgramProjection.ToSyntax(domain, analysis).First(t => t.Name == "Group");
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        emitted.UnionWith(typeDef.Properties?.Select(p => p.Name) ?? []);
        emitted.UnionWith(typeDef.Methods?.Select(m => m.Name) ?? []);
        emitted.UnionWith(typeDef.Fields?.Select(f => f.Name) ?? []);

        var memberRows = ReservedNames.ForEntity(group, domain)
            .Where(static row => row.Kind == ReservedKind.GeneratedMember)
            .Select(static row => row.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var missing = memberRows.Where(name => !emitted.Contains(name)).ToList();
        await Assert.That(string.Join(",", missing)).IsEqualTo("");
    }
}