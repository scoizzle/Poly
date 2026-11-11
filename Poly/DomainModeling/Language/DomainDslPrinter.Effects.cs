using Poly.DomainModeling.Dispatch;
using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Language;

// DomainDslPrinter partial: effect printing and the effect dispatch printer (G-00b mechanical split).
public sealed partial class DomainDslPrinter {
    private void PrintEffect(Effect effect, string indent) {
        EffectPrinter.Run(_sb, this, effect, indent);
    }

    /// <summary>
    /// Effect dispatch for printing. Methods are named by the Effect subtype.
    /// The verb (print) comes from the containing DomainDslPrinter.
    /// </summary>
    private sealed class EffectPrinter : EffectDispatch<object?> {
        private readonly StringBuilder _sb;
        private readonly DomainDslPrinter _printer;
        private string _indent = "";

        private EffectPrinter(StringBuilder sb, DomainDslPrinter printer) {
            _sb = sb;
            _printer = printer;
        }

        protected override object? Default() => null;

        public static void Run(StringBuilder sb, DomainDslPrinter printer, Effect effect, string indent) {
            var ep = new EffectPrinter(sb, printer) { _indent = indent };
            ep.Route(effect);
        }

        protected override object? StageTransition(StageTransitionEffect ste) {
            _sb.Append("transition to ");
            _sb.Append(ste.TargetStage.StageName);
            _sb.AppendLine();
            return null;
        }

        protected override object? Assign(AssignEffect ae) {
            _sb.Append("assign ");
            _sb.Append(_printer.PrintExpression(ae.Target));
            _sb.Append(" to ");
            _sb.Append(_printer.PrintExpression(ae.Value));
            _sb.AppendLine();
            return null;
        }

        protected override object? Composite(CompositeEffect ce) {
            foreach (var sub in ce.Effects) {
                Run(_sb, _printer, sub, _indent);
            }
            return null;
        }

        protected override object? Conditional(ConditionalEffect ce) {
            _printer.PrintConditionalEffect(ce, _indent);
            return null;
        }

        protected override object? CreateEntityInstance(CreateEntityInstance create) {
            if (create.RelationshipName is not null) {
                _sb.Append("create in ");
                _sb.Append(create.RelationshipName);
                _sb.Append(" {");
            }
            else {
                _sb.Append("create ");
                _sb.Append(create.Type.TypeName);
                _sb.Append(" {");
            }
            var first = true;
            foreach (var init in create.Initializers) {
                _sb.Append(' ');
                _sb.Append(init.PropertyName);
                _sb.Append(": ");
                _sb.Append(_printer.PrintExpression(init.Expression));
                first = false;
            }
            if (!first) _sb.Append(' ');
            _sb.AppendLine("}");
            return null;
        }

        protected override object? CreateEntityInRelationship(CreateEntityInRelationshipEffect createIn) {
            _sb.Append("create in ");
            _sb.Append(createIn.RelationshipName);
            _sb.Append(" {");
            var firstInit = true;
            foreach (var init in createIn.Initializers) {
                _sb.Append(' ');
                _sb.Append(init.PropertyName);
                _sb.Append(": ");
                _sb.Append(_printer.PrintExpression(init.Expression));
                firstInit = false;
            }
            if (!firstInit) _sb.Append(' ');
            _sb.AppendLine("}");
            return null;
        }

        protected override object? InvokeAction(InvokeActionEffect invoke) {
            _sb.Append("invoke ");
            if (invoke.TargetRelationship is not null) {
                _sb.Append(invoke.TargetRelationship);
                _sb.Append('.');
            }
            _sb.Append(invoke.ActionName);
            if (invoke.ParameterBindings.Count > 0) {
                _sb.Append('(');
                var firstBinding = true;
                foreach (var binding in invoke.ParameterBindings) {
                    if (!firstBinding) _sb.Append(", ");
                    _sb.Append(binding.PropertyName);
                    _sb.Append(": ");
                    _sb.Append(_printer.PrintExpression(binding.Expression));
                    firstBinding = false;
                }
                _sb.Append(')');
            }
            _sb.AppendLine();
            return null;
        }

        protected override object? ForEach(ForEachEffect fe) {
            _sb.Append($"for {fe.BinderName} in {fe.RelationshipName}");
            if (fe.Where is not null)
                _sb.Append(" where ").Append(_printer.PrintExpression(fe.Where));
            _sb.AppendLine(" {");
            foreach (var sub in fe.Body) {
                _sb.Append(_indent);
                _sb.Append("    ");
                _printer.PrintEffect(sub, _indent + "  ");
            }
            _sb.Append(_indent);
            _sb.AppendLine("}");
            return null;
        }

        protected override object? ForEachInvoke(ForEachInvokeEffect efe) {
            _sb.Append($"for {efe.RelationshipName} as {efe.BinderName}");
            switch (efe.Predicate) {
                case ForEachNamedPolicy { PolicyName: var policyName }:
                    _sb.Append($" where {efe.BinderName} {policyName}");
                    break;
                case ForEachStageMembership { StageName: var stageName }:
                    _sb.Append($" where {efe.BinderName} in {stageName}");
                    break;
            }
            _sb.Append($" invoke {efe.BinderName}.{efe.ActionName}");
            if (efe.ParameterBindings.Count > 0) {
                _sb.Append('(');
                var firstBinding = true;
                foreach (var binding in efe.ParameterBindings) {
                    if (!firstBinding) _sb.Append(", ");
                    _sb.Append(binding.PropertyName);
                    _sb.Append(": ");
                    _sb.Append(_printer.PrintExpression(binding.Expression));
                    firstBinding = false;
                }
                _sb.Append(')');
            }
            _sb.AppendLine();
            return null;
        }
    }

    private void PrintConditionalEffect(ConditionalEffect ce, string indent) {
        _sb.AppendLine("if (" + PrintExpression(ce.Condition) + ") {");
        foreach (var sub in ce.ThenEffects) {
            _sb.Append(indent);
            _sb.Append("    ");
            PrintEffect(sub, indent + "  ");
        }
        _sb.Append(indent);
        _sb.Append('}');

        if (ce.ElseEffects is { Count: > 0 }) {
            if (ce.ElseEffects is [ConditionalEffect nestedOnly]) {
                _sb.AppendLine();
                _sb.Append(indent);
                _sb.Append("else ");
                PrintConditionalEffect(nestedOnly, indent);
                return;
            }

            _sb.AppendLine();
            _sb.Append(indent);
            _sb.AppendLine("else {");
            foreach (var sub in ce.ElseEffects) {
                _sb.Append(indent);
                _sb.Append("    ");
                PrintEffect(sub, indent + "  ");
            }
            _sb.Append(indent);
            _sb.Append('}');
        }

        _sb.AppendLine();
    }
}