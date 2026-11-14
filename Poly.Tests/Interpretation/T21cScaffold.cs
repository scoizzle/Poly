using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.Vm;
using Poly.Introspection;

namespace Poly.Tests.Interpretation;

// SCAFFOLD (T-21c step 0): deleted in the last commit.
public class T21cScaffold {
    private enum E { A = 1, B = 2 }

    private static readonly Type Obj = typeof(object);

    private static string Run(string label, Type run, Type take, object? value, Node? arg = null, Node? extraProperty = null) {
        try {
            var v = new Parameter("v", new ClrTypeReference(run));
            var x = new Parameter("x", new ClrTypeReference(take));
            var typeDef = new TypeDefinitionNode(Name: "ScaffoldBox",
                Constructors: [new ConstructorDefinitionNode([], new Block([]))],
                Properties: [new PropertyDefinitionNode("At", new ClrTypeReference(typeof(DateTimeOffset)))],
                Methods: [
                    new MethodDefinitionNode("Take", new ClrTypeReference(Obj), Parameters: [x],
                        Body: new Return(new TypeCast(x, new ClrTypeReference(Obj)))),
                    new MethodDefinitionNode("Run", new ClrTypeReference(Obj), Parameters: [v],
                        Body: new Block([
                            new Assignment(new Member(new ThisReference(), "At"), new Constant(DateTimeOffset.UnixEpoch)),
                            new Return(new Invoke(new Member(new ThisReference(), "Take"), arg ?? v))
                        ])),
                ]);
            var context = AnalysisContext.CreateDefault();
            new TypeDefinitionNodeAnalyzer().Analyze(context, typeDef);
            var node = new Invoke(new Member(new New(new TypeReference("ScaffoldBox")), "Run"),
                new Parameter("v", new ClrTypeReference(run)));
            using var exec = Interpreter.Execute(Interpreter.Compile(node, context.TypeDefinitions), value);
            var r = exec.Result.Value;
            return $"{label}: {(r is null ? "null" : r.GetType().Name + ":" + r)}";
        }
        catch (Exception ex) {
            var e = ex;
            while (e.InnerException is not null) e = e.InnerException;
            return $"{label}: THROWS {e.GetType().Name}: {e.Message.Split('\n')[0]}";
        }
    }

    [Test]
    public async Task Probe() {
        var lines = new List<string> {
            Run("DateOnly->DateOnly?", typeof(DateOnly), typeof(DateOnly?), new DateOnly(2024, 2, 28)),
            Run("DateTime->DateTime?", typeof(DateTime), typeof(DateTime?), new DateTime(2024, 2, 28)),
            Run("TimeOnly->TimeOnly?", typeof(TimeOnly), typeof(TimeOnly?), new TimeOnly(1, 2, 3)),
            Run("E->E?", typeof(E), typeof(E?), E.B),
            Run("ulong->ulong?", typeof(ulong), typeof(ulong?), ulong.MaxValue),
            Run("DateOnly->object", typeof(DateOnly), Obj, new DateOnly(2024, 2, 28)),
            Run("DateOnly->IComparable", typeof(DateOnly), typeof(IComparable), new DateOnly(2024, 2, 28)),
            Run("bool->object", typeof(bool), Obj, true),
            Run("long->decimal?", typeof(long), typeof(decimal?), 7L),
            Run("int->long?", typeof(int), typeof(long?), 7),
            Run("long->decimal", typeof(long), typeof(decimal), 7L),
            Run("long->double", typeof(long), typeof(double), 7L),
            Run("int->double", typeof(int), typeof(double), 7),
            Run("TimeSpan->TimeSpan?", typeof(TimeSpan), typeof(TimeSpan?), TimeSpan.FromMinutes(5)),
            Run("Guid->Guid?", typeof(Guid), typeof(Guid?), Guid.Empty),
            Run("decimal->decimal?", typeof(decimal), typeof(decimal?), 7m),
            Run("long->long?", typeof(long), typeof(long?), 7L),
            Run("bool->bool?", typeof(bool), typeof(bool?), true),
            Run("double->double?", typeof(double), typeof(double?), 1.5),
            // step 0 (b): DateTimeOffset
            Run("DTO->DTO?", typeof(DateTimeOffset), typeof(DateTimeOffset?), DateTimeOffset.UnixEpoch),
            Run("DTO->DTO? (call result arg)", typeof(DateTimeOffset), typeof(DateTimeOffset?), DateTimeOffset.UnixEpoch,
                new Member(new ClrTypeReference(typeof(DateTimeOffset)), nameof(DateTimeOffset.UtcNow))),
            Run("DTO->DTO? (property arg)", typeof(DateTimeOffset), typeof(DateTimeOffset?), DateTimeOffset.UnixEpoch,
                new Member(new ThisReference(), "At")),
            Run("DTO->DTO? (AddDays arg)", typeof(DateTimeOffset), typeof(DateTimeOffset?), DateTimeOffset.UnixEpoch,
                new Invoke(new Member(new Parameter("v", new ClrTypeReference(typeof(DateTimeOffset))), "AddDays"), new Constant(1.0))),
        };
        File.WriteAllLines("/private/tmp/claude-501/-Users-scoizzle--t3-worktrees-Poly-t3-1e0ef8dd-43f79f8baa5d9093/0bcdd728-e562-47f0-9674-433b856b6849/scratchpad/step0.txt", lines);
        await Assert.That(lines.Count).IsGreaterThan(0);
    }
}