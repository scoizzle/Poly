using System.Linq.Expressions;
using Poly.Interpretation;

namespace Poly.Tests.Interpretation.C99Repl;

/// <summary>
/// Compiles C99 AST to Poly Interpretation DSL constructs.
/// </summary>
public sealed class C99Compiler
{
    private sealed class ScopeFrame : IDisposable
    {
        private readonly C99Compiler _compiler;
        private bool _disposed;

        public List<ParameterExpression> Variables { get; } = new();

        public ScopeFrame(C99Compiler compiler)
        {
            _compiler = compiler;
            _compiler.Context.PushScope();
            _compiler._scopeStack.Push(this);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _compiler._scopeStack.Pop();
            _compiler.Context.PopScope();
        }
    }

    private readonly Func<InterpretationContext> _contextFactory;
    private InterpretationContext? _context;
    private Stack<ScopeFrame> _scopeStack = new();

    public C99Compiler(Func<InterpretationContext>? contextFactory = null)
    {
        _contextFactory = contextFactory ?? (() => new InterpretationContext());
    }

    public InterpretationContext Context => _context ?? throw new InvalidOperationException("No active compilation context.");

    public Value Compile(C99Program program)
    {
        _context = _contextFactory();
        _scopeStack = new Stack<ScopeFrame>();
        using var scope = EnterScope();

        var expressions = new List<Interpretable>();

        foreach (var statement in program.Statements)
        {
            var expr = CompileStatement(statement);
            if (expr != null)
            {
                expressions.Add(expr);
            }
        }

        if (expressions.Count == 0)
        {
            return Value.Wrap(0);
        }

        if (expressions.Count == 1 && scope.Variables.Count == 0)
        {
            return (Value)expressions[0];
        }

        var blockExpr = scope.Variables.Count > 0
            ? new Block(expressions, scope.Variables)
            : new Block(expressions.ToArray());

        return blockExpr;
    }

    private Interpretable? CompileStatement(C99Statement statement)
    {
        return statement switch
        {
            ExpressionStatement expr => CompileExpression(expr.Expression),
            BlockStatement block => CompileBlockStatement(block),
            VariableDeclaration varDecl => CompileVariableDeclaration(varDecl),
            IfStatement ifStmt => CompileIfStatement(ifStmt),
            WhileStatement whileStmt => CompileWhileStatement(whileStmt),
            ForStatement forStmt => CompileForStatement(forStmt),
            ReturnStatement returnStmt => CompileReturnStatement(returnStmt),
            BreakStatement => throw new NotSupportedException("Break statements not yet supported"),
            ContinueStatement => throw new NotSupportedException("Continue statements not yet supported"),
            _ => throw new InvalidOperationException($"Unknown statement type: {statement.GetType()}")
        };
    }

    private Interpretable CompileBlockStatement(BlockStatement block)
    {
        using var scope = EnterScope();

        var expressions = new List<Interpretable>();
        
        foreach (var statement in block.Statements)
        {
            var expr = CompileStatement(statement);
            if (expr != null)
            {
                expressions.Add(expr);
            }
        }

        if (expressions.Count == 0)
        {
            return Value.Wrap(0);
        }

        return scope.Variables.Count > 0
            ? new Block(expressions, scope.Variables)
            : new Block(expressions.ToArray());
    }

    private Interpretable CompileVariableDeclaration(VariableDeclaration varDecl)
    {
        var typeDefinition = varDecl.Type switch
        {
            "int" => Context.GetTypeDefinition<int>() ?? throw new InvalidOperationException("Failed to get int type definition"),
            "float" => Context.GetTypeDefinition<float>() ?? throw new InvalidOperationException("Failed to get float type definition"),
            _ => throw new InvalidOperationException($"Unsupported type: {varDecl.Type}")
        };

        var parameter = new Parameter(varDecl.Name, typeDefinition);
        var variable = Context.DeclareVariable(varDecl.Name, parameter);

        // Track block-local variables so they are declared in the resulting Expression.Block
        CurrentScope.Variables.Add(parameter.BuildExpression(Context));

        var initialValue = varDecl.Initializer != null
            ? CompileExpression(varDecl.Initializer)
            : varDecl.Type switch
            {
                "int" => Value.Wrap(0),
                "float" => Value.Wrap(0.0f),
                _ => throw new InvalidOperationException($"Unsupported type: {varDecl.Type}")
            };

        return new Assignment(variable, initialValue);
    }

    private Interpretable CompileIfStatement(IfStatement ifStmt)
    {
        var condition = ToBool(CompileExpression(ifStmt.Condition));
        
        var thenExpr = CompileStatement(ifStmt.ThenBranch);
        var thenValue = EnsureValue(thenExpr);
        
        if (ifStmt.ElseBranch == null)
        {
            // if without else: condition ? thenBranch : 0
            return condition.Conditional(thenValue, Value.Wrap(0));
        }
        
        var elseExpr = CompileStatement(ifStmt.ElseBranch);
        var elseValue = EnsureValue(elseExpr);
        
        return condition.Conditional(thenValue, elseValue);
    }

    private Interpretable CompileWhileStatement(WhileStatement whileStmt)
    {
        throw new NotSupportedException("While loops not yet supported in Interpretation system");
    }

    private Interpretable CompileForStatement(ForStatement forStmt)
    {
        throw new NotSupportedException("For loops not yet supported in Interpretation system");
    }

    private Interpretable CompileReturnStatement(ReturnStatement returnStmt)
    {
        if (returnStmt.Value == null)
        {
            return Value.Wrap(0);
        }
        return CompileExpression(returnStmt.Value);
    }

    private Value CompileExpression(C99Expression expression)
    {
        return expression switch
        {
            IntLiteralExpression intLit => Value.Wrap(intLit.Value),
            FloatLiteralExpression floatLit => Value.Wrap(floatLit.Value),
            StringLiteralExpression stringLit => Value.Wrap(stringLit.Value),
            IdentifierExpression id => GetVariable(id.Name),
            BinaryOpExpression binOp => CompileBinaryOp(binOp),
            UnaryOpExpression unaryOp => CompileUnaryOp(unaryOp),
            AssignmentExpression assign => CompileAssignment(assign),
            ConditionalExpression cond => CompileConditional(cond),
            CallExpression call => CompileCallExpression(call),
            ArrayAccessExpression arrayAccess => CompileArrayAccess(arrayAccess),
            MemberAccessExpression memberAccess => CompileMemberAccess(memberAccess),
            _ => throw new InvalidOperationException($"Unknown expression type: {expression.GetType()}")
        };
    }

    private Value ToBool(Value value)
    {
        // Convert to bool for use in conditions
        // If already a boolean type, return as-is
        var type = value.GetTypeDefinition(Context);
        if (type.ClrType == typeof(bool))
        {
            return value;
        }
        
        // Otherwise convert: 0 -> false, non-zero -> true
        return value.NotEqual(Value.Wrap(0));
    }

    private Value CompileBinaryOp(BinaryOpExpression binOp)
    {
        var left = CompileExpression(binOp.Left);
        var right = CompileExpression(binOp.Right);

        return binOp.Op switch
        {
            "+" => left.Add(right),
            "-" => left.Subtract(right),
            "*" => left.Multiply(right),
            "/" => left.Divide(right),
            "%" => left.Modulo(right),
            "==" => left.Equal(right),
            "!=" => left.NotEqual(right),
            "<" => left.LessThan(right),
            "<=" => left.LessThanOrEqual(right),
            ">" => left.GreaterThan(right),
            ">=" => left.GreaterThanOrEqual(right),
            "&&" => ToBool(left).And(ToBool(right)),
            "||" => ToBool(left).Or(ToBool(right)),
            "&" => throw new NotSupportedException("Bitwise AND not yet supported"),
            "|" => throw new NotSupportedException("Bitwise OR not yet supported"),
            "^" => throw new NotSupportedException("Bitwise XOR not yet supported"),
            "<<" => throw new NotSupportedException("Left shift not yet supported"),
            ">>" => throw new NotSupportedException("Right shift not yet supported"),
            _ => throw new InvalidOperationException($"Unknown binary operator: {binOp.Op}")
        };
    }

    private Value CompileUnaryOp(UnaryOpExpression unaryOp)
    {
        var operand = CompileExpression(unaryOp.Operand);

        return unaryOp.Op switch
        {
            "-" => operand.Negate(),
            "+" => operand,
            "!" => ToBool(operand).Not(),
            "~" => throw new NotSupportedException("Bitwise NOT not yet supported"),
            "++" => throw new NotSupportedException("Increment operator not yet supported"),
            "--" => throw new NotSupportedException("Decrement operator not yet supported"),
            _ => throw new InvalidOperationException($"Unknown unary operator: {unaryOp.Op}")
        };
    }

    private Value CompileAssignment(AssignmentExpression assign)
    {
        if (assign.Target is not IdentifierExpression id)
        {
            throw new NotSupportedException("Only simple variable assignment is supported");
        }

        var value = CompileExpression(assign.Value);
        
        var variable = Context.GetVariable(id.Name) ?? throw new InvalidOperationException($"Variable {id.Name} not found");
        
        // Create an assignment expression: variable = value
        var assignment = new Assignment(variable, value);
        
        return assignment;
    }

    private Value CompileConditional(ConditionalExpression cond)
    {
        var condition = ToBool(CompileExpression(cond.Condition));
        var ifTrue = CompileExpression(cond.IfTrue);
        var ifFalse = CompileExpression(cond.IfFalse);
        return condition.Conditional(ifTrue, ifFalse);
    }

    private Value CompileCallExpression(CallExpression call)
    {
        throw new NotSupportedException("Function calls not yet supported");
    }

    private Value CompileArrayAccess(ArrayAccessExpression arrayAccess)
    {
        throw new NotSupportedException("Array access not yet supported");
    }

    private Value CompileMemberAccess(MemberAccessExpression memberAccess)
    {
        var target = CompileExpression(memberAccess.Target);
        return target.GetMember(memberAccess.MemberName);
    }

    private Value GetVariable(string name)
    {
        var variable = Context.GetVariable(name) ?? throw new InvalidOperationException($"Undefined variable: {name}");
        
        return variable;
    }

    private static Value EnsureValue(Interpretable? expr) => expr switch
    {
        Value v => v,
        null => throw new InvalidOperationException("Expected expression to produce a value"),
        _ => throw new InvalidOperationException("Expected expression to produce a value")
    };

    private ScopeFrame EnterScope() => new ScopeFrame(this);

    private ScopeFrame CurrentScope => _scopeStack.Peek();
}