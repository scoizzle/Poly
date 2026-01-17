using System.Linq.Expressions;
using Poly.Interpretation;

namespace Poly.Tests.Interpretation.C99Repl;

/// <summary>
/// C99 REPL (Read-Eval-Print Loop) for interpreting C99 code snippets using the Poly Interpretation system.
/// </summary>
public sealed class C99ReplEngine
{
    /// <summary>
    /// Parses and compiles a C99 code snippet into a compiled function.
    /// </summary>
    /// <param name="code">C99 source code to execute</param>
    /// <returns>A compiled delegate that can be executed</returns>
    public Func<T> ExecuteExpression<T>(string code)
    {
        var context = new InterpretationContext();
        var tokens = new C99Lexer(code).Tokenize();
        var parser = new C99DirectParser(tokens, context);
        var interpretable = parser.Parse();

        var expression = interpretable.BuildExpression(context);
        var lambda = Expression.Lambda<Func<T>>(expression);
        return lambda.Compile();
    }

    /// <summary>
    /// Evaluates a C99 code snippet and returns the result.
    /// </summary>
    public T Evaluate<T>(string code)
    {
        var func = ExecuteExpression<T>(code);
        return func();
    }

    /// <summary>
    /// Parses C99 code and returns the AST for inspection.
    /// </summary>
    public C99Program Parse(string code)
    {
        var tokens = new C99Lexer(code).Tokenize();
        return new C99Parser(tokens).Parse();
    }

    /// <summary>
    /// Tokenizes C99 code and returns the token stream.
    /// </summary>
    public List<C99Token> Tokenize(string code)
    {
        return new C99Lexer(code).Tokenize();
    }
}
