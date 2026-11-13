using Poly.Tests.Polyc;

using Polyc;

namespace Poly.Tests.TestHelpers;

/// <summary>What each door said about one document. A door that did not run (analysis rejected) is null.</summary>
internal sealed record GateParityResult(
    bool Analysis,
    string AnalysisSummary,
    bool? Simulate,
    string? SimulateSummary,
    bool? CSharp,
    string? CSharpSummary,
    bool? PolycCheck,
    string? PolycSummary) {
    /// <summary>Accepted by analysis implies accepted by every other door (#679 GateParity).</summary>
    public bool Holds => !Analysis || (Simulate is true && CSharp is true && PolycCheck is true);

    public override string ToString() =>
        $"analysis={Analysis} ({AnalysisSummary}); simulate={Simulate} ({SimulateSummary}); " +
        $"csharp={CSharp} ({CSharpSummary}); polyc check={PolycCheck} ({PolycSummary})";
}

/// <summary>
/// GateParity (#679, provider T-05): one document through every door. Domain analysis (<c>poly_define</c>);
/// if it accepts, the VM (<c>poly_simulate</c> over the given steps, every step must succeed), the generated C#
/// (the session's lowered module printed by <c>CSharpGenerator</c> and compiled with Roslyn, as
/// <see cref="ParityScenario"/> does) and <c>polyc check</c> (analysis, Lower and the VM compile).
/// The rule it states is "accepted by analysis implies accepted by every door".
/// </summary>
internal static class GateParity {
    public static GateParityResult Check(string dsl, params SimStep[] steps) {
        var surface = PolySurfaceHarness.New();
        var workspace = surface.Open("GateParity");
        var define = surface.Define(workspace, dsl);
        if (!define.Success)
            return new GateParityResult(false, define.Summary, null, null, null, null, null, null);

        var simulate = surface.Simulate(workspace, steps);
        var failed = Enumerable.Range(0, steps.Length).Select(simulate.Step).FirstOrDefault(s => !s.Ok);
        var simulateOk = simulate.Success && failed is null;

        bool csharpOk;
        string csharpSummary;
        try {
            ParityScenario.FromDsl(dsl, "GateParity");
            csharpOk = true;
            csharpSummary = "compiled";
        }
        catch (InvalidOperationException e) {
            csharpOk = false;
            csharpSummary = e.Message.Split('\n')[0];
        }

        var path = Path.Combine(PolycTestSupport.TempDir(), "gate-parity.poly");
        File.WriteAllText(path, dsl);
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = PolycApp.Run(["check", path], new StringReader(""), false, stdout, stderr);

        return new GateParityResult(
            true, define.Summary,
            simulateOk, failed?.Summary ?? simulate.Summary,
            csharpOk, csharpSummary,
            exit == 0, exit == 0 ? "exit 0" : $"exit {exit}: {(stdout.ToString() + stderr).Trim()}");
    }
}