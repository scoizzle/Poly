namespace Poly.Data.Modeling.Analysis;

public interface IDomainModelAnalyzer {
    string Name { get; }

    void Analyze(Domain domain, DomainModelAnalysisContext context);
}