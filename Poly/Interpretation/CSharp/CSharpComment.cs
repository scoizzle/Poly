namespace Poly.Interpretation.CSharp;

/// <summary>
/// One source comment the scanner kept, with where its opening delimiter started. The reader turns
/// these into <see cref="Poly.Ast.Nodes.Comment"/> statements at the next statement boundary; the
/// writer prints them back, so a body round-trips with its notes.
/// </summary>
/// <param name="Text">Body without the delimiters.</param>
/// <param name="IsBlock">True for <c>/* … */</c>, false for <c>// …</c>.</param>
/// <param name="Line">1-based line of the opening delimiter.</param>
/// <param name="Col">1-based column of the opening delimiter.</param>
public readonly record struct CSharpComment(string Text, bool IsBlock, int Line, int Col);