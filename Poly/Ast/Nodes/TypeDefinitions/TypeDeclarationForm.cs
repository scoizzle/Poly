namespace Poly.Ast.Nodes;

/// <summary>
/// The form a type declaration is written in. This is syntax, a fact about the declaration, which a writer prints
/// back; what the declared type is, is the fact its definition states.
/// </summary>
public enum TypeDeclarationForm {
    /// <summary>A class: the form a declaration has when it states no other.</summary>
    Class,

    /// <summary>An interface declaration.</summary>
    Interface,

    /// <summary>An enumeration declaration.</summary>
    Enum,

    /// <summary>A value type declaration.</summary>
    Struct
}