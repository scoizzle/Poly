using Poly.DomainModeling;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.Interpretation.CSharp;

using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// #611 and #634: an absent <c>optional</c> <c>unique</c> value is not a key. <c>Create</c> checks
/// and records the value only when it is present, so any number of instances may omit it and the
/// port never receives a null value's string. Both hosts run the one lowered <c>Create</c>.
/// </summary>
public class OptionalUniqueCreateParityTests {
    const string Dsl = """
        domain M
        uses persistence

        Thing: entity {
          Name: Text length(2, 10)
          Tag: Text unique optional
        }
        """;

    [Test]
    public async Task Create_WhenOptionalUniqueTextIsOmitted_SucceedsAndIsNotRecorded() {
        var outcomes = await ParityScenario.FromDsl(Dsl, "ParityOptionalUniqueCreate")
            .AssertAgree(side => {
                side.Create("Thing", ("Name", "aa"));
                side.Create("Thing", ("Name", "cc"), ("Tag", "t"));
                side.Create("Thing", ("Name", "dd"));
                side.Create("Thing", ("Name", "ee"), ("Tag", "t"));
            });

        await Assert.That(outcomes[0].Success).IsTrue()
            .Because($"create: {outcomes[0].ExceptionType} {outcomes[0].Message}");
        await Assert.That(outcomes[1].Success).IsTrue()
            .Because($"create: {outcomes[1].ExceptionType} {outcomes[1].Message}");
        await Assert.That(outcomes[2].Success).IsTrue()
            .Because("a second instance may omit an optional unique value; null is never a key");
        await Assert.That(outcomes[3].Success).IsFalse()
            .Because("a present value is still checked");
        await Assert.That(outcomes[3].Message)
            .IsEqualTo("Unique constraint violated: 'Tag' value is already used on another 'Thing'.");
    }

    /// <summary>
    /// #634's second symptom: the printed <c>Create</c> called <c>tag.ToString()</c> on the nullable
    /// parameter, which is CS8602 under <c>#nullable enable</c>. The port call lives inside the
    /// presence guard, so the module compiles with warnings as errors.
    /// </summary>
    [Test]
    public async Task Export_WhenOptionalUniqueTextIsOmitted_CompilesCleanAndGuardsThePort() {
        var session = ParitySessions.Create();
        var changes = new PolyDslParser(Dsl, session).Parse();
        var evolved = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes, session: session);
        await Assert.That(evolved.Succeeded).IsTrue();

        var cs = new CSharpGenerator().Generate(session.Lower(evolved.Root!, evolved.Analysis));
        var errors = ExportedCSharp.CompileWithWarningsAsErrors("OptionalUniqueCreate",
            [("M.cs", "#nullable enable\n" + ExportedCSharp.MissingPolyRuntimeUsings(cs) + cs)]);

        await Assert.That(string.Join("\n", errors.Select(e => e.ToString()))).IsEqualTo("");
        var create = ExportedCSharp.ExtractMethod(cs, "DomainResult<Thing> __BodyOfCreate");
        await Assert.That(create).Contains("if (tag != null)");
        await Assert.That(create).DoesNotContain("tag.ToString()");
    }
}