using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// T3 (#469): the mutation-by-invariant matrix. One row per invariant kind the
/// pipeline-convergence plan names — required, range, length, pattern, equality,
/// unique, enum, cardinality. Each row applies one deliberate mutation (a create,
/// an action assign, a stage entry effect, or a <c>create in</c> child) that breaks
/// exactly that invariant, and asserts two things in both simulation and printed C#:
/// <list type="number">
/// <item>the declaring domain rejects the mutation with that invariant's exact
/// message, and the two sides agree on the outcome (the T2 parity helper);</item>
/// <item>the same mutation is accepted by the same domain with the invariant
/// removed, so the rejection is attributable to that invariant and not to a
/// neighbouring guard.</item>
/// </list>
/// A row that stops rejecting its mutation is an undetected mutation. The issue
/// asks for that gap to be recorded, not for production code to be relaxed.
/// </summary>
public sealed class MutationInvariantMatrixTests {
    /// <summary>The invariant kinds the plan's T3 names, in its order.</summary>
    static readonly string[] PlanInvariants =
        ["required", "range", "length", "pattern", "equality", "unique", "enum", "cardinality"];

    /// <summary>
    /// One matrix row: the invariant, the two domains, the mutation, and the exact
    /// failure it must produce. <paramref name="PrintedFailure"/> is set only where the
    /// two sides must name the offending value in their own representation; then the
    /// failure text is the one pinned divergence and every other field still agrees.
    /// </summary>
    public sealed record Row(
        string Invariant,
        string DeclaredDsl,
        string LoosenedDsl,
        Action<ParitySide> Mutation,
        string DeclaredFailure,
        string? PrintedFailure = null);

    public static IEnumerable<Row> Rows() {
        // required — create omits a required Text property.
        yield return new("required", """
            domain Matrix
            Widget: entity {
              Name: Text
            }
            """, """
            domain Matrix
            Widget: entity {
              Name: Text optional
            }
            """,
            side => side.Create("Widget"),
            "'Name' is required.");

        // range — the first stage's entry effect assigns above the maximum.
        yield return new("range", """
            domain Matrix
            Widget: entity {
              Score: Number range(1, 10) default(5)
              Bump: Number default(100)
              Draft: stage {
                entry { assign Score to Bump }
              }
            }
            """, """
            domain Matrix
            Widget: entity {
              Score: Number default(5)
              Bump: Number default(100)
              Draft: stage {
                entry { assign Score to Bump }
              }
            }
            """,
            side => side.Create("Widget"),
            "'Score' must be <= 10.");

        // length — an action assign supplies a value below the minimum.
        yield return new("length", """
            domain Matrix
            Person: entity {
              Name: Text length(3, 10)
              Rename: action (name: Text) {
                assign Name to name
              }
            }
            """, """
            domain Matrix
            Person: entity {
              Name: Text
              Rename: action (name: Text) {
                assign Name to name
              }
            }
            """,
            side => {
                side.Create("Person", ("Name", "Ada"));
                side.Invoke("Rename", ("name", "ab"));
            },
            "'Name' must be at least 3 characters.");

        // pattern — an action assign supplies text that misses the regex.
        yield return new("pattern", """
            domain Matrix
            Tag: entity {
              Code: Text pattern("^[A-Z]{2}$")
              Relabel: action (code: Text) {
                assign Code to code
              }
            }
            """, """
            domain Matrix
            Tag: entity {
              Code: Text
              Relabel: action (code: Text) {
                assign Code to code
              }
            }
            """,
            side => {
                side.Create("Tag", ("Code", "AB"));
                side.Invoke("Relabel", ("code", "a1"));
            },
            "'Code' does not match the required pattern.");

        // equality — create supplies a value other than the only legal one.
        yield return new("equality", """
            domain Matrix
            Order: entity {
              Status: Text equals("Active")
            }
            """, """
            domain Matrix
            Order: entity {
              Status: Text
            }
            """,
            side => side.Create("Order", ("Status", "Closed")),
            "'Status' must equal Active.");

        // unique — a second create reuses a key the storage port recorded.
        yield return new("unique", """
            domain Matrix
            uses persistence
            Widget: entity {
              Code: Text unique
            }
            """, """
            domain Matrix
            Widget: entity {
              Code: Text
            }
            """,
            side => {
                side.Create("Widget", ("Code", "A"));
                side.Create("Widget", ("Code", "A"));
            },
            "Unique constraint violated: 'Code' value is already used on another 'Widget'.");

        // enum — create supplies a value that is not in the declared member set. The loosened
        // domain declares that value, so the same mutation is legal. The VM carries the
        // non-member as text and the CLR enum as its numeric value, so each side names the
        // value differently; the rejection itself is the same on both.
        yield return new("enum", """
            domain Matrix
            Tier: enum { Basic, Plus, Elite }
            Member: entity {
              Name: Text
              Level: Tier
            }
            """, """
            domain Matrix
            Tier: enum { Basic, Plus, Elite, Gold }
            Member: entity {
              Name: Text
              Level: Tier
            }
            """,
            side => side.Create("Member", ("Name", "Cy"), ("Level", "Gold")),
            "'Gold' is not a valid member of Tier for property 'Level'.",
            "'42' is not a valid member of Tier for property 'Level'.");

        // cardinality — a one-navigation (cardinality one) takes a second child.
        yield return new("cardinality", """
            domain Matrix
            Child: entity {
              Name: Text
            }
            Parent: entity {
              Name: Text
              kid: Child
              AddKid: action { create in kid { Name: "x" } }
            }
            """, """
            domain Matrix
            Child: entity {
              Name: Text
            }
            Parent: entity {
              Name: Text
              kids: many Child
              AddKid: action { create in kids { Name: "x" } }
            }
            """,
            side => {
                side.Create("Parent", ("Name", "Pat"));
                side.Invoke("AddKid");
                side.Invoke("AddKid");
            },
            Relationship.LinkViolationMessage("kid"));
    }

    public static IEnumerable<string> RowNames() => Rows().Select(row => row.Invariant);

    [Test]
    [MethodDataSource(nameof(RowNames))]
    public async Task Mutation_IsRejectedByItsOwnInvariant_InSimulationAndPrintedCSharp(string invariant) {
        var row = Rows().Single(candidate => candidate.Invariant == invariant);
        var assemblyName = "Matrix" + char.ToUpperInvariant(invariant[0]) + invariant[1..];

        var (simulate, printed) = ParityScenario.FromDsl(row.DeclaredDsl, assemblyName + "Declared")
            .Run(row.Mutation);
        await Assert.That(simulate[^1].Success).IsFalse()
            .Because($"{invariant}: simulation must reject the deliberate mutation.");
        await Assert.That(printed[^1].Success).IsFalse()
            .Because($"{invariant}: printed C# must reject the deliberate mutation.");
        await Assert.That(simulate[^1].Message).IsEqualTo(row.DeclaredFailure)
            .Because($"{invariant}: the simulated failure must be the invariant's own message, exactly.");
        await Assert.That(printed[^1].Message).IsEqualTo(row.PrintedFailure ?? row.DeclaredFailure)
            .Because($"{invariant}: the printed failure must be the invariant's own message, exactly.");

        var differences = ParityScenario.Differences(simulate, printed);
        if (row.PrintedFailure is null) {
            await Assert.That(differences).IsEmpty()
                .Because($"{invariant}: simulation and printed C# must agree on the whole outcome.");
        }
        else {
            // The failure text is the one pinned divergence: each side names the offending
            // value in its own representation. Success, step count and state still agree.
            await Assert.That(differences.Count).IsEqualTo(1)
                .Because($"{invariant}: the failure text is the only pinned divergence.");
            await Assert.That(differences[0]).Contains("failure message differs");
        }

        var loosened = await ParityScenario.FromDsl(row.LoosenedDsl, assemblyName + "Loosened")
            .AssertAgree(row.Mutation);
        await Assert.That(loosened.All(outcome => outcome.Success)).IsTrue()
            .Because($"{invariant}: without the invariant the same mutation must be accepted, "
                + "otherwise the rejection above is not attributable to it.");
    }

    /// <summary>
    /// No invariant the plan names is silently untested: the matrix's rows must cover
    /// the plan's list exactly. Deleting a row turns this red.
    /// </summary>
    [Test]
    public async Task Matrix_CoversEveryInvariantThePlanNames() {
        var covered = RowNames().OrderBy(name => name, StringComparer.Ordinal).ToArray();
        var planned = PlanInvariants.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        await Assert.That(covered).IsEquivalentTo(planned);
        await Assert.That(covered.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(covered.Length)
            .Because("each invariant has exactly one row");
    }
}