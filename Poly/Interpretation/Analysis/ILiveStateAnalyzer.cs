using Poly.Interpretation;
using Poly.Syntax.Analysis;

namespace Poly.Interpretation.Analysis;

public interface ILiveStateAnalyzer {
    void AnalyzeSuspendedState(AnalysisContext context, SuspendedExecution suspended);
}