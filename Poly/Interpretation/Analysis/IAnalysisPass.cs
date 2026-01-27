namespace Poly.Interpretation.Analysis;

public interface IAnalysisPass {
    void Analyze(AnalysisContext context, Node node);
}

public static class AnalysisPassExtensions
{
    extension(IAnalysisPass pass)
    {
        public void AnalyzeChildren(AnalysisContext context, Node node)
        {
            foreach (var child in node.Children.Where(static c => c is not null))
            {
                pass.Analyze(context, child!);
            }
        }
    }
}