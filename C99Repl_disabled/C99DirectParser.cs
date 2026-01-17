using System.Linq.Expressions;
using Poly.Interpretation;
using Poly.Interpretation.Operators;
using Poly.Interpretation.Operators.Arithmetic;
using Poly.Interpretation.Operators.Boolean;
using Poly.Interpretation.Operators.Comparison;
using Poly.Interpretation.Operators.Equality;
using Poly.Introspection;

namespace Poly.Tests.Interpretation.C99Repl;

/// <summary>
/// Recursive descent parser for a subset of C99 language that emits Interpretable objects directly.
/// </summary>
public sealed class C99DirectParser
{
    private readonly List<C99Token> _tokens;
    private readonly InterpretationContext _context;
    private int _current;

    public C99DirectParser(List<C99Token> tokens, InterpretationContext context)
    {
        _tokens = tokens;
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Interpretable Parse()
    {
        var statements = new List<Interpretable>();
        while (!IsAtEnd())
        {
            var stmt = ParseStatement();
            if (stmt != null)
            {
                statements.Add(stmt);
            }
        }

        // Collect all variables declared during parsing
        var blockVariables = new List<ParameterExpression>();
        foreach (var varName in _context.GetCurrentScopeVariables())
        {
            var variable = _context.GetVariable(varName);
            if (variable?.Value is Parameter param)
            {
                blockVariables.Add(param.BuildExpression(_context));
            }
        }

        // Wrap in a Block with all declared variables
        if (blockVariables.Count > 0)
        {
            return statements.Count switch
            {
                0 => new Block(new[] { Value.Wrap(0) }, blockVariables),
                1 => new Block(new[] { statements[0] }, blockVariables),
                _ => new Block(statements.ToArray(), blockVariables)
            };
        }

        return statements.Count switch
        {
            0 => Value.Wrap(0),
            1 => statements[0],
            _ => new Block(statements.ToArray())
        };
    }

    private Interpretable? ParseStatement()
    {
        return Peek().Type switch
        {
            TokenType.LeftBrace => ParseBlockStatement(),
            TokenType.If => ParseIfStatement(),
            TokenType.While => ParseWhileStatement(),
            TokenType.For => ParseForStatement(),
            TokenType.Return => ParseReturnStatement(),
            TokenType.Break => throw new NotSupportedException("Break statements not yet supported"),
            TokenType.Continue => throw new NotSupportedException("Continue statements not yet supported"),
            TokenType.Int or TokenType.Float => ParseVariableDeclaration(),
            _ => ParseExpressionStatement()
        };
    }

    private Interpretable ParseBlockStatement()
    {
        Consume(TokenType.LeftBrace, "Expected '{'");
        _context.PushScope();
        try
        {
            var statements = new List<Interpretable>();

            while (!Check(TokenType.RightBrace) && !IsAtEnd())
            {
                var stmt = ParseStatement();
                if (stmt != null)
                {
                    statements.Add(stmt);
                }
            }
            Consume(TokenType.RightBrace, "Expected '}'");

            return statements.Count switch
            {
                0 => Value.Wrap(0),
                1 => statements[0] is Value v ? v : new Block(statements.ToArray()),
                _ => new Block(statements.ToArray())
            };
        }
        finally
        {
            _context.PopScope();
        }
    }

    private Interpretable ParseIfStatement()
    {
        Consume(TokenType.If, "Expected 'if'");
        Consume(TokenType.LeftParen, "Expected '(' after 'if'");
        var condition = ToBool(ParseExpression());
        Consume(TokenType.RightParen, "Expected ')' after condition");
        var thenBranch = ParseStatement();
        Interpretable? elseBranch = null;
        if (Match(TokenType.Else))
        {
            elseBranch = ParseStatement();
        }
        return new Poly.Interpretation.Operators.IfStatement(condition, thenBranch, elseBranch);
    }

    private Interpretable ParseWhileStatement()
    {
        throw new NotSupportedException("While loops not yet supported in Interpretation system");
    }

    private Interpretable ParseForStatement()
    {
        throw new NotSupportedException("For loops not yet supported in Interpretation system");
    }

    private Interpretable ParseReturnStatement()
    {
        Consume(TokenType.Return, "Expected 'return'");
        Value? value = null;
        if (!Check(TokenType.Semicolon))
        {
            value = ParseExpression();
        }
        Consume(TokenType.Semicolon, "Expected ';' after return value");
        return value ?? Value.Wrap(0);
    }

    private Interpretable ParseVariableDeclaration()
    {
        var typeToken = Advance();
        var type = typeToken.Value;
        var name = Consume(TokenType.Identifier, "Expected variable name").Value;

        Value? initializer = null;
        if (Match(TokenType.Assign))
        {
            initializer = ParseExpression();
        }
        Consume(TokenType.Semicolon, "Expected ';' after variable declaration");

        var typeDefinition = type switch
        {
            "int" => _context.GetTypeDefinition<int>() ?? throw new InvalidOperationException("Failed to get int type definition"),
            "float" => _context.GetTypeDefinition<float>() ?? throw new InvalidOperationException("Failed to get float type definition"),
            _ => throw new InvalidOperationException($"Unsupported type: {type}")
        };

        var parameter = new Parameter(name, typeDefinition);
        var variable = _context.DeclareVariable(name, parameter);

        // If there's an initializer, create an Assignment; otherwise just evaluate the variable
        if (initializer != null)
        {
            return new Assignment(variable, initializer);
        }

        // No initializer - just return the variable reference itself (which will reference the parameter)
        return variable;
    }

    private Interpretable ParseExpressionStatement()
    {
        var expr = ParseExpression();
        Consume(TokenType.Semicolon, "Expected ';' after expression");
        return expr;
    }

    private Value ParseExpression()
    {
        return ParseConditional();
    }

    private Value ParseConditional()
    {
        var expr = ParseLogicalOr();
        if (Match(TokenType.Question))
        {
            var ifTrue = ParseExpression();
            Consume(TokenType.Colon, "Expected ':' in ternary expression");
            var ifFalse = ParseExpression();
            return new Conditional(ToBool(expr), ifTrue, ifFalse);
        }
        return expr;
    }

    private Value ParseLogicalOr()
    {
        var expr = ParseLogicalAnd();
        while (Match(TokenType.Or))
        {
            var right = ParseLogicalAnd();
            expr = ToBool(expr).Or(ToBool(right));
        }
        return expr;
    }

    private Value ParseLogicalAnd()
    {
        var expr = ParseBitwiseOr();
        while (Match(TokenType.And))
        {
            var right = ParseBitwiseOr();
            expr = ToBool(expr).And(ToBool(right));
        }
        return expr;
    }

    private Value ParseBitwiseOr()
    {
        var expr = ParseBitwiseXor();
        while (Match(TokenType.BitwiseOr))
        {
            var right = ParseBitwiseXor();
            throw new NotSupportedException("Bitwise OR not yet supported");
        }
        return expr;
    }

    private Value ParseBitwiseXor()
    {
        var expr = ParseBitwiseAnd();
        while (Match(TokenType.BitwiseXor))
        {
            var right = ParseBitwiseAnd();
            throw new NotSupportedException("Bitwise XOR not yet supported");
        }
        return expr;
    }

    private Value ParseBitwiseAnd()
    {
        var expr = ParseEquality();
        while (Match(TokenType.BitwiseAnd))
        {
            var right = ParseEquality();
            throw new NotSupportedException("Bitwise AND not yet supported");
        }
        return expr;
    }

    private Value ParseEquality()
    {
        var expr = ParseComparison();
        while (Match(TokenType.Equal, TokenType.NotEqual))
        {
            var op = Previous().Value;
            var right = ParseComparison();
            expr = op == "==" ? expr.Equal(right) : expr.NotEqual(right);
        }
        return expr;
    }

    private Value ParseComparison()
    {
        var expr = ParseShift();
        while (Match(TokenType.LessThan, TokenType.LessThanOrEqual, TokenType.GreaterThan, TokenType.GreaterThanOrEqual))
        {
            var op = Previous().Value;
            var right = ParseShift();
            expr = op switch
            {
                "<" => expr.LessThan(right),
                "<=" => expr.LessThanOrEqual(right),
                ">" => expr.GreaterThan(right),
                ">=" => expr.GreaterThanOrEqual(right),
                _ => throw new InvalidOperationException($"Unknown comparison operator: {op}")
            };
        }
        return expr;
    }

    private Value ParseShift()
    {
        var expr = ParseAdditive();
        while (Match(TokenType.LeftShift, TokenType.RightShift))
        {
            throw new NotSupportedException("Shift operators not yet supported");
        }
        return expr;
    }

    private Value ParseAdditive()
    {
        var expr = ParseMultiplicative();
        while (Match(TokenType.Plus, TokenType.Minus))
        {
            var op = Previous().Value;
            var right = ParseMultiplicative();
            expr = op == "+" ? expr.Add(right) : expr.Subtract(right);
        }
        return expr;
    }

    private Value ParseMultiplicative()
    {
        var expr = ParseUnary();
        while (Match(TokenType.Star, TokenType.Slash, TokenType.Percent))
        {
            var op = Previous().Value;
            var right = ParseUnary();
            expr = op switch
            {
                "*" => expr.Multiply(right),
                "/" => expr.Divide(right),
                "%" => expr.Modulo(right),
                _ => throw new InvalidOperationException($"Unknown multiplicative operator: {op}")
            };
        }
        return expr;
    }

    private Value ParseUnary()
    {
        if (Match(TokenType.Minus, TokenType.Plus, TokenType.Not, TokenType.BitwiseNot, TokenType.Increment, TokenType.Decrement))
        {
            var op = Previous().Value;
            var operand = ParseUnary();
            return op switch
            {
                "-" => operand.Negate(),
                "+" => operand,
                "!" => ToBool(operand).Not(),
                "~" => throw new NotSupportedException("Bitwise NOT not yet supported"),
                "++" => throw new NotSupportedException("Increment operator not yet supported"),
                "--" => throw new NotSupportedException("Decrement operator not yet supported"),
                _ => throw new InvalidOperationException($"Unknown unary operator: {op}")
            };
        }
        return ParsePostfix();
    }

    private Value ParsePostfix()
    {
        var expr = ParsePrimary();
        while (true)
        {
            if (Match(TokenType.LeftBracket))
            {
                var index = ParseExpression();
                Consume(TokenType.RightBracket, "Expected ']'");
                expr = expr.Index(index);
            }
            else if (Match(TokenType.LeftParen))
            {
                throw new NotSupportedException("Function calls not yet supported");
            }
            else if (Match(TokenType.Dot))
            {
                var memberName = Consume(TokenType.Identifier, "Expected member name").Value;
                expr = expr.GetMember(memberName);
            }
            else if (Match(TokenType.Increment, TokenType.Decrement))
            {
                throw new NotSupportedException("Postfix increment/decrement not yet supported");
            }
            else
            {
                break;
            }
        }

        // Handle assignment here after all postfix operations
        if (Match(TokenType.Assign, TokenType.PlusAssign, TokenType.MinusAssign, TokenType.StarAssign, TokenType.SlashAssign))
        {
            var op = Previous().Value;
            var value = ParseExpression();
            
            if (op != "=")
            {
                value = op switch
                {
                    "+=" => expr.Add(value),
                    "-=" => expr.Subtract(value),
                    "*=" => expr.Multiply(value),
                    "/=" => expr.Divide(value),
                    _ => throw new InvalidOperationException($"Unknown assignment operator: {op}")
                };
            }

            if (expr is not Variable v)
            {
                throw new InvalidOperationException("Assignment target must be a variable");
            }
            var targetVar = _context.GetVariable(v.Name) ?? throw new InvalidOperationException($"Variable {v.Name} not found");
            return new Assignment(targetVar, value);
        }

        return expr;
    }

    private Value ParsePrimary()
    {
        return Peek().Type switch
        {
            TokenType.IntLiteral => Value.Wrap(int.Parse(Advance().Value)),
            TokenType.FloatLiteral => Value.Wrap(double.Parse(Advance().Value)),
            TokenType.StringLiteral => Value.Wrap(Advance().Value),
            TokenType.Identifier => GetVariable(Advance().Value),
            TokenType.LeftParen => ParseParenthesizedExpression(),
            _ => throw new InvalidOperationException($"Unexpected token: {Peek()}")
        };
    }

    private Value ParseParenthesizedExpression()
    {
        Consume(TokenType.LeftParen, "Expected '('");
        var expr = ParseExpression();
        Consume(TokenType.RightParen, "Expected ')'");
        return expr;
    }

    private Value ToBool(Value value)
    {
        var type = value.GetTypeDefinition(_context);
        if (type.ClrType == typeof(bool))
        {
            return value;
        }
        return value.NotEqual(Value.Wrap(0));
    }

    private Value GetVariable(string name)
    {
        var variable = _context.GetVariable(name) ?? throw new InvalidOperationException($"Undefined variable: {name}");
        return variable;
    }

    // Helper methods

    private bool Match(params TokenType[] types)
    {
        foreach (var type in types)
        {
            if (Check(type))
            {
                Advance();
                return true;
            }
        }
        return false;
    }

    private bool Check(TokenType type)
    {
        if (IsAtEnd()) return false;
        return Peek().Type == type;
    }

    private C99Token Advance()
    {
        if (!IsAtEnd()) _current++;
        return Previous();
    }

    private bool IsAtEnd()
    {
        return Peek().Type == TokenType.EOF;
    }

    private C99Token Peek()
    {
        return _tokens[_current];
    }

    private C99Token Previous()
    {
        return _tokens[_current - 1];
    }

    private C99Token Consume(TokenType type, string message)
    {
        if (Check(type)) return Advance();
        throw new InvalidOperationException($"{message} at {Peek()}");
    }
}
