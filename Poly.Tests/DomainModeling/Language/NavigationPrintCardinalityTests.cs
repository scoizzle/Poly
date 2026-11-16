using Poly.DomainModeling;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Language;

/// <summary>
/// Q11 (theme #678, slice G-01): the printer emits <c>one </c> for a one-to-one
/// navigation, and only where the printed text parses back to the same cardinality.
///
/// <para>Each pin holds three things at once: the canonical text carries the
/// cardinality keyword, <c>print(parse(print(parse(x))))</c> is a fixed point, and the
/// navigation's cardinality on the reprinted domain equals the one the source declared.
/// The three shapes are the ones the N1 grammar can express: <c>one T</c> (one-to-one),
/// a bare <c>x: T</c> (which the resolver reads as one-to-one), and <c>one owned T</c>
/// in the token order <see cref="DomainResolver"/> accepts.</para>
///
/// <para>The one-to-one case uses a target whose lowercased name is a Phase 1a
/// unsupported keyword (<c>Schedule</c>, on the <c>_unsupportedKeywords</c> list). That
/// is the parked corpus defect: printing the navigation bare turns it into a bare typed
/// property line, and <c>CheckUnsupportedKeyword</c> then rejects it on the next read.</para>
/// </summary>
public sealed class NavigationPrintCardinalityTests {
    /// <summary>
    /// <c>one Schedule</c> survives the write/read/write cycle, and the navigation is
    /// still one-to-one. On HEAD the printer drops <c>one </c>, so the reprint is
    /// <c>schedule: Schedule</c> and the second read throws a <c>FormatException</c>.
    /// </summary>
    [Test]
    public async Task Print_OneToOneWithReservedWordTarget_IsIdempotentAndKeepsCardinality() {
        const string dsl = """
            domain OneToOneNav
            Doctor: entity {
              Name: Text
              schedule: one Schedule
            }
            Schedule: entity {
              Day: Text
            }
            """;

        await AssertCardinalityPin(dsl, "Doctor", "schedule", RelationshipCardinality.OneToOne, "schedule: one Schedule");
    }

    /// <summary>
    /// The bare-source case: a typed line <c>x: T</c> is how the resolver takes a singular
    /// reference (it reads as one-to-one, not ManyToOne), so the printer writes <c>one T</c>
    /// and the second read is one-to-one again.
    /// </summary>
    [Test]
    public async Task Print_BareTypedLineSource_PrintsOneAsOneToOne() {
        const string dsl = """
            domain BareNav
            Visit: entity {
              Reason: Text
              patient: Patient
            }
            Patient: entity {
              Name: Text
            }
            """;

        await AssertCardinalityPin(dsl, "Visit", "patient", RelationshipCardinality.OneToOne, "patient: one Patient");
    }

    /// <summary>
    /// The owned singular shape: <c>one owned T</c>, the token order the parser accepts,
    /// keeps the ownership facet and the cardinality across the cycle.
    /// </summary>
    [Test]
    public async Task Print_OneOwnedOneToOne_IsIdempotentAndKeepsCardinality() {
        const string dsl = """
            domain OwnedNav
            Order: entity {
              Number: Text
              profile: one owned Profile optional
            }
            Profile: entity {
              City: Text
            }
            """;

        await AssertCardinalityPin(dsl, "Order", "profile", RelationshipCardinality.OneToOne, "profile: one owned Profile");

        var (first, _) = Generate(dsl);
        var owned = Navigation(first.Root, "Order", "profile");
        await Assert.That(owned.SourceOwnsTarget).IsTrue()
            .Because("`one owned T` keeps the ownership facet on the navigations it prints");
    }

    /// <summary>
    /// Regression pin: the <c>many</c> branch is untouched, so a collection navigation
    /// still prints <c>many </c> and stays one-to-many.
    /// </summary>
    [Test]
    public async Task Print_OneToMany_StillPrintsManyAndKeepsCardinality() {
        const string dsl = """
            domain OneToManyNav
            Patient: entity {
              Name: Text
              visits: many Visit
            }
            Visit: entity {
              Reason: Text
            }
            """;

        await AssertCardinalityPin(dsl, "Patient", "visits", RelationshipCardinality.OneToMany, "visits: many Visit");
    }

    /// <summary>
    /// A <see cref="RelationshipCardinality.ManyToOne"/> navigation — code-built only, since
    /// the DSL has no spelling for it — keeps the bare form. <c>one T</c> would read back as
    /// one-to-one, a cardinality the source never claimed, so the Q11 condition withholds the
    /// keyword here; the pin exists so a later widening of the printer arm is a red.
    /// </summary>
    [Test]
    public async Task Print_ManyToOneNavigation_KeepsBareForm() {
        const string dsl = """
            domain ManyToOneNav
            Order: entity {
              Code: Text
            }
            Customer: entity {
              Name: Text
            }
            """;

        var parsed = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser(dsl).Parse());
        await Assert.That(parsed.Succeeded).IsTrue().Because($"{parsed.FailureSummary}");

        var added = new DomainEvolution(parsed.Root).ApplyAndAnalyze([
            new AddRelationshipChange(
                "customer",
                new DomainTypeReference("Order"),
                new DomainTypeReference("Customer"),
                RelationshipCardinality.ManyToOne,
                [])]);
        await Assert.That(added.Succeeded).IsTrue().Because($"{added.FailureSummary}");

        var printed = new DomainDslPrinter().Print(added.Root);
        await Assert.That(printed).Contains("customer: Customer").Because(printed);
        await Assert.That(printed).DoesNotContain("customer: one Customer").Because(printed);
    }

    /// <summary>
    /// One pin: the first print carries the expected navigation line, the second print is
    /// the same text, and the navigation's cardinality is unchanged on both domains.
    /// </summary>
    private static async Task AssertCardinalityPin(
        string dsl,
        string entityName,
        string navName,
        RelationshipCardinality expected,
        string expectedLine) {
        var (first, firstText) = Generate(dsl);
        await Assert.That(first.Succeeded).IsTrue().Because($"{first.FailureSummary}");

        await Assert.That(firstText).Contains(expectedLine)
            .Because($"the canonical text carries the cardinality the source declared{Environment.NewLine}{firstText}");

        var (second, secondText) = Generate(firstText);
        await Assert.That(second.Succeeded).IsTrue().Because($"{second.FailureSummary}");
        await Assert.That(secondText).IsEqualTo(firstText).Because("print(parse(print(parse(x)))) is a fixed point");

        await Assert.That(Navigation(first.Root, entityName, navName).Cardinality).IsEqualTo(expected);
        await Assert.That(Navigation(second.Root, entityName, navName).Cardinality).IsEqualTo(expected);
    }

    private static Relationship Navigation(Domain root, string entityName, string navName) =>
        root.Types.OfType<Entity>().Single(e => e.Name == entityName)
            .Navigations.Single(n => n.Name == navName);

    /// <summary>Parses, analyses and prints one generation of the document.</summary>
    private static (EvolutionResult Result, string Text) Generate(string poly) {
        var session = DomainSession.Create(DslSyntaxTree.Parse(poly).Uses, SessionOptions.Bare(ExtensionCatalog.Core));
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser(poly, session).Parse(), session: session);
        return (result, new DomainDslPrinter(session).Print(result.Root));
    }
}