using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// S-04b: the Lower oracle for <see cref="StageEntryFacts.EntryAssignedProperties"/>.
/// <para>
/// Every fixture analyzes clean (<see cref="EvolvedDomain.FromDsl"/> throws with the
/// diagnostics otherwise) and is projected through <see cref="DomainProgramProjection.ToSyntax(Domain, AnalysisResult)"/>.
/// The oracle then collects every <c>this.P = …</c> in the body-bearing constructor as a
/// generic tree walk. It excludes <c>CurrentStage</c>, skips only the constructor's own head
/// slots (top-level statements before <c>this.CurrentStage = …</c>) and only by value (the
/// right-hand side holds a constructor <see cref="Parameter"/>), and follows a self
/// <c>this.Action()</c> invoke into the method body with a visited set. Names are compared over
/// <see cref="Entity.Properties"/>. Each fixture also pins the expected set, so the oracle
/// cannot pass by agreeing on an empty set.
/// </para>
/// <para>
/// The fixtures cover these cursor shapes of the merged Lower cursor (#765), not every possible
/// combination (cross-entity and fan-out reach-back, and an exit-block target override, are accepted
/// residuals): each hop exits its
/// own source, the cursor moves before the target's entry, the else branch restarts from the
/// conditional's entry stage, and the cursor is dynamic (the per-stage exit switch) after a
/// hop-holding branch or <c>for</c>, after every self-invoke, and inside a <c>for</c> body.
/// </para>
/// </summary>
public class StageEntryFactsParityTests {
    private static (HashSet<string> Oracle, IReadOnlySet<string> Fact) Compute(string dsl, string entityName) {
        var (domain, analysis) = EvolvedDomain.FromDsl(dsl);
        var entity = domain.Types.OfType<Entity>().Single(e => e.Name == entityName);
        return (LowerOracle(domain, analysis, entity), StageEntryFacts.EntryAssignedProperties(entity, domain));
    }

    private static async Task AssertParity(string dsl, string entityName, params string[] expected) {
        var (oracle, fact) = Compute(dsl, entityName);

        await Assert.That(oracle).IsEquivalentTo(expected, StringComparer.Ordinal);
        await Assert.That(fact).IsEquivalentTo(oracle, StringComparer.Ordinal);
    }

    private static HashSet<string> LowerOracle(Domain domain, AnalysisResult analysis, Entity entity) {
        var type = DomainProgramProjection.ToSyntax(domain, analysis).Single(t => t.Name == entity.Name);
        var ctor = type.Constructors!.Single(c => c.Body is not null);
        var ctorParameters = (ctor.Parameters ?? []).Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var methods = (type.Methods ?? []).ToLookup(m => m.Name, StringComparer.Ordinal);
        var propertyNames = entity.Properties.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        var assigned = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        void Collect(Node? node) {
            if (node is null)
                return;
            switch (node) {
                case Assignment { Destination: Member { Value: ThisReference } target }:
                    assigned.Add(target.MemberName);
                    break;
                case Invoke { Delegate: Member { Value: ThisReference } callee }
                    when methods.Contains(callee.MemberName) && visited.Add(callee.MemberName):
                    foreach (var method in methods[callee.MemberName])
                        Collect(method.Body);
                    break;
            }
            foreach (var child in node.Children)
                Collect(child);
        }

        var statements = ctor.Body is Block block ? block.Nodes : [ctor.Body!];
        var inHead = true;
        foreach (var statement in statements) {
            if (statement is Assignment { Destination: Member { Value: ThisReference, MemberName: "CurrentStage" } })
                inHead = false;
            if (inHead && statement is Assignment { Destination: Member { Value: ThisReference } } && HoldsParameter(statement, ctorParameters))
                continue;
            Collect(statement);
        }

        assigned.Remove("CurrentStage");
        assigned.IntersectWith(propertyNames);
        return assigned;
    }

    private static bool HoldsParameter(Node node, IReadOnlySet<string> ctorParameters) =>
        node is Parameter p && ctorParameters.Contains(p.Name)
        || node.Children.Any(child => child is not null && HoldsParameter(child, ctorParameters));

    // ── Baseline: the entry chain only (green before and after the widening) ─────────────

    [Test]
    public Task Parity_TopLevelEntryAssign() => AssertParity("""
        domain P1
        Z: entity {
          N: Number
          A: stage { entry { assign N to 1 } }
        }
        """, "Z", "N");

    [Test]
    public Task Parity_NestedConditionalEntryAssigns() => AssertParity("""
        domain P2
        Z: entity {
          N: Number
          M: Number
          A: stage { entry { if (N > 0) { assign N to 1 } else { assign M to 2 } } }
        }
        """, "Z", "N", "M");

    [Test]
    public Task Parity_OneHopEntryChain() => AssertParity("""
        domain P3
        Z: entity {
          M: Number
          A: stage { entry { transition to B } }
          B: stage { entry { assign M to 2 } }
        }
        """, "Z", "M");

    [Test]
    public Task Parity_LeafStageExit_NeverDeparted_IsNotAssigned() => AssertParity("""
        domain P4
        Z: entity {
          N: Number
          X: Number default(0)
          A: stage {
            entry { assign N to 1 }
            exit { assign X to 1 }
          }
        }
        """, "Z", "N");

    // ── Exit cursor ──────────────────────────────────────────────────────────────────────

    [Test]
    public Task Parity_NestedEntryChain_ExitsEachSourceOnce() => AssertParity("""
        domain P5
        Z: entity {
          N: Number
          X: Number default(0)
          Y: Number default(0)
          Q: Number default(0)
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
        """, "Z", "N", "X", "Y");

    [Test]
    public Task Parity_SequentialHops_SecondExitsFirstTarget() => AssertParity("""
        domain P6
        Z: entity {
          X: Number default(0)
          Y: Number default(0)
          Q: Number default(0)
          A: stage {
            entry { transition to B  transition to C }
            exit { assign X to 1 }
          }
          B: stage { exit { assign Y to 1 } }
          C: stage { exit { assign Q to 1 } }
        }
        """, "Z", "X", "Y");

    [Test]
    public Task Parity_GuardedHopThenHop_CursorGoesDynamic() => AssertParity("""
        domain P7
        Z: entity {
          Flag: Boolean
          X: Number default(0)
          Y: Number default(0)
          W: Number default(0)
          A: stage {
            entry {
              if (Flag is true) { transition to B }
              transition to C
            }
            exit { assign X to 1 }
          }
          B: stage { exit { assign Y to 1 } }
          C: stage { }
          D: stage { exit { assign W to 1 } }
        }
        """, "Z", "X", "Y", "W");

    [Test]
    public Task Parity_ElseBranchHop_StartsFromConditionalEntryStage() => AssertParity("""
        domain P8
        Z: entity {
          Flag: Boolean
          X: Number default(0)
          Y: Number default(0)
          Q: Number default(0)
          A: stage {
            entry {
              if (Flag is true) { transition to B } else { transition to C }
            }
            exit { assign X to 1 }
          }
          B: stage { exit { assign Y to 1 } }
          C: stage { exit { assign Q to 1 } }
        }
        """, "Z", "X");

    [Test]
    public Task Parity_ForBodyHop_StartsWithDynamicCursor() => AssertParity("""
        domain P9
        Item: entity { }
        Z: entity {
          Items: many Item
          X: Number default(0)
          Y: Number default(0)
          A: stage {
            entry { for i in Items { transition to B } }
            exit { assign X to 1 }
          }
          B: stage { exit { assign Y to 1 } }
        }
        """, "Z", "X", "Y");

    [Test]
    public Task Parity_ForBodySelfInvoke_ThenHop_CursorGoesDynamic() => AssertParity("""
        domain P10
        Item: entity { }
        Z: entity {
          Items: many Item
          M: Number default(0)
          X: Number default(0)
          Y: Number default(0)
          Bump: action { assign M to 1 }
          A: stage {
            entry {
              for i in Items { invoke Bump }
              transition to B
            }
            exit { assign X to 1 }
          }
          B: stage { exit { assign Y to 1 } }
        }
        """, "Z", "M", "X", "Y");

    [Test]
    public Task Parity_SelfInvokeThenHop_CursorGoesDynamic() => AssertParity("""
        domain P11
        Z: entity {
          M: Number default(0)
          X: Number default(0)
          Y: Number default(0)
          Bump: action { assign M to 1 }
          A: stage {
            entry {
              invoke Bump
              transition to B
            }
            exit { assign X to 1 }
          }
          B: stage { exit { assign Y to 1 } }
        }
        """, "Z", "M", "X", "Y");

    [Test]
    public Task Parity_TransitiveSelfInvoke_FollowsCalleeBodies() => AssertParity("""
        domain P12
        Z: entity {
          M: Number default(0)
          X: Number default(0)
          Y: Number default(0)
          Bump: action { assign M to 1 }
          Mid: action { invoke Bump }
          A: stage {
            entry {
              invoke Mid
              transition to B
            }
            exit { assign X to 1 }
          }
          B: stage { exit { assign Y to 1 } }
        }
        """, "Z", "M", "X", "Y");

    [Test]
    public Task Parity_TransitionInsideExit_IsFollowed() => AssertParity("""
        domain P13
        Z: entity {
          X: Number default(0)
          Q: Number default(0)
          A: stage {
            entry { transition to B }
            exit {
              assign X to 1
              transition to C
            }
          }
          B: stage { }
          C: stage { entry { assign Q to 1 } }
        }
        """, "Z", "X", "Q");

    [Test]
    public Task Parity_GuardedEntryCycle_TerminatesWithInitialUnpushed() => AssertParity("""
        domain P14
        Z: entity {
          Flag: Boolean
          N: Number
          M: Number
          X: Number default(0)
          Y: Number default(0)
          A: stage {
            entry { assign N to 1  transition to B }
            exit { assign X to 1 }
          }
          B: stage {
            entry {
              assign M to 2
              if (Flag is true) { transition to A }
            }
            exit { assign Y to 1 }
          }
        }
        """, "Z", "N", "M", "X", "Y");

    [Test]
    public Task Parity_NonTransitioningBranchAndFor_KeepCursor() => AssertParity("""
        domain P18
        Item: entity { }
        Z: entity {
          Flag: Boolean
          Items: many Item
          N: Number
          X: Number default(0)
          Y: Number default(0)
          W: Number default(0)
          A: stage {
            entry {
              if (Flag is true) { assign N to 1 }
              for i in Items { assign N to 2 }
              transition to B
            }
            exit { assign X to 1 }
          }
          B: stage { exit { assign Y to 1 } }
          C: stage { exit { assign W to 1 } }
        }
        """, "Z", "N", "X");

    // ── G7 lifecycle ─────────────────────────────────────────────────────────────────────

    [Test]
    public Task Parity_LifecycleProperty_AssignedPerResolvedHop() => AssertParity("""
        domain P15
        OrderState: enum { Pending, Paid }
        Z: entity {
          N: Number
          State: OrderState default(Pending)
          Pending: stage { entry { assign N to 1  transition to Paid } }
          Paid: stage { }
        }
        """, "Z", "N", "State");

    [Test]
    public Task Parity_LifecycleProperty_TargetEntryAssignsIt_SameSet() => AssertParity("""
        domain P16
        OrderState: enum { Pending, Paid }
        Z: entity {
          N: Number
          State: OrderState default(Pending)
          Pending: stage { entry { assign N to 1  transition to Paid } }
          Paid: stage { entry { assign State to Paid } }
        }
        """, "Z", "N", "State");

    [Test]
    public Task Parity_LifecycleProperty_SingleStageNoHop_NotAssigned() => AssertParity("""
        domain P17
        OneState: enum { Only }
        Z: entity {
          N: Number
          State: OneState default(Only)
          Only: stage { entry { assign N to 1 } }
        }
        """, "Z", "N");

    // F-1, a recorded gap: a transition inside an invoked action is not followed, so the fact
    // is a strict subset of Lower here. May-assign; not for definite-initialization decisions.
    // S-14's lock must acknowledge this before it consumes the fact.
    [Test]
    public async Task Characterization_InvokedActionTransition_NotInFact() {
        var (oracle, fact) = Compute("""
            domain Gap
            Z: entity {
              X: Number default(0)
              Q: Number default(0)
              Go: action { transition to B }
              A: stage {
                entry { invoke Go }
                exit { assign X to 1 }
              }
              B: stage { entry { assign Q to 1 } }
            }
            """, "Z");

        await Assert.That(oracle).IsEquivalentTo(new[] { "X", "Q" }, StringComparer.Ordinal);
        await Assert.That(fact.All(oracle.Contains)).IsTrue();
        await Assert.That(fact.Count).IsLessThan(oracle.Count);
    }
}