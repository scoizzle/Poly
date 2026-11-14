using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.DomainModeling;

/// <summary>
/// Combined dogfood: one University domain covering enroll/create-in, require-not,
/// Rel-exists, subscriptions, for-invoke, same-stage dispatch, unique, pattern,
/// enums, and export compile.
/// </summary>
public class UniversityDogfoodTests {
    private static string PolyText() {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(root, "docs/probes/dogfood/university.poly"));
    }

    private static (Domain Domain, AnalysisResult Analysis) Evolve() {
        var changes = new PolyDslParser(PolyText()).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
        if (!result.Succeeded) {
            var errors = string.Join("; ", result.Analysis.Diagnostics
                .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                .Select(d => d.Message));
            throw new InvalidOperationException($"Evolution failed: {errors}");
        }
        var analysis = DomainModelAnalyzer.Analyze(result.Root!);
        if (analysis.HasErrors) {
            var errors = string.Join("; ", analysis.Diagnostics
                .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                .Select(d => d.Message));
            throw new InvalidOperationException($"Analysis failed: {errors}");
        }
        return (result.Root!, analysis);
    }

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

    [Test]
    public async Task University_Export_Compiles() {
        var (domain, analysis) = Evolve();
        var types = DomainProgramProjection.ToSyntax(domain, analysis);
        var cs = new CSharpGenerator().Generate(types);

        await Assert.That(cs).DoesNotContain("void Notify(string stageName)");
        await Assert.That(cs).Contains("Instructor? instructor = default");
        await Assert.That(cs).Contains("IEnumerable<Enrollment>? enrollments = default");
        await Assert.That(cs).Contains("void AttachEnrollments");
        await Assert.That(cs).Contains("section.AttachEnrollments");
        await Assert.That(cs).Contains("this.Waitlist.Count");
        await Assert.That(cs).Contains("this.Enrollments.Count");
        await Assert.That(cs).DoesNotContain("Policy 'HasWaiters' requires store-aware");
        await Assert.That(cs).Contains("if (score >= 70L)");
        await Assert.That(cs).Contains("WhenEachEnrollmentCompleted");

        var tree = CSharpSyntaxTree.ParseText("#nullable enable\n" + cs);
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            ?.Split(Path.PathSeparator)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToArray() ?? [];
        var compilation = CSharpCompilation.Create(
            "UniversityExport",
            [tree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToArray();
        await Assert.That(errors).IsEmpty();
    }

    [Test]
    public async Task University_Runtime_WalksEnrollConfirmDropAndGuards() {
        var (domain, _) = Evolve();
        var exec = ModuleExec.For(domain);
        Entity E(string n) => domain.Types.OfType<Entity>().First(t => t.Name == n);

        var instructor = exec.Create("Instructor", new Dictionary<string, object?> {
            ["Name"] = "Kim",
            ["Email"] = "kim@uni.test"
        }).Instance!;
        var course = exec.Create("Course", new Dictionary<string, object?> {
            ["Code"] = "CS101",
            ["Title"] = "Intro"
        }).Instance!;
        // L-15a: Section.course is a required to-one; Section.instructor is optional (a section is unstaffed
        // until it is assigned), so Close is blocked by HasInstructor until the instructor is linked.
        var section = exec.Create("Section", new Dictionary<string, object?> {
            ["Cap"] = 1L,
            ["OfferingTerm"] = "Fall",
            ["course"] = course
        }).Instance!;
        var student = exec.Create("Student", new Dictionary<string, object?> {
            ["Name"] = "Alex",
            ["Email"] = "alex@uni.test"
        }).Instance!;

        var closeUnlinked = exec.Invoke(section, "Close");
        await Assert.That(closeUnlinked.Succeeded).IsFalse();
        await Assert.That(closeUnlinked.Error).Contains("HasInstructor");
        exec.Link("instructor", section, instructor);

        var noSeat = exec.Invoke(student, "Enroll");
        await Assert.That(noSeat.Succeeded).IsFalse();

        var dup = exec.Create("Student", new Dictionary<string, object?> {
            ["Name"] = "Other",
            ["Email"] = "alex@uni.test"
        });
        await Assert.That(dup.Succeeded).IsFalse();
        await Assert.That(dup.Error).Contains("Email");

        var enroll = exec.Invoke(student, "Enroll", new Dictionary<string, object?> { ["offering"] = section });
        await Assert.That(enroll.Succeeded).IsTrue();
        await Assert.That(enroll.Instance).IsNotNull();
        var enrollment = enroll.Instance!;
        await Assert.That(ModuleReads.Stage(enrollment)).IsEqualTo("Pending");
        await Assert.That(student.Bag["OpenCredits"]).IsEqualTo(3L);
        await Assert.That(enrollment.Bag["EnrolledOn"]).IsNotNull();

        var closeOk = exec.Invoke(section, "Close");
        await Assert.That(closeOk.Succeeded).IsTrue();

        var confirm = exec.Invoke(enrollment, "Confirm");
        await Assert.That(confirm.Succeeded).IsTrue();
        await Assert.That(ModuleReads.Stage(enrollment)).IsEqualTo("Registered");
        await Assert.That(section.Bag["SeatsTaken"]).IsEqualTo(1L);
        var enrolledOn = (DateOnly)enrollment.Bag["EnrolledOn"]!;
        var dueDate = (DateOnly)enrollment.Bag["DueDate"]!;
        await Assert.That(dueDate).IsEqualTo(enrolledOn.AddDays(14));

        var student2 = exec.Create("Student", new Dictionary<string, object?> {
            ["Name"] = "Blake",
            ["Email"] = "blake@uni.test"
        }).Instance!;
        var enroll2 = exec.Invoke(student2, "Enroll", new Dictionary<string, object?> { ["offering"] = section });
        await Assert.That(enroll2.Succeeded).IsTrue();
        var enrollment2 = enroll2.Instance!;
        var confirmFull = exec.Invoke(enrollment2, "Confirm");
        await Assert.That(confirmFull.Succeeded).IsFalse();
        await Assert.That(confirmFull.FailedGuards.SequenceEqual(["SectionFull"])).IsTrue();

        var drop = exec.Invoke(enrollment, "Drop");
        await Assert.That(drop.Succeeded).IsTrue();
        await Assert.That(student.Bag["OpenCredits"]).IsEqualTo(0L);
        await Assert.That(section.Bag["SeatsTaken"]).IsEqualTo(0L);

        // No registered enrollments left: the filtered `for` iterates zero records, which
        // is a no-op (2026-10-17) — the action succeeds and changes nothing.
        var dropEmpty = exec.Invoke(student, "DropAllRegistered");
        await Assert.That(dropEmpty.Succeeded).IsTrue();
        await Assert.That(student.Bag["OpenCredits"]).IsEqualTo(0L);

        await Assert.That(exec.Invoke(section, "Reopen").Succeeded).IsTrue();
        var staffLeave = exec.Invoke(section, "StaffLeave");
        await Assert.That(staffLeave.Succeeded).IsTrue();
        await Assert.That(ModuleReads.Stage(instructor)).IsEqualTo("Leave");
        var leaveAgain = exec.Invoke(section, "StaffLeave");
        await Assert.That(leaveAgain.Succeeded).IsFalse();

        await Assert.That(exec.Invoke(instructor, "Return").Succeeded).IsTrue();
        var queue = exec.Invoke(section, "Queue", new Dictionary<string, object?> { ["who"] = student });
        await Assert.That(queue.Succeeded).IsTrue();
        var offer = queue.Instance!;
        await Assert.That(ModuleReads.Stage(offer)).IsEqualTo("Queued");
        await Assert.That(E("Section").Policies.First(p => p.Name == "HasWaiters")).IsNotNull();
        await Assert.That(exec.EvaluatePolicy(section, (E("Section").Policies.First(p => p.Name == "HasWaiters")).Name)).IsTrue();

        await Assert.That(exec.Invoke(offer, "Seat").Succeeded).IsTrue();
        await Assert.That(section.Bag["OfferedSeats"]).IsEqualTo(1L);
        var queue2 = exec.Invoke(section, "Queue", new Dictionary<string, object?> { ["who"] = student2 });
        await Assert.That(queue2.Succeeded).IsTrue();
        await Assert.That(exec.Invoke(queue2.Instance!, "Seat").Succeeded).IsTrue();
        await Assert.That(section.Bag["OfferedSeats"]).IsEqualTo(2L);

        await Assert.That(exec.Invoke(offer, "Cancel").Succeeded).IsTrue();
        await Assert.That(ModuleReads.Stage(offer)).IsEqualTo("Cancelled");
        await Assert.That(section.Bag["WaitCleared"]).IsEqualTo(0L);
        await Assert.That(exec.Invoke(queue2.Instance!, "Cancel").Succeeded).IsTrue();
        await Assert.That(section.Bag["WaitCleared"]).IsEqualTo(1L);
        var cancelAgain = exec.Invoke(offer, "Cancel");
        await Assert.That(cancelAgain.Succeeded).IsFalse();

        var enroll3 = exec.Invoke(student, "Enroll", new Dictionary<string, object?> { ["offering"] = section });
        await Assert.That(enroll3.Succeeded).IsTrue();
        var e3 = enroll3.Instance!;
        await Assert.That(exec.Invoke(e3, "Confirm").Succeeded).IsTrue();
        var failGrade = exec.Invoke(e3, "RecordGrade", new Dictionary<string, object?> { ["score"] = 50L });
        await Assert.That(failGrade.Succeeded).IsTrue();
        await Assert.That(e3.Bag["Grade"]).IsEqualTo(0L);
        // No enrollment passes the predicate yet (Grade 50 < 70): the filtered `for` is a
        // no-op, so the action succeeds and nothing is completed.
        var noPass = exec.Invoke(student, "CompletePassing");
        await Assert.That(noPass.Succeeded).IsTrue();
        await Assert.That(ModuleReads.Stage(e3)).IsEqualTo("Registered");
        await Assert.That(e3.Bag["Grade"]).IsEqualTo(0L);

        var passGrade = exec.Invoke(e3, "RecordGrade", new Dictionary<string, object?> { ["score"] = 90L });
        await Assert.That(passGrade.Succeeded).IsTrue();
        await Assert.That(e3.Bag["Grade"]).IsEqualTo(90L);
        var completePass = exec.Invoke(student, "CompletePassing");
        await Assert.That(completePass.Succeeded).IsTrue();
        await Assert.That(ModuleReads.Stage(e3)).IsEqualTo("Completed");
        await Assert.That(student.Bag["OpenCredits"]).IsEqualTo(0L);
        await Assert.That(student.Bag["Completions"]).IsEqualTo(1L);
        await Assert.That(student.Bag["LastGrade"]).IsEqualTo(90L);

        await Assert.That(exec.Invoke(student, "Suspend").Succeeded).IsTrue();
        await Assert.That(ModuleReads.Stage(student)).IsEqualTo("Suspended");
        await Assert.That(student.Bag["Holds"]).IsEqualTo(1L);
    }
}