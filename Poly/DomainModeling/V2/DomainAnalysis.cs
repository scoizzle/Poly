namespace Poly.DomainModeling.V2;

public sealed record DomainAnalysis(
    int EntityCount,
    int PropertyCount,
    int StageCount,
    int ActionCount,
    int RelationshipCount,
    DomainValidationResult Validation
);

public static class DomainAnalyzer {
    public static DomainAnalysis Analyze(Domain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        var validation = DomainValidator.Validate(domain);
        return new DomainAnalysis(
            domain.Entities.Count,
            domain.Entities.Sum(entity => entity.Properties.Count),
            domain.Entities.Sum(entity => entity.Stages.Count),
            domain.Entities.Sum(entity => entity.Actions.Count),
            domain.Relationships.Count,
            validation
        );
    }
}
