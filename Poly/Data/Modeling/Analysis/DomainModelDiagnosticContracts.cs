namespace Poly.Data.Modeling;

internal static class DomainModelDiagnosticContracts {
    internal static class Structural {
        public const string DuplicateFragment = "Duplicate";
        public const string CycleFragment = "inheritance cycle";
        public const string OwnershipFragment = "Ownership relationship";
        public const string MutationInvariantFragment = "does not belong to domain";
    }

    internal static class Semantic {
        public const string StageInheritanceFragment = "must have a parent stage";
        public const string ActionVisibilityFragment = "must belong to entity";
        public const string TypeCompatibilityFragment = "from a different domain";
        public const string ConstraintMismatchFragment = "is not a subset of parent's constraint";
    }

    internal static class Policy {
        public const string MissingPropertyFragment = "references property";
        public const string AstGenerationFragment = "Failed to build validation AST";
        public const string ActorReferenceFragment = "references actor";
    }

    internal static class Effect {
        public const string BindingFragment = "missing binding";
        public const string UnsatisfiedRequirementFragment = "requires property";
        public const string PrePostFragment = "invalid post-state";
    }

    internal static class ActionEvent {
        public const string ContractFragment = "event contract";
        public const string OrderingFragment = "causality";
        public const string ReplayFragment = "idempotency";
    }

    internal static class Event {
        public const string LivenessFragment = "not observed";
        public const string CorrelationFragment = "correlation";
    }

    internal static class Constraint {
        public const string FixedPointFragment = "constraint fixed-point";
        public const string SatisfiabilityFragment = "unsatisfiable";
    }

    internal static class Coverage {
        public const string RuleCoverageFragment = "coverage";
    }

    internal static class Quality {
        public const string DriftFragment = "drift";
    }
}