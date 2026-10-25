namespace Poly.DomainModeling.Compile;

/// <summary>Ordered stages a domain compilation passes through.</summary>
public enum PipelineStage {
    Parse,
    Load,
    Resolve,
    Evolve,
    Analyze,
    Lower,
    Produce,
    Consume,
}
