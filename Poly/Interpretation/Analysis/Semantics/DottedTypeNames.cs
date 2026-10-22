using Poly.Ast.Nodes;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Interpretation.Analysis.Semantics;

/// <summary>
/// One definition of a dotted CLR type name folded as a member chain over an
/// identifier (<c>System.String</c>). The bind pass rewrites the spine when the root
/// is not a declared name; the type pass keeps the same lookup as a fallback.
/// </summary>
internal static class DottedTypeNames {
    /// <summary>Flatten a member chain over an identifier to its dotted name and root
    /// identifier. False when the chain is not a pure identifier spine.</summary>
    public static bool TryGetSpine(Node node, out string dotted, out Variable root) {
        var names = new List<string>();
        var current = node;
        while (current is Member member) {
            names.Insert(0, member.MemberName);
            current = member.Value;
        }
        if (current is not Variable variable) {
            dotted = string.Empty;
            root = null!;
            return false;
        }
        names.Insert(0, variable.Name);
        dotted = string.Join(".", names);
        root = variable;
        return true;
    }

    public static bool TryResolveType(
        AnalysisContext context, Node node, out ClrTypeDefinition type) {
        type = null!;
        if (!TryGetSpine(node, out var dotted, out _))
            return false;
        if (context.TypeDefinitions.GetTypeDefinition(dotted) is not ClrTypeDefinition clr)
            return false;
        type = clr;
        return true;
    }
}