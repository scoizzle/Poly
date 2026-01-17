namespace Poly.Tests.Interpretation.C99Repl;

/// <summary>
/// Base class for all C99 AST nodes.
/// </summary>
public abstract class C99AstNode
{
}

/// <summary>
/// Represents a C99 expression.
/// </summary>
public abstract class C99Expression : C99AstNode
{
}

/// <summary>
/// Represents a C99 statement.
/// </summary>
public abstract class C99Statement : C99AstNode
{
}

// Expressions

public sealed class IntLiteralExpression(int value) : C99Expression
{
    public int Value { get; } = value;
}

public sealed class FloatLiteralExpression(double value) : C99Expression
{
    public double Value { get; } = value;
}

public sealed class StringLiteralExpression(string value) : C99Expression
{
    public string Value { get; } = value;
}

public sealed class IdentifierExpression(string name) : C99Expression
{
    public string Name { get; } = name;
}

public sealed class BinaryOpExpression(C99Expression left, string op, C99Expression right) : C99Expression
{
    public C99Expression Left { get; } = left;
    public string Op { get; } = op;
    public C99Expression Right { get; } = right;
}

public sealed class UnaryOpExpression(string op, C99Expression operand, bool isPrefix = true) : C99Expression
{
    public string Op { get; } = op;
    public C99Expression Operand { get; } = operand;
    public bool IsPrefix { get; } = isPrefix;
}

public sealed class AssignmentExpression(C99Expression target, C99Expression value) : C99Expression
{
    public C99Expression Target { get; } = target;
    public C99Expression Value { get; } = value;
}

public sealed class ConditionalExpression(C99Expression condition, C99Expression ifTrue, C99Expression ifFalse) : C99Expression
{
    public C99Expression Condition { get; } = condition;
    public C99Expression IfTrue { get; } = ifTrue;
    public C99Expression IfFalse { get; } = ifFalse;
}

public sealed class CallExpression(string functionName, List<C99Expression> arguments) : C99Expression
{
    public string FunctionName { get; } = functionName;
    public List<C99Expression> Arguments { get; } = arguments;
}

public sealed class ArrayAccessExpression(C99Expression array, C99Expression index) : C99Expression
{
    public C99Expression Array { get; } = array;
    public C99Expression Index { get; } = index;
}

public sealed class MemberAccessExpression(C99Expression target, string memberName) : C99Expression
{
    public C99Expression Target { get; } = target;
    public string MemberName { get; } = memberName;
}

// Statements

public sealed class ExpressionStatement(C99Expression expression) : C99Statement
{
    public C99Expression Expression { get; } = expression;
}

public sealed class BlockStatement(List<C99Statement> statements) : C99Statement
{
    public List<C99Statement> Statements { get; } = statements;
}

public sealed class VariableDeclaration(string type, string name, C99Expression? initializer = null) : C99Statement
{
    public string Type { get; } = type;
    public string Name { get; } = name;
    public C99Expression? Initializer { get; } = initializer;
}

public sealed class IfStatement(C99Expression condition, C99Statement thenBranch, C99Statement? elseBranch = null) : C99Statement
{
    public C99Expression Condition { get; } = condition;
    public C99Statement ThenBranch { get; } = thenBranch;
    public C99Statement? ElseBranch { get; } = elseBranch;
}

public sealed class WhileStatement(C99Expression condition, C99Statement body) : C99Statement
{
    public C99Expression Condition { get; } = condition;
    public C99Statement Body { get; } = body;
}

public sealed class ForStatement(
    C99Statement? initializer,
    C99Expression? condition,
    C99Expression? increment,
    C99Statement body) : C99Statement
{
    public C99Statement? Initializer { get; } = initializer;
    public C99Expression? Condition { get; } = condition;
    public C99Expression? Increment { get; } = increment;
    public C99Statement Body { get; } = body;
}

public sealed class ReturnStatement(C99Expression? value = null) : C99Statement
{
    public C99Expression? Value { get; } = value;
}

public sealed class BreakStatement : C99Statement
{
}

public sealed class ContinueStatement : C99Statement
{
}

// Program

public sealed class C99Program(List<C99Statement> statements) : C99AstNode
{
    public List<C99Statement> Statements { get; } = statements;
}
