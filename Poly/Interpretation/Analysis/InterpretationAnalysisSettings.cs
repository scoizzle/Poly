using Poly.Interpretation.Analysis.Semantics;
using Poly.Syntax.Analysis;

namespace Poly.Interpretation.Analysis;

public enum InterpretationAnalysisMode {
    Balanced,
    Strict,
    Explain
}

public sealed record InterpretationAnalysisSettings {
    public static InterpretationAnalysisSettings Default { get; } = new();

    public InterpretationAnalysisMode Mode { get; init; } = InterpretationAnalysisMode.Balanced;
    public AnalysisOptions AnalysisOptions { get; init; } = AnalysisOptions.Default;
    public AnalysisDiagnosticConfiguration DiagnosticConfiguration { get; init; } = AnalysisDiagnosticConfiguration.Default;
    public SideEffectAnalysisOptions SideEffectOptions { get; init; } = SideEffectAnalysisOptions.Default;

    public static InterpretationAnalysisSettings ForMode(InterpretationAnalysisMode mode) {
        return mode switch {
            InterpretationAnalysisMode.Strict => new InterpretationAnalysisSettings {
                Mode = mode,
                AnalysisOptions = new AnalysisOptions { Mode = AnalysisMode.FailFast },
                DiagnosticConfiguration = new AnalysisDiagnosticConfiguration {
                    TreatWarningsAsErrors = true
                }
            },

            InterpretationAnalysisMode.Explain => new InterpretationAnalysisSettings {
                Mode = mode,
                AnalysisOptions = AnalysisOptions.Default,
                DiagnosticConfiguration = AnalysisDiagnosticConfiguration.Default,
                SideEffectOptions = new SideEffectAnalysisOptions {
                    EmitElisionDiagnostics = true
                }
            },

            _ => Default
        };
    }
}