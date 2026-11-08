using System.Collections;
using System.Reflection;

using Poly.Ast;

namespace Poly.DomainModeling.Ontology;

/// <summary>
/// Structural equality for domain expressions that ignores node identity and source position.
/// Record equality cannot be used: <see cref="Node.Id"/> and a literal's line/column
/// participate. Library-contributed expression nodes are compared by their public properties,
/// so core needs no per-library arm (a temporal <c>Today</c> or <c>DateOperation</c> compares
/// like any other node).
/// </summary>
internal static class DomainExpressionStructuralComparer {
    public static bool Equal(DomainExpression a, DomainExpression b) => EqualNodes(a, b);

    private static bool EqualNodes(Node? a, Node? b) {
        if (ReferenceEquals(a, b))
            return true;
        if (a is null || b is null || a.GetType() != b.GetType())
            return false;

        foreach (var property in a.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
            if (property.GetIndexParameters().Length > 0)
                continue;
            // Children is the computed walk; the concrete properties carry the same nodes.
            // Id, Line and Col are identity/position, not meaning.
            if (property.Name is nameof(Node.Id) or nameof(Node.Children) or "Line" or "Col")
                continue;
            if (!EqualValues(property.GetValue(a), property.GetValue(b)))
                return false;
        }
        return true;
    }

    private static bool EqualValues(object? a, object? b) {
        if (ReferenceEquals(a, b))
            return true;
        if (a is null || b is null)
            return false;
        if (a is Node leftNode && b is Node rightNode)
            return EqualNodes(leftNode, rightNode);
        if (a is string || b is string)
            return Equals(a, b);
        if (a is IEnumerable left && b is IEnumerable right) {
            var leftItems = left.GetEnumerator();
            var rightItems = right.GetEnumerator();
            try {
                while (true) {
                    var hasLeft = leftItems.MoveNext();
                    var hasRight = rightItems.MoveNext();
                    if (hasLeft != hasRight)
                        return false;
                    if (!hasLeft)
                        return true;
                    if (!EqualValues(leftItems.Current, rightItems.Current))
                        return false;
                }
            }
            finally {
                (leftItems as IDisposable)?.Dispose();
                (rightItems as IDisposable)?.Dispose();
            }
        }
        return Equals(a, b);
    }
}