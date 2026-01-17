namespace Poly.Tests.Interpretation.C99Repl;

/// <summary>
/// Recursive descent parser for a subset of C99 language.
/// </summary>
public sealed class C99Parser
{
    private readonly List<C99Token> _tokens;
    private int _current;

    public C99Parser(List<C99Token> tokens)
    {
        _tokens = tokens;
    }

    public C99Program Parse()
    {
        var statements = new List<C99Statement>();
        while (!IsAtEnd())
        {
            statements.Add(ParseStatement());
        }
        return new C99Program(statements);
    }

    private C99Statement ParseStatement()
    {
        return Peek().Type switch
        {
            TokenType.LeftBrace => ParseBlockStatement(),
            TokenType.If => ParseIfStatement(),
            TokenType.While => ParseWhileStatement(),
            TokenType.For => ParseForStatement(),
            TokenType.Return => ParseReturnStatement(),
            TokenType.Break => ParseBreakStatement(),
            TokenType.Continue => ParseContinueStatement(),
            TokenType.Int or TokenType.Float => ParseVariableDeclaration(),
            _ => ParseExpressionStatement()
        };
    }

    private C99Statement ParseBlockStatement()
    {
        Consume(TokenType.LeftBrace, "Expected '{'");
        var statements = new List<C99Statement>();
        while (!Check(TokenType.RightBrace) && !IsAtEnd())
        {
            statements.Add(ParseStatement());
        }
        Consume(TokenType.RightBrace, "Expected '}'");
        return new BlockStatement(statements);
    }

    private C99Statement ParseIfStatement()
    {
        Consume(TokenType.If, "Expected 'if'");
        Consume(TokenType.LeftParen, "Expected '(' after 'if'");
        var condition = ParseExpression();
        Consume(TokenType.RightParen, "Expected ')' after condition");
        var thenBranch = ParseStatement();
        C99Statement? elseBranch = null;
        if (Match(TokenType.Else))
        {
            elseBranch = ParseStatement();
        }
        return new IfStatement(condition, thenBranch, elseBranch);
    }

    private C99Statement ParseWhileStatement()
    {
        Consume(TokenType.While, "Expected 'while'");
        Consume(TokenType.LeftParen, "Expected '(' after 'while'");
        var condition = ParseExpression();
        Consume(TokenType.RightParen, "Expected ')' after condition");
        var body = ParseStatement();
        return new WhileStatement(condition, body);
    }

    private C99Statement ParseForStatement()
    {
        Consume(TokenType.For, "Expected 'for'");
        Consume(TokenType.LeftParen, "Expected '(' after 'for'");
        
        C99Statement? initializer = null;
        if (!Check(TokenType.Semicolon))
        {
            if (Peek().Type is TokenType.Int or TokenType.Float)
            {
                initializer = ParseVariableDeclaration();
            }
            else
            {
                initializer = new ExpressionStatement(ParseExpression());
                Consume(TokenType.Semicolon, "Expected ';' after initializer");
            }
        }
        else
        {
            Advance();
        }

        C99Expression? condition = null;
        if (!Check(TokenType.Semicolon))
        {
            condition = ParseExpression();
        }
        Consume(TokenType.Semicolon, "Expected ';' after condition");

        C99Expression? increment = null;
        if (!Check(TokenType.RightParen))
        {
            increment = ParseExpression();
        }
        Consume(TokenType.RightParen, "Expected ')' after for clauses");

        var body = ParseStatement();
        return new ForStatement(initializer, condition, increment, body);
    }

    private C99Statement ParseReturnStatement()
    {
        Consume(TokenType.Return, "Expected 'return'");
        C99Expression? value = null;
        if (!Check(TokenType.Semicolon))
        {
            value = ParseExpression();
        }
        Consume(TokenType.Semicolon, "Expected ';' after return value");
        return new ReturnStatement(value);
    }

    private C99Statement ParseBreakStatement()
    {
        Consume(TokenType.Break, "Expected 'break'");
        Consume(TokenType.Semicolon, "Expected ';' after 'break'");
        return new BreakStatement();
    }

    private C99Statement ParseContinueStatement()
    {
        Consume(TokenType.Continue, "Expected 'continue'");
        Consume(TokenType.Semicolon, "Expected ';' after 'continue'");
        return new ContinueStatement();
    }

    private C99Statement ParseVariableDeclaration()
    {
        var typeToken = Advance();
        var type = typeToken.Value;
        var name = Consume(TokenType.Identifier, "Expected variable name").Value;

        C99Expression? initializer = null;
        if (Match(TokenType.Assign))
        {
            initializer = ParseExpression();
        }
        Consume(TokenType.Semicolon, "Expected ';' after variable declaration");
        return new VariableDeclaration(type, name, initializer);
    }

    private C99Statement ParseExpressionStatement()
    {
        var expr = ParseExpression();
        Consume(TokenType.Semicolon, "Expected ';' after expression");
        return new ExpressionStatement(expr);
    }

    private C99Expression ParseExpression()
    {
        return ParseConditional();
    }

    private C99Expression ParseConditional()
    {
        var expr = ParseLogicalOr();
        if (Match(TokenType.Question))
        {
            var ifTrue = ParseExpression();
            Consume(TokenType.Colon, "Expected ':' in ternary expression");
            var ifFalse = ParseExpression();
            return new ConditionalExpression(expr, ifTrue, ifFalse);
        }
        return expr;
    }

    private C99Expression ParseLogicalOr()
    {
        var expr = ParseLogicalAnd();
        while (Match(TokenType.Or))
        {
            var op = Previous().Value;
            var right = ParseLogicalAnd();
            expr = new BinaryOpExpression(expr, op, right);
        }
        return expr;
    }

    private C99Expression ParseLogicalAnd()
    {
        var expr = ParseBitwiseOr();
        while (Match(TokenType.And))
        {
            var op = Previous().Value;
            var right = ParseBitwiseOr();
            expr = new BinaryOpExpression(expr, op, right);
        }
        return expr;
    }

    private C99Expression ParseBitwiseOr()
    {
        var expr = ParseBitwiseXor();
        while (Match(TokenType.BitwiseOr))
        {
            var op = Previous().Value;
            var right = ParseBitwiseXor();
            expr = new BinaryOpExpression(expr, op, right);
        }
        return expr;
    }

    private C99Expression ParseBitwiseXor()
    {
        var expr = ParseBitwiseAnd();
        while (Match(TokenType.BitwiseXor))
        {
            var op = Previous().Value;
            var right = ParseBitwiseAnd();
            expr = new BinaryOpExpression(expr, op, right);
        }
        return expr;
    }

    private C99Expression ParseBitwiseAnd()
    {
        var expr = ParseEquality();
        while (Match(TokenType.BitwiseAnd))
        {
            var op = Previous().Value;
            var right = ParseEquality();
            expr = new BinaryOpExpression(expr, op, right);
        }
        return expr;
    }

    private C99Expression ParseEquality()
    {
        var expr = ParseComparison();
        while (Match(TokenType.Equal, TokenType.NotEqual))
        {
            var op = Previous().Value;
            var right = ParseComparison();
            expr = new BinaryOpExpression(expr, op, right);
        }
        return expr;
    }

    private C99Expression ParseComparison()
    {
        var expr = ParseShift();
        while (Match(TokenType.LessThan, TokenType.LessThanOrEqual, TokenType.GreaterThan, TokenType.GreaterThanOrEqual))
        {
            var op = Previous().Value;
            var right = ParseShift();
            expr = new BinaryOpExpression(expr, op, right);
        }
        return expr;
    }

    private C99Expression ParseShift()
    {
        var expr = ParseAdditive();
        while (Match(TokenType.LeftShift, TokenType.RightShift))
        {
            var op = Previous().Value;
            var right = ParseAdditive();
            expr = new BinaryOpExpression(expr, op, right);
        }
        return expr;
    }

    private C99Expression ParseAdditive()
    {
        var expr = ParseMultiplicative();
        while (Match(TokenType.Plus, TokenType.Minus))
        {
            var op = Previous().Value;
            var right = ParseMultiplicative();
            expr = new BinaryOpExpression(expr, op, right);
        }
        return expr;
    }

    private C99Expression ParseMultiplicative()
    {
        var expr = ParseUnary();
        while (Match(TokenType.Star, TokenType.Slash, TokenType.Percent))
        {
            var op = Previous().Value;
            var right = ParseUnary();
            expr = new BinaryOpExpression(expr, op, right);
        }
        return expr;
    }

    private C99Expression ParseUnary()
    {
        if (Match(TokenType.Minus, TokenType.Plus, TokenType.Not, TokenType.BitwiseNot, TokenType.Increment, TokenType.Decrement))
        {
            var op = Previous().Value;
            var operand = ParseUnary();
            return new UnaryOpExpression(op, operand, true);
        }
        return ParsePostfix();
    }

    private C99Expression ParsePostfix()
    {
        var expr = ParsePrimary();
        while (true)
        {
            if (Match(TokenType.LeftBracket))
            {
                var index = ParseExpression();
                Consume(TokenType.RightBracket, "Expected ']'");
                expr = new ArrayAccessExpression(expr, index);
            }
            else if (Match(TokenType.LeftParen) && expr is IdentifierExpression id)
            {
                var arguments = new List<C99Expression>();
                if (!Check(TokenType.RightParen))
                {
                    do
                    {
                        arguments.Add(ParseExpression());
                    } while (Match(TokenType.Comma));
                }
                Consume(TokenType.RightParen, "Expected ')' after arguments");
                expr = new CallExpression(id.Name, arguments);
            }
            else if (Match(TokenType.Dot))
            {
                var memberName = Consume(TokenType.Identifier, "Expected member name").Value;
                expr = new MemberAccessExpression(expr, memberName);
            }
            else if (Match(TokenType.Increment, TokenType.Decrement))
            {
                var op = Previous().Value;
                expr = new UnaryOpExpression(op, expr, false);
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
            
            // Convert compound assignments to binary operations
            if (op != "=")
            {
                var binaryOp = op[0].ToString(); // "+", "-", "*", "/"
                value = new BinaryOpExpression(expr, binaryOp, value);
            }
            return new AssignmentExpression(expr, value);
        }

        return expr;
    }

    private C99Expression ParsePrimary()
    {
        return Peek().Type switch
        {
            TokenType.IntLiteral => new IntLiteralExpression(int.Parse(Advance().Value)),
            TokenType.FloatLiteral => new FloatLiteralExpression(double.Parse(Advance().Value)),
            TokenType.StringLiteral => new StringLiteralExpression(Advance().Value),
            TokenType.Identifier => new IdentifierExpression(Advance().Value),
            TokenType.LeftParen => ParseParenthesizedExpression(),
            _ => throw new InvalidOperationException($"Unexpected token: {Peek()}")
        };
    }

    private C99Expression ParseParenthesizedExpression()
    {
        Consume(TokenType.LeftParen, "Expected '('");
        var expr = ParseExpression();
        Consume(TokenType.RightParen, "Expected ')'");
        return expr;
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
