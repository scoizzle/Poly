using System.Linq.Expressions;
using System.Text;

namespace Poly.Tests.TestHelpers;

/// <summary>
/// Renders an emitted expression body as an indented tree, so the shape the VM produces can be
/// read the way <c>docs/interpretation/vm-abi-reference.md</c> describes it. Test-side only for
/// now; if the core lib grows a dump entry point this is the shape it should produce.
/// <para>Each node prints on its own line, and a node's own text is repeated on its parent's line
/// by <see cref="Expression.ToString()"/>. Count structural facts on the node lines, not by
/// grepping the whole text - a text count triple-counts.</para>
/// </summary>
public static class ExpressionText {
    public static string Render(Expression? body) {
        var sb = new StringBuilder();
        Walk(body, sb, 0);
        return sb.ToString();
    }

    static void Walk(Expression? e, StringBuilder sb, int depth) {
        if (e is null) return;
        var pad = new string(' ', depth * 2);
        switch (e) {
            case LambdaExpression l:
                sb.AppendLine($"{pad}lambda");
                Walk(l.Body, sb, depth + 1);
                return;
            case BlockExpression b:
                sb.AppendLine($"{pad}block : {b.Type.Name}  locals=[{string.Join(", ", b.Variables.Select(v => $"{v.Name}:{v.Type.Name}"))}]");
                foreach (var x in b.Expressions) Walk(x, sb, depth + 1);
                return;
            case ConditionalExpression c:
                sb.AppendLine($"{pad}if ({c.Test.Type.Name})");
                Walk(c.Test, sb, depth + 1);
                sb.AppendLine($"{pad}then");
                Walk(c.IfTrue, sb, depth + 1);
                sb.AppendLine($"{pad}else");
                Walk(c.IfFalse, sb, depth + 1);
                return;
            case GotoExpression g:
                sb.AppendLine($"{pad}{g.Kind} -> {g.Target?.Name}");
                return;
            case LabelExpression la:
                sb.AppendLine($"{pad}label {la.Target.Name}");
                Walk(la.DefaultValue, sb, depth + 1);
                return;
            case LoopExpression lo:
                sb.AppendLine($"{pad}loop {lo.BreakLabel?.Name}/{lo.ContinueLabel?.Name}");
                Walk(lo.Body, sb, depth + 1);
                return;
            case TryExpression t:
                sb.AppendLine($"{pad}try");
                Walk(t.Body, sb, depth + 1);
                foreach (var h in t.Handlers) { sb.AppendLine($"{pad}catch"); Walk(h.Body, sb, depth + 1); }
                if (t.Finally is not null) { sb.AppendLine($"{pad}finally"); Walk(t.Finally, sb, depth + 1); }
                return;
            case BinaryExpression bin when bin.NodeType == ExpressionType.Assign:
                sb.AppendLine($"{pad}assign : {bin.Type.Name}");
                Walk(bin.Left, sb, depth + 1);
                Walk(bin.Right, sb, depth + 1);
                return;
            default:
                sb.AppendLine($"{pad}{e.NodeType} : {e.Type.Name}  {Short(e)}");
                switch (e) {
                    case BinaryExpression b2: Walk(b2.Left, sb, depth + 1); Walk(b2.Right, sb, depth + 1); break;
                    case UnaryExpression u: Walk(u.Operand, sb, depth + 1); break;
                    case MethodCallExpression mc:
                        Walk(mc.Object, sb, depth + 1);
                        foreach (var a in mc.Arguments) Walk(a, sb, depth + 1);
                        break;
                    case InvocationExpression inv:
                        Walk(inv.Expression, sb, depth + 1);
                        foreach (var a in inv.Arguments) Walk(a, sb, depth + 1);
                        break;
                    case IndexExpression ix:
                        Walk(ix.Object, sb, depth + 1);
                        foreach (var a in ix.Arguments) Walk(a, sb, depth + 1);
                        break;
                    case NewArrayExpression na: foreach (var x in na.Expressions) Walk(x, sb, depth + 1); break;
                    case MemberExpression me: Walk(me.Expression, sb, depth + 1); break;
                    case TypeBinaryExpression tb: Walk(tb.Expression, sb, depth + 1); break;
                }
                return;
        }
    }

    static string Short(Expression e) {
        var s = e.ToString().Replace("\r", " ").Replace("\n", " ");
        return s.Length > 90 ? s[..90] + "…" : s;
    }
}