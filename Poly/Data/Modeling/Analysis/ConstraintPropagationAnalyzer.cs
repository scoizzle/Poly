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
        Property param,
        List<Constraint> accumulated,
        HashSet<Effect> visited) {

        foreach (var effect in effects) {
            if (!visited.Add(effect)) continue;

            switch (effect) {
                case InvokeAction invoke:
                    // Check if invoke references our parameter
                    foreach (var binding in invoke.ParameterBindings) {
                        if (binding.Value is EffectValueRef eRef && eRef.SourceEffectName == param.Name) {
                            // This invoke uses our parameter - collect constraints from target action
                            CollectFromAction(invoke.TargetAction, accumulated, visited);
                        }
                    }
                    break;

                case Conditional cond:
                    CollectDownstreamConstraints(cond.ChildEffects, param, accumulated, visited);
                    break;

                case Assign assign:
                    // Check if target references our parameter
                    if (assign.Target is Property prop && ReferencesParameter(prop, param)) {
                        accumulated.AddRange(prop.Type.Constraints);
                    }
                    break;
            }
        }
    }

    private static void CollectFromAction(Action target, List<Constraint> accumulated, HashSet<Effect> visited) {
        CollectDownstreamConstraints(target.Effects, null!, accumulated, visited);
    }

    private static bool ReferencesParameter(Property prop, Property param) {
        // Simplified: check if property's type matches parameter's type
        return ReferenceEquals(prop.Type, param.Type);
    }
}