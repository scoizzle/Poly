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
        return File.ReadAllText(Path.Combine(root, "probes/dogfood/university.poly"));
    }

    private static (Domain Domain, AnalysisResult Analysis) Evolve() {
        var changes = new PolyDslParser(PolyText()).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
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
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(types);

        await Assert.That(cs).DoesNotContain("void Notify(string stageName)");
        await Assert.That(cs).Contains("Instructor? instructor = null");
        await Assert.That(cs).Contains("IEnumerable<Enrollment>? enrollments = null");
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
        var store = new DomainInstanceStore();
        Entity E(string n) => domain.Types.OfType<Entity>().First(t => t.Name == n);

        var instructor = DomainEntityInstance.Create(E("Instructor"),
            new Dictionary<string, object?> {
                ["Name"] = "Kim",
                ["Email"] = "kim@uni.test"
            }, domain);
        var course = DomainEntityInstance.Create(E("Course"),
            new Dictionary<string, object?> {
                ["Code"] = "CS101",
                ["Title"] = "Intro"
            }, domain);
        var section = DomainEntityInstance.Create(E("Section"),
            new Dictionary<string, object?> {
                ["Cap"] = 1L,
                ["OfferingTerm"] = "Fall"
            }, domain);
        var student = DomainEntityInstance.Create(E("Student"),
            new Dictionary<string, object?> {
                ["Name"] = "Alex",
                ["Email"] = "alex@uni.test"
            }, domain);
        store.Add(instructor);
        store.Add(course);
        store.Add(section);
        store.Add(student);
        store.Link("course", section, course);

        var closeUnlinked = section.InvokeAction("Close");
        await Assert.That(closeUnlinked.Succeeded).IsFalse();
        await Assert.That(closeUnlinked.FailedGuards).Contains("HasInstructor");
        store.Link("instructor", section, instructor);

        var noSeat = student.InvokeAction("Enroll");
        await Assert.That(noSeat.Succeeded).IsFalse();

        var dup = DomainEntityInstance.Create(E("Student"),
            new Dictionary<string, object?> {
                ["Name"] = "Other",
                ["Email"] = "alex@uni.test"
            }, domain);
        var dupAdd = Assert.Throws<InvalidOperationException>(() => store.Add(dup));
        await Assert.That(dupAdd!.Message).Contains("Email");

        var enroll = student.InvokeAction("Enroll",
            new Dictionary<string, object?> { ["offering"] = section });
        await Assert.That(enroll.Succeeded).IsTrue();
        await Assert.That(enroll.ResultInstance).IsNotNull();
        var enrollment = enroll.ResultInstance!;
        await Assert.That(enrollment.CurrentStage).IsEqualTo("Pending");
        await Assert.That(student.GetProperty<object>("OpenCredits")).IsEqualTo(3L);
        await Assert.That(enrollment.GetProperty<object>("EnrolledOn")).IsNotNull();

        var closeOk = section.InvokeAction("Close");
        await Assert.That(closeOk.Succeeded).IsTrue();

        var confirm = enrollment.InvokeAction("Confirm");
        await Assert.That(confirm.Succeeded).IsTrue();
        await Assert.That(enrollment.CurrentStage).IsEqualTo("Registered");
        await Assert.That(section.GetProperty<object>("SeatsTaken")).IsEqualTo(1L);
        var enrolledOn = (DateOnly)enrollment.GetProperty<object>("EnrolledOn")!;
        var dueDate = (DateOnly)enrollment.GetProperty<object>("DueDate")!;
        await Assert.That(dueDate).IsEqualTo(enrolledOn.AddDays(14));

        var student2 = DomainEntityInstance.Create(E("Student"),
            new Dictionary<string, object?> {
                ["Name"] = "Blake",
                ["Email"] = "blake@uni.test"
            }, domain);
        store.Add(student2);
        var enroll2 = student2.InvokeAction("Enroll",
            new Dictionary<string, object?> { ["offering"] = section });
        await Assert.That(enroll2.Succeeded).IsTrue();
        var enrollment2 = enroll2.ResultInstance!;
        var confirmFull = enrollment2.InvokeAction("Confirm");
        await Assert.That(confirmFull.Succeeded).IsFalse();
        await Assert.That(confirmFull.FailedGuards).Contains("not_SectionFull");

        var drop = enrollment.InvokeAction("Drop");
        await Assert.That(drop.Succeeded).IsTrue();
        await Assert.That(student.GetProperty<object>("OpenCredits")).IsEqualTo(0L);
        await Assert.That(section.GetProperty<object>("SeatsTaken")).IsEqualTo(0L);

        var dropEmpty = student.InvokeAction("DropAllRegistered");
        await Assert.That(dropEmpty.Succeeded).IsFalse();
        await Assert.That(dropEmpty.ErrorMessage).Contains("matched zero");

        await Assert.That(section.InvokeAction("Reopen").Succeeded).IsTrue();
        var staffLeave = section.InvokeAction("StaffLeave");
        await Assert.That(staffLeave.Succeeded).IsTrue();
        await Assert.That(instructor.CurrentStage).IsEqualTo("Leave");
        var leaveAgain = section.InvokeAction("StaffLeave");
        await Assert.That(leaveAgain.Succeeded).IsFalse();

        await Assert.That(instructor.InvokeAction("Return").Succeeded).IsTrue();
        var queue = section.InvokeAction("Queue",
            new Dictionary<string, object?> { ["who"] = student });
        await Assert.That(queue.Succeeded).IsTrue();
        var offer = queue.ResultInstance!;
        await Assert.That(offer.CurrentStage).IsEqualTo("Queued");
        await Assert.That(E("Section").Policies.First(p => p.Name == "HasWaiters")).IsNotNull();
        await Assert.That(section.EvaluatePolicy(E("Section").Policies.First(p => p.Name == "HasWaiters"))).IsTrue();

        await Assert.That(offer.InvokeAction("Seat").Succeeded).IsTrue();
        await Assert.That(section.GetProperty<object>("OfferedSeats")).IsEqualTo(1L);
        var queue2 = section.InvokeAction("Queue",
            new Dictionary<string, object?> { ["who"] = student2 });
        await Assert.That(queue2.Succeeded).IsTrue();
        await Assert.That(queue2.ResultInstance!.InvokeAction("Seat").Succeeded).IsTrue();
        await Assert.That(section.GetProperty<object>("OfferedSeats")).IsEqualTo(1L);

        await Assert.That(offer.InvokeAction("Cancel").Succeeded).IsTrue();
        await Assert.That(offer.CurrentStage).IsEqualTo("Cancelled");
        await Assert.That(section.GetProperty<object>("WaitCleared")).IsEqualTo(0L);
        await Assert.That(queue2.ResultInstance!.InvokeAction("Cancel").Succeeded).IsTrue();
        await Assert.That(section.GetProperty<object>("WaitCleared")).IsEqualTo(1L);
        var cancelAgain = offer.InvokeAction("Cancel");
        await Assert.That(cancelAgain.Succeeded).IsFalse();

        var enroll3 = student.InvokeAction("Enroll",
            new Dictionary<string, object?> { ["offering"] = section });
        await Assert.That(enroll3.Succeeded).IsTrue();
        var e3 = enroll3.ResultInstance!;
        await Assert.That(e3.InvokeAction("Confirm").Succeeded).IsTrue();
        var failGrade = e3.InvokeAction("RecordGrade",
            new Dictionary<string, object?> { ["score"] = 50L });
        await Assert.That(failGrade.Succeeded).IsTrue();
        await Assert.That(e3.GetProperty<object>("Grade")).IsEqualTo(0L);
        var noPass = student.InvokeAction("CompletePassing");
        await Assert.That(noPass.Succeeded).IsFalse();
        await Assert.That(noPass.ErrorMessage).Contains("matched zero");

        var passGrade = e3.InvokeAction("RecordGrade",
            new Dictionary<string, object?> { ["score"] = 90L });
        await Assert.That(passGrade.Succeeded).IsTrue();
        await Assert.That(e3.GetProperty<object>("Grade")).IsEqualTo(90L);
        var completePass = student.InvokeAction("CompletePassing");
        await Assert.That(completePass.Succeeded).IsTrue();
        await Assert.That(e3.CurrentStage).IsEqualTo("Completed");
        await Assert.That(student.GetProperty<object>("OpenCredits")).IsEqualTo(0L);
        await Assert.That(student.GetProperty<object>("Completions")).IsEqualTo(1L);
        await Assert.That(student.GetProperty<object>("LastGrade")).IsEqualTo(90L);

        await Assert.That(student.InvokeAction("Suspend").Succeeded).IsTrue();
        await Assert.That(student.CurrentStage).IsEqualTo("Suspended");
        await Assert.That(student.GetProperty<object>("Holds")).IsEqualTo(1L);
    }
}