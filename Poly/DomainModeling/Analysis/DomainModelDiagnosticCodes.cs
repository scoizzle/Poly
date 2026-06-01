namespace Poly.DomainModeling.Analysis;

internal static class DomainModelDiagnosticCodes {
    public const string StructuralDuplicate = "DMSTR001";
    public const string SemanticTypeCompatibility = "DMSEM003";
    public const string SemanticReferenceResolution = "DMSEM005";
    public const string EffectBinding = "DMEFF001";
    public const string EffectUnsatisfiedRequirement = "DMEFF002";
    public const string EffectPrePostCondition = "DMEFF003";
    public const string EffectUnusedParameter = "DMEFF004";
    public const string ConstraintSatisfiability = "DMCS001";
    public const string ConstraintFixedPoint = "DMCS002";
    public const string EventFlowLiveness = "DMEV002";
    public const string ActionIdempotencyReplay = "DMEV005";
    public const string EventCorrelationSoundness = "DMEV004";
    public const string ActionOrderingCausality = "DMEV003";
    public const string SemanticConstraintMismatch = "DMSEM004";
    public const string ActionEventContract = "DMEV001";
    public const string RuleCoverage = "DMEV006";
    public const string ContractIntegration = "DMCON001";
    public const string StructuralCycle = "DMSTR002";
    public const string StructuralOwnership = "DMSTR003";
}