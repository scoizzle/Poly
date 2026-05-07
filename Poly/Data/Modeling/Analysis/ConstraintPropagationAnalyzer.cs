using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.Effects.Mutations;
using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation;

namespace Poly.Data.Modeling.Analysis;

using Conditional = Poly.Data.Modeling.Effects.Conditional;

internal sealed record DownstreamConstraintsMetadata(IReadOnlyList<Constraint> Constraints) : IAnalysisMetadata;

/// <summary>
/// Propagates constraints from downstream effect property accesses to Action parameters.
/// This allows code generation to validate parameters earlier based on how they're used.
/// </summary>
internal sealed class ConstraintPropagationAnalyzer : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node) {
        if (node is Action action) {
            AnalyzeAction(context, action);
        }
    }

    private static void AnalyzeAction(AnalysisContext context, Action action) {
        if (!context.TryBeginAnalyzerVisit<ConstraintPropagationAnalyzer>(action)) {
            return;
        }

        foreach (var param in action.Parameters.OfType<Property>()) {
            var visited = new HashSet<Effect>();
            var constraints = new List<Constraint>();
            CollectDownstreamConstraints(action.Effects, param, constraints, visited);

            if (constraints.Count > 0) {
                context.SetMetadata(param, new DownstreamConstraintsMetadata(constraints.AsReadOnly()));
            }
        }
    }

    private static void CollectDownstreamConstraints(
        IEnumerable<Effect> effects,
        Property? param,
        List<Constraint> accumulated,
        HashSet<Effect> visited) {

        foreach (var effect in effects) {
            if (!visited.Add(effect)) continue;

            switch (effect) {
                case InvokeAction invoke:
                    // Check if this invoke references our parameter (if we have one)
                    if (param is not null) {
                        foreach (var binding in invoke.ParameterBindings) {
                            if (binding.Value is EffectValueRef eRef &&
                                eRef.SourceEffectName == param.Name) {
                                // Collect from target action
                                if (invoke.TargetAction is not null) {
                                    CollectFromAction(invoke.TargetAction, accumulated, visited);
                                }
                            }
                        }
                    }
                    else {
                        // No specific param — collect from ALL invokes
                        if (invoke.TargetAction is not null) {
                            CollectFromAction(invoke.TargetAction, accumulated, visited);
                        }
                    }
                    break;

                case Conditional cond:
                    if (param is null) {
                        CollectFromEffects(cond.ChildEffects, accumulated, visited);
                    }
                    else {
                        CollectDownstreamConstraints(cond.ChildEffects, param, accumulated, visited);
                    }
                    break;

                case Assign assign:
                    // Collect constraints from the target's TYPE if it's a Property
                    if (assign.Target is Property prop) {
                        if (param is null || ReferencesParameter(prop, param)) {
                            accumulated.AddRange(prop.Type.Constraints);
                        }
                    }
                    break;
            }
        }
    }

    private static void CollectFromAction(
        Action target,
        List<Constraint> accumulated,
        HashSet<Effect> visited) {

        // Collect constraints from ALL property accesses in this action's effects
        foreach (var effect in target.Effects) {
            if (!visited.Add(effect)) continue;

            switch (effect) {
                case Assign assign:
                    if (assign.Target is Property prop) {
                        accumulated.AddRange(prop.Type.Constraints);
                    }
                    break;

                case InvokeAction nestedInvoke:
                    if (nestedInvoke.TargetAction is not null) {
                        CollectFromAction(nestedInvoke.TargetAction, accumulated, visited);
                    }
                    break;

                case Conditional cond:
                    CollectFromEffects(cond.ChildEffects, accumulated, visited);
                    break;
            }
        }
    }

    private static void CollectFromEffects(
        IEnumerable<Effect> effects,
        List<Constraint> accumulated,
        HashSet<Effect> visited) {

        foreach (var effect in effects) {
            if (!visited.Add(effect)) continue;

            switch (effect) {
                case Assign assign:
                    if (assign.Target is Property prop) {
                        accumulated.AddRange(prop.Type.Constraints);
                    }
                    break;

                case InvokeAction nestedInvoke:
                    if (nestedInvoke.TargetAction is not null) {
                        CollectFromAction(nestedInvoke.TargetAction, accumulated, visited);
                    }
                    break;

                case Conditional cond:
                    CollectFromEffects(cond.ChildEffects, accumulated, visited);
                    break;
            }
        }
    }

    private static bool ReferencesParameter(Property prop, Property param) {
        // Simplified: check if property's type matches parameter's type
        return ReferenceEquals(prop.Type, param.Type);
    }
}