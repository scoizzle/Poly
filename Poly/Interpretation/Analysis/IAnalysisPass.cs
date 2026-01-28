namespace Poly.Interpretation.Analysis;

public interface INodeAnalyzer {
    void Analyze(AnalysisContext context, Node node);
}

public static class NodeAnalyzerExtensions
{
    extension(INodeAnalyzer analyzer)
    {
        public void AnalyzeChildren(AnalysisContext context, Node node)
        {
            foreach (var child in node.Children.Where(static c => c is not null))
            {
                analyzer.Analyze(context, child!);
            }
        }
    }
}