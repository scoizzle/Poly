using Poly.Ast.Nodes;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation;
using Poly.Interpretation.CSharp;

using Poly.Tests.TestHelpers;

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
    [Arguments("__Snapshot: Text", "__Snapshot")]
    [Arguments("__Restore: Text", "__Restore")]
    [Arguments("__x: Text", "__x")]
    [Arguments("__scope: Number", "__scope")]
    [Arguments("__Go: action { assign Z to \"a\" }\n  Z: Text", "__Go")]
    [Arguments("__p: policy { Z is \"a\" }\n  Z: Text", "__p")]
    [Arguments("__owner: Other", "__owner")]
    public async Task MemberWithActionScopePrefix_IsReserved(string member, string name) {
        var analysis = Analyze($$"""
            domain Scope
            Other: entity {
              Tag: Text
            }
            A: entity {
              {{member}}
            }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.ReservedMemberName);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(NodeName(errors[0])).IsEqualTo(name);
        await Assert.That(errors[0].Message).Contains("names beginning `__` are the generated action-scope surface");
    }

    [Test]
    public async Task ActionParameterWithActionScopePrefix_IsReserved() {
        var analysis = Analyze("""
            domain Scope
            A: entity {
              Z: Text
              Go: action (__n: Text) { assign Z to __n }
            }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.ReservedMemberName);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(NodeName(errors[0])).IsEqualTo("__n");
        await Assert.That(errors[0].Message).Contains("names beginning `__` are the generated action-scope surface");
    }

    [Test]
    public async Task EmitterParity_ActionScopeSurface_IsClosedOverSnapshotRestoreAndActionBodies() {
        // Every emitted member beginning `__` is one of the two IActionScoped members; a new
        // `__` name must be added here deliberately, with the prefix rule already covering it.
        var evolved = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(
            new PolyDslParser("""
                domain Parity
                Item: entity { Code: Text }
                Group: entity {
                  Label: Text
                  items: many Item
                  Add: action (code: Text) { create in items { Code: code } }
                  Draft: stage { }
                  Live: stage { entry { transition to Closed } }
                  Closed: stage { }
                }
                """).Parse());
        await Assert.That(evolved.Succeeded).IsTrue();
        var domain = evolved.Root!;
        var analysis = DomainModelAnalyzer.Analyze(domain);

        var entityTypes = DomainProgramProjection.ToSyntax(domain, analysis)
            .Where(t => t.Kind != Poly.Introspection.TypeKind.Enum)
            .ToList();
        await Assert.That(string.Join(",", entityTypes.Select(t => t.Name).Order(StringComparer.Ordinal)))
            .IsEqualTo("Group,Item");

        foreach (var typeDef in entityTypes) {
            var emitted = new List<string>();
            emitted.AddRange(typeDef.Properties?.Select(p => p.Name) ?? []);
            emitted.AddRange(typeDef.Methods?.Select(m => m.Name) ?? []);
            emitted.AddRange(typeDef.Fields?.Select(f => f.Name) ?? []);
            var scopeNames = emitted.Where(ReservedNames.IsActionScopeName).Order(StringComparer.Ordinal);
            var entity = domain.Types.OfType<Entity>().First(e => e.Name == typeDef.Name);
            // An action, the static Create factory and each Create{Nav} are scope wrappers with a private body partner (L-04b).
            var bodies = entity.Actions.Concat(entity.Stages.SelectMany(s => s.Actions)).Select(a => a.Name)
                .Concat(["Create"]).Concat(entity.Navigations.Select(n => "Create" + Poly.DomainModeling.Lowering.DomainToCSharpExporter.ToPascalCase(n.Name)))
                .Select(n => Poly.DomainModeling.Lowering.DomainToCSharpExporter.ActionBodyPrefix + n).Distinct(StringComparer.Ordinal);
            var expected = bodies.Concat(["__Restore", "__Snapshot"]).Order(StringComparer.Ordinal);
            await Assert.That(string.Join(",", scopeNames)).IsEqualTo(string.Join(",", expected));
        }
    }

    /// <summary>
    /// The private body method a scope wrapper calls: <c>__BodyOf</c> plus an authored ACTION name (not any member), or
    /// the name of a generated wrapper (<c>Create</c>, <c>Create{Nav}</c>), which is a reserved row.
    /// </summary>
    private static bool IsBodyOfAuthoredAction(string name, IReadOnlySet<string> authored, IReadOnlySet<string> reserved) =>
        name.StartsWith(Poly.DomainModeling.Lowering.DomainToCSharpExporter.ActionBodyPrefix, StringComparison.Ordinal)
        && name[Poly.DomainModeling.Lowering.DomainToCSharpExporter.ActionBodyPrefix.Length..] is var wrapped
        && (authored.Contains(wrapped) || reserved.Contains(wrapped));

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
            "Type 'ConstraintFailureException' collides with the Poly.Runtime type the generated C# imports; rename it.");
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
        foreach (var expected in new[] { "DomainResult", "ConstraintFailureException", "System", "Poly" })
            await Assert.That(moduleRows.Contains(expected)).IsTrue();

        // {Entity}Stage is generated per entity, so it is not a fixed row. It is a
        // member-scope row: the constructor body prints `AStage.One`, which a member of that
        // name shadows (probe a).
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
            .FirstOrDefault(r => r.Name == "OrderStage" && r.Kind == ReservedKind.GeneratedReference);
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
                uses persistence
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
                  groups: many Group via owner
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
        var actionNames = group.Actions.Select(a => a.Name)
            .Concat(group.Stages.SelectMany(s => s.Actions).Select(a => a.Name))
            .ToHashSet(StringComparer.Ordinal);
        authored.UnionWith(actionNames);
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
            .Where(name => !authored.Contains(name) && !reserved.Contains(name) && !subscriptionGap.Contains(name)
                && !IsBodyOfAuthoredAction(name, actionNames, reserved))
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
    public async Task PropertyNamedSystem_OnEntityWhoseGeneratedBodyCallsSystem_IsRejected() {
        // Probe (c): the generated action body prints `System.Convert.ToInt64(0)` for a folded
        // zero divisor, and the member wins the simple name from expression position (CS1061).
        // Probe (g) fails the same way on a pattern check's `System.Text.RegularExpressions…
        // Regex.IsMatch`. The row is unconditional because the writer's expression-position
        // `System.` sites span several shapes; probe (h) shows `new System.X(...)` in type
        // position would compile, so this rejection is deliberately conservative.
        var analysis = Analyze("""
            domain Scope
            A: entity {
              System: Text
              N: Number
              Half: action { assign N to N / 0 }
            }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.ReservedMemberName);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(NodeName(errors[0])).IsEqualTo("System");
        await Assert.That(errors[0].Message).Contains("would shadow the generated C# type 'System'");
    }

    [Test]
    public async Task PropertyNamedSystem_OnEntityWithAutomaticTransition_IsRejectedConservatively() {
        // Probe (h) compiles — `new System.InvalidOperationException(...)` is type position, so
        // the namespace wins — but the System row is unconditional (probe c/g), so this shape
        // is rejected anyway. Pinned so the conservative cost is a recorded decision, not an
        // accident: narrowing it would mean twinning the writer's expression-position decisions.
        var analysis = Analyze("""
            domain Scope
            A: entity {
              System: Text
              One: stage { entry { transition to Two } }
              Two: stage { }
            }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.ReservedMemberName);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(NodeName(errors[0])).IsEqualTo("System");
    }

    [Test]
    public async Task PropertyNamedConstraintFailureException_OnEntityWithVoidActionCreateIn_IsNotRejected() {
        // Probes (e) and (f): the name binds in both shapes that could throw it unqualified —
        // a void action's create-in returns a DomainResult instead of throwing, and a stage
        // entry's assign guard emitted no throw in the probed fixture — so it keeps no member
        // row. Only the module-scope type row applies to a *type* of this name.
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
    public async Task PropertyNamedDomainResult_OnEntityWithCreateReturningAction_IsNotRejected() {
        // Probe (b3): a typed action prints only `DomainResult<T>`, which binds as a type
        // beside the member (generic-arity lookup finds the record), and its failure path goes
        // through `create0`/`created1`, not an unqualified `DomainResult.Success()`. The
        // narrow shape matters: a void action (b) and a unique property (b2) both fail.
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
                uses persistence
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

    // ── Fix round 2: the member-scope rows the compile probes prove ──────────

    [Test]
    public async Task PropertyNamedLikeOwnStageEnum_OnGeneratedStageEntity_IsRejected() {
        // Probe (a): the constructor body prints `this.CurrentStage = AStage.One;` for every
        // generated stage enum (Create.cs:433), which is expression position — the member wins
        // the name and the emitted C# is CS1061. A type position (`AStage CurrentStage`) would
        // still compile, which is why this is a GeneratedReference row, not a type row.
        var analysis = Analyze("""
            domain Scope
            A: entity {
              AStage: Text
              One: stage { }
              Two: stage { }
            }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.ReservedMemberName);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(NodeName(errors[0])).IsEqualTo("AStage");
        await Assert.That(errors[0].Message).Contains("would shadow the generated C# type 'AStage'");
    }

    [Test]
    public async Task PropertyNamedDomainResult_OnEntityWithVoidAction_IsRejected() {
        // Probe (b): a void action returns the non-generic DomainResult and its body calls
        // `DomainResult.Success()`, so a member of that name shadows the type (CS1061).
        var analysis = Analyze("""
            domain Scope
            A: entity {
              DomainResult: Text
              Go: action { }
            }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.ReservedMemberName);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(NodeName(errors[0])).IsEqualTo("DomainResult");
        await Assert.That(errors[0].Message).Contains("would shadow the generated C# type 'DomainResult'");
    }

    [Test]
    public async Task PropertyNamedDomainResult_OnEntityWithUniqueProperty_IsRejected() {
        // Probe (b2): EnsureUnique returns the non-generic DomainResult and calls
        // `DomainResult.Success()`/`Failure(...)` from its body.
        var analysis = Analyze("""
            domain Scope
            uses persistence
            A: entity {
              DomainResult: Text
              Code: Text unique
            }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.ReservedMemberName);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(NodeName(errors[0])).IsEqualTo("DomainResult");
    }

    [Test]
    public async Task PropertyNamedLikeStageEnum_OnImplicitSingleStageEntity_IsNotRejected() {
        // Probe (d), the guard: an implicit single stage emits no stage enum (and no
        // CurrentStage), so neither member name is reserved on it.
        var analysis = Analyze("""
            domain Scope
            A: entity {
              AStage: Text
              Only: stage { }
            }
            """);

        await Assert.That(AllErrors(analysis).Count).IsEqualTo(0);
    }

    [Test]
    public async Task EntityTypeNamedLikeStageEnum_IsDMSTR004OnlyWhenTheEnumIsGenerated() {
        // The DMSTR004 stage-enum loop is gated on HasStages: an implicit single stage emits no
        // enum, so a sibling type of that name is legal; a generated enum makes it CS0101.
        var implicitStage = Analyze("""
            domain Scope
            A: entity {
              N: Text
              Only: stage { }
            }
            AStage: entity { M: Text }
            """);
        await Assert.That(AllErrors(implicitStage).Count).IsEqualTo(0);

        var generated = Analyze("""
            domain Scope
            A: entity {
              One: stage { }
              Two: stage { }
            }
            AStage: entity { M: Text }
            """);
        var errors = Errors(generated, DomainModelDiagnosticCodes.ReservedGeneratedName);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).Contains("collides with the stage enum generated for entity 'A'");
    }

    [Test]
    public async Task PropertyAndTwoPoliciesSameName_OneReportPerNode() {
        // The action/policy carve-out is per entry: the property↔first-policy pair is reported
        // by this pass, the policy↔policy pair stays with ReportPolicyNameClashes, and the
        // second policy is not blamed twice. The stage-scoped second policy avoids the
        // same-list duplicate path (ReportDuplicateNames + ReportPolicyNameClashes both fire
        // there), which is legacy and not S-01's to change.
        var analysis = Analyze("""
            domain Scope
            A: entity {
              X: Text
              X: policy { X is "a" }
              X: policy { X is "b" }
            }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.StructuralDuplicate);
        // The property↔policy pairing is attributed once, to the first policy; the second
        // policy is skipped here and left to ReportPolicyNameClashes. Without that per-entry
        // carve-out both policies would take the pairing report.
        await Assert.That(errors.Count(d => d.Message.Contains("are both named 'X'"))).IsEqualTo(1);
        await Assert.That(errors.Any(d => d.Node is Property)).IsFalse();
        await Assert.That(errors.Count).IsGreaterThanOrEqualTo(2);
    }

    // ── S-02 (#520 #530): an enum's member list is one C# scope ──────────────

    /// <summary>
    /// A compile probe for a shape analysis accepts (S-02 H1): the real session path — parse,
    /// <c>ApplyAndAnalyze</c>, <c>session.Emit</c> — then compile the emitted module, the shape
    /// <see cref="SampleRoslynGateTests"/> uses.
    /// </summary>
    private static IReadOnlyList<Microsoft.CodeAnalysis.Diagnostic> EmitAndCompile(
        string dsl, out AnalysisResult analysis) {
        var session = DomainSession.ForSource(dsl, ExtensionCatalog.ProductAuthoring);
        var outcome = new DomainEvolution(new Domain("_", []))
            .ApplyAndAnalyze(new PolyDslParser(dsl, session).Parse(), session: session);
        if (!outcome.Succeeded)
            throw new InvalidOperationException(
                $"Probe domain has errors: {string.Join("; ", outcome.Analysis.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message))}");
        analysis = outcome.Analysis;
        var files = session.Emit(outcome.Root!, outcome.Analysis);
        return ExportedCSharp.CompileWithWarningsAsErrors("S02Probe", files);
    }

    /// <summary>
    /// A compile probe for a shape analysis rejects (S-02 H1, the <c>value__</c> member):
    /// <c>Emit</c> refuses an analysis with errors, which is the shape under probe, so this
    /// projects the same module through the ungated
    /// <see cref="DomainProgramProjection.ToSyntax(Domain, INodeMetadataProvider)"/> overload and
    /// prints it with the same <see cref="CSharpWriter"/>. No
    /// <see cref="DiagnosticAllowance"/> can express this: allowances are read only by the
    /// observed-flow and contract arms, never by <c>StructuralDomainAnalyzer</c>.
    /// </summary>
    private static IReadOnlyList<Microsoft.CodeAnalysis.Diagnostic> ProjectAndCompile(
        string dsl, out AnalysisResult analysis) {
        var session = DomainSession.ForSource(dsl, ExtensionCatalog.ProductAuthoring);
        var mutation = new DomainEvolution(new Domain("_", []))
            .Apply(new PolyDslParser(dsl, session).Parse(), session: session);
        if (!mutation.Succeeded)
            throw new InvalidOperationException($"Probe DSL did not apply: {mutation.FailureSummary}");
        analysis = session.Analyze(mutation.Root);
        var module = DomainProgramProjection.ToSyntax(mutation.Root, (INodeMetadataProvider)analysis);
        var vm = DomainSession.AnalyzeModule(module);
        var source = new CSharpWriter(vm).Write(module);
        return ExportedCSharp.CompileWithWarningsAsErrors("S02Probe", [("Probe.cs", source)]);
    }

    [Test]
    public async Task EnumMemberNamedLikeEnum_MatchesTheEmitter() {
        // H1 probe 1: `E: enum { E, A }` compiled clean, so an enum member may carry the
        // enum's own name — an enum member is a value, not a nested member (CS0542 is a class
        // and struct rule). Analysis must agree: no diagnostic, and no reserved row. The
        // entity pins the shape the emitter really sees: a property of type E.
        var diagnostics = EmitAndCompile("""
            domain Probe
            E: enum { E, A }
            X: entity { V: E default(E) }
            """, out var analysis);
        await Assert.That(string.Join(",", diagnostics.Select(d => d.Id).Distinct().Order(StringComparer.Ordinal)))
            .IsEqualTo("");
        await Assert.That(AllErrors(analysis).Count).IsEqualTo(0);

        var domain = ParseDomain("""
            domain Probe
            E: enum { E, A }
            """);
        var enumType = domain.Types.OfType<EnumType>().Single();
        await Assert.That(ReservedNames.ForEnum(enumType).Any(r => r.Name == enumType.Name)).IsFalse();
    }

    [Test]
    public async Task EnumMemberNamedValueUnderscore_MatchesTheEmitter() {
        // H1 probe 2: `E: enum { value__ }` is CS0076 — every enum type carries that backing
        // field. Analysis agrees with exactly one DMSTR009 on the enum node.
        var diagnostics = ProjectAndCompile("""
            domain Probe
            E: enum { value__ }
            """, out var analysis);
        await Assert.That(string.Join(",", diagnostics.Select(d => d.Id).Distinct().Order(StringComparer.Ordinal)))
            .IsEqualTo("CS0076");

        var errors = Errors(analysis, DomainModelDiagnosticCodes.ReservedMemberName);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Node is EnumType).IsTrue();
        await Assert.That((errors[0].Node as EnumType)!.Name).IsEqualTo("E");
        await Assert.That(errors[0].Message).Contains("'value__'");
        await Assert.That(errors[0].Message).Contains("CS0076");
    }

    [Test]
    public async Task EnumMembersDifferingOnlyByCase_Compile() {
        // H1 probe 3, and the compile proof FORK-1 rests on: enum member identity is verbatim,
        // so `Active` and `active` are two distinct C# enum values. DMSTR008's own doc is about
        // two names that derive one identifier, which is an entity-scope shape.
        var diagnostics = EmitAndCompile("""
            domain Probe
            E: enum { Active, active }
            """, out var analysis);
        await Assert.That(string.Join(",", diagnostics.Select(d => d.Id).Distinct().Order(StringComparer.Ordinal)))
            .IsEqualTo("");
        await Assert.That(AllErrors(analysis).Count).IsEqualTo(0);
    }

    [Test]
    public async Task EnumMembersDifferingOnlyByCase_IsAccepted() {
        var analysis = Analyze("""
            domain Scope
            E: enum { Active, active }
            """);

        await Assert.That(AllErrors(analysis).Count).IsEqualTo(0);
    }

    [Test]
    public async Task DuplicateEnumMemberNamedLikeEnum_ReportsOnce() {
        // H2: the duplicate group owns the name, so the reserved check does not also fire.
        var analysis = Analyze("""
            domain Scope
            E: enum { E, E }
            """);

        var duplicate = Errors(analysis, DomainModelDiagnosticCodes.StructuralDuplicate);
        await Assert.That(duplicate.Count).IsEqualTo(1);
        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.ReservedMemberName).Count).IsEqualTo(0);
        await Assert.That(AllErrors(analysis).Count).IsEqualTo(1);
    }

    [Test]
    public async Task EnumScope_ReportsAuthoredNamesInDeclarationOrderWithOneBasedIndexes() {
        var domain = ParseDomain("""
            domain Scope
            E: enum { A, B, C }
            """);

        var scope = NameScopes.EnumScope(domain.Types.OfType<EnumType>().Single());
        await Assert.That(scope.Count).IsEqualTo(3);
        await Assert.That(string.Join(",", scope.Select(entry => $"{entry.Name}:{entry.Index}")))
            .IsEqualTo("A:1,B:2,C:3");
    }

    [Test]
    public async Task DuplicateEnumMember_IsPositionedError() {
        var analysis = Analyze("""
            domain Scope
            E: enum { A, B, A }
            X: entity { V: E default(A) }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.StructuralDuplicate);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Node is EnumType).IsTrue();
        await Assert.That((errors[0].Node as EnumType)!.Name).IsEqualTo("E");
        await Assert.That(errors[0].Message).Contains("'A'");
        await Assert.That(errors[0].Message).Contains("enum 'E'");
        await Assert.That(errors[0].Message).Contains("member 1");
        await Assert.That(errors[0].Message).Contains("member 3");
    }

    [Test]
    public async Task DuplicateEnumMember_ThroughEvolution_IsPositionedError() {
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze([new AddEnumTypeChange("E", ["A", "B", "A"])]);

        var errors = result.Analysis.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error
                && d.Code == DomainModelDiagnosticCodes.StructuralDuplicate)
            .ToList();
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Node is EnumType).IsTrue();
        await Assert.That(errors[0].Message).Contains("member 3");
    }

    [Test]
    public async Task InterleavedDuplicateEnumMembers_ReportTwoDistinctMessages() {
        var analysis = Analyze("""
            domain Scope
            E: enum { A, B, A, B }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.StructuralDuplicate);
        await Assert.That(errors.Count).IsEqualTo(2);
        await Assert.That(errors.Select(d => d.Message).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(2);
    }

    [Test]
    public async Task TripleDuplicateEnumMember_ReportsEachRepeat() {
        var analysis = Analyze("""
            domain Scope
            E: enum { A, A, A }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.StructuralDuplicate);
        await Assert.That(errors.Count).IsEqualTo(2);
    }

    [Test]
    public async Task EmptyEnum_HasNoDiagnostic() {
        var analysis = Analyze("""
            domain Scope
            E: enum { }
            """);

        await Assert.That(AllErrors(analysis).Count).IsEqualTo(0);
    }
}