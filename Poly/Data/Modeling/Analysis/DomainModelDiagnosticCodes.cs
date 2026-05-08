namespace Poly.Data.Modeling;

internal static class DomainModelDiagnosticCodes {
    public const string StructuralDuplicate = "DMSTR001";
    public const string StructuralCycle = "DMSTR002";
    public const string StructuralOwnership = "DMSTR003";
    public const string MutationInvariant = "DMMUT001";
    public const string SemanticStageInheritance = "DMSEM001";
    public const string SemanticActionVisibility = "DMSEM002";
    public const string SemanticTypeCompatibility = "DMSEM003";
    public const string SemanticConstraintMismatch = "DMSEM004";
    public const string PolicyMissingProperty = "DMPOL001";
    public const string PolicyAstGeneration = "DMPOL002";
    public const string PolicyActorReference = "DMPOL003";
    public const string EffectBinding = "DMEFF001";
    public const string EffectUnsatisfiedRequirement = "DMEFF002";
    public const string ActionTrigger = "DMACT001";
    public const string EventSubscription = "DMEVT001";
}