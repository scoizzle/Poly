```csharp
public abstract record Node
{
    /// <summary>
    /// Transforms this node using the provided transformer.
    /// Type information is resolved by semantic analysis middleware, not by the node itself.
    /// </summary>
    public abstract TResult Transform<TResult>(ITransformer<TResult> transformer);
}

public record Constant<T>(T Value) : Node
{
    public override TResult Transform<TResult>(ITransformer<TResult> transformer)
        => transformer.Transform(this);
}

public record Variable(string Name) : Node
{
    public override TResult Transform<TResult>(ITransformer<TResult> transformer)
        => transformer.Transform(this);
}

public record MemberAccess(Node Value, string MemberName) : Node
{
    public override TResult Transform<TResult>(ITransformer<TResult> transformer)
        => transformer.Transform(this);
}

public record Add(Node LeftHandValue, Node RightHandValue) : Node
{
    public override TResult Transform<TResult>(ITransformer<TResult> transformer)
        => transformer.Transform(this);
}
```

/// <summary>
/// Example 1: Simple arithmetic with semantic analysis
/// </summary>
public class Example1_SimpleArithmetic
{
    public void Run()
    {
        // Build AST: 42 + 8
        var ast = new Constant<int>(42)
            .Plus(new Constant<int>(8));

        // Create interpreter
        var interpreter = new InterpreterBuilder<Expression>()
            .UseTypeProvider(ClrTypeDefinitionRegistry.Shared)
            .UseSemanticAnalysis()
            .UseLinqExpression()
            .Build();

        // Interpret
        var expression = interpreter.Interpret(ast);
        
        // Compile and execute
        var lambda = Expression.Lambda<Func<int>>(expression);
        var result = lambda.Compile()();
        
        Console.WriteLine($"Result: {result}"); // Output: Result: 50
    }
}

/// <summary>
/// Example 2: Data model property access
/// </summary>
public class Example2_DataModelPropertyAccess
{
    public void Run()
    {
        // Create a data model
        var person = new DataModelBuilder("Person")
            .Property("Name", StringProperty.Instance)
            .Property("Age", Int32Property.Instance)
            .Build();

        // Create type provider stack
        var dataModelProvider = new DataModelTypeDefinitionProvider(new DataModel[] { person });
        var typeProviders = new TypeDefinitionProviderCollection(
            dataModelProvider,
            ClrTypeDefinitionRegistry.Shared
        );

        // Create interpreter with data model support
        var interpreter = new InterpreterBuilder<Expression>()
            .UseTypeProvider(typeProviders)
            .UseLogging((context, node) =>
            {
                Console.WriteLine($"[Before] Processing: {node.GetType().Name}");
            })
            .UseSemanticAnalysis()
            .UseLogging((context, node) =>
            {
                var type = context.GetResolvedType(node);
                var member = context.GetResolvedMember(node);
                Console.WriteLine($"[After]  Type: {type?.Name ?? "?"}, Member: {member?.Name ?? "?"}");
            })
            .UseLinqExpression()
            .Build();

        // Build AST accessing person.Name
        var personVar = new Variable("person");
        var accessName = new DataModelPropertyAccessor(personVar, "Name", null); // Type resolved during semantic analysis

        // Interpret
        var expression = interpreter.Interpret(accessName);
        
        Console.WriteLine($"Generated expression: {expression}");
    }
}

/// <summary>
/// Example 3: Complex nested property access with validation
/// </summary>
public class Example3_NestedPropertyAccess
{
    public void Run()
    {
        // Create data models
        var address = new DataModelBuilder("Address")
            .Property("Street", StringProperty.Instance)
            .Property("City", StringProperty.Instance)
            .Build();

        var person = new DataModelBuilder("Person")
            .Property("Name", StringProperty.Instance)
            .Property("HomeAddress", new ReferenceProperty("Address"))
            .Build();

        var dataModel = new DataModel[] { person, address };

        // Create type providers
        var dataModelProvider = new DataModelTypeDefinitionProvider(dataModel);
        var typeProviders = new TypeDefinitionProviderCollection(
            dataModelProvider,
            ClrTypeDefinitionRegistry.Shared
        );

        // Create interpreter
        var interpreter = new InterpreterBuilder<Expression>()
            .UseTypeProvider(typeProviders)
            .UseSemanticAnalysis()
            .Use(new ValidationMiddleware()) // Custom middleware: validate semantic rules
            .UseLinqExpression()
            .Build();

        // Build AST: person.HomeAddress.City
        var personVar = new Variable("person");
        var homeAddress = new DataModelPropertyAccessor(personVar, "HomeAddress", null);
        var city = new DataModelPropertyAccessor(homeAddress, "City", null);

        // Interpret (semantic middleware will resolve types through the chain)
        var expression = interpreter.Interpret(city);
        
        Console.WriteLine($"Successfully generated expression for nested access");
    }
}

/// <summary>
/// Example 4: Custom middleware - constant folding optimization
/// </summary>
public class Example4_CustomMiddleware
{
    public class ConstantFoldingMiddleware : ITransformationMiddleware<Expression>
    {
        public Expression Transform(InterpretationContext context, Node node, TransformationDelegate<Expression> next)
        {
            // Optimize: 10 + 20 → 30 (no need to generate Add expression)
            if (node is Add add &&
                add.LeftHandValue is Constant<int> leftConst &&
                add.RightHandValue is Constant<int> rightConst)
            {
                Console.WriteLine($"[Optimization] Folding constant: {leftConst.Value} + {rightConst.Value} = {leftConst.Value + rightConst.Value}");
                return Expression.Constant(leftConst.Value + rightConst.Value);
            }

            // Default: pass to next middleware
            return next(context, node);
        }
    }

    public class DiagnosticsMiddleware : ITransformationMiddleware<Expression>
    {
        public Expression Transform(InterpretationContext context, Node node, TransformationDelegate<Expression> next)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = next(context, node);
            sw.Stop();
            
            Console.WriteLine($"[Diagnostic] {node.GetType().Name} took {sw.ElapsedMilliseconds}ms");
            return result;
        }
    }

    public void Run()
    {
        // Build AST: (10 + 20) + (5 + 3)
        var left = new Constant<int>(10).Plus(new Constant<int>(20));  // Will be folded to 30
        var right = new Constant<int>(5).Plus(new Constant<int>(3));   // Will be folded to 8
        var ast = left.Plus(right);                                     // 30 + 8 = 38

        var interpreter = new InterpreterBuilder<Expression>()
            .UseTypeProvider(ClrTypeDefinitionRegistry.Shared)
            .UseSemanticAnalysis()
            .Use(new ConstantFoldingMiddleware())           // Optimization pass
            .Use(new DiagnosticsMiddleware())               // Profiling pass
            .UseLinqExpression()
            .Build();

        var expression = interpreter.Interpret(ast);
        var lambda = Expression.Lambda<Func<int>>(expression);
        var result = lambda.Compile()();
        
        Console.WriteLine($"Final result: {result}"); // Output: Final result: 38
    }
}

/// <summary>
/// Example 5: Reusing context across multiple AST fragments
/// </summary>
public class Example5_SharedContext
{
    public void Run()
    {
        // Create interpreter
        var interpreter = new InterpreterBuilder<Expression>()
            .UseTypeProvider(ClrTypeDefinitionRegistry.Shared)
            .UseSemanticAnalysis()
            .UseLinqExpression()
            .Build();

        // Create a context to share across multiple interpretations
        var context = new InterpretationContext(ClrTypeDefinitionRegistry.Shared);
        
        // Define parameter (variable)
        var x = Expression.Parameter(typeof(int), "x");
        context.Properties["x"] = x;

        // Interpret first AST: x + 10
        var add10 = new Variable("x").Plus(new Constant<int>(10));
        var expr1 = interpreter.Interpret(add10, context);
        
        // Interpret second AST: x * 2
        var mul2 = new Variable("x").Plus(new Constant<int>(2));
        var expr2 = interpreter.Interpret(mul2, context);

        // Both expressions now share the same parameter binding
        var lambda1 = Expression.Lambda<Func<int, int>>(expr1, x);
        var lambda2 = Expression.Lambda<Func<int, int>>(expr2, x);

        Console.WriteLine($"x + 10 where x=5: {lambda1.Compile()(5)}"); // Output: 15
        Console.WriteLine($"x + 2 where x=5: {lambda2.Compile()(5)}");  // Output: 7
    }
}

/// <summary>
/// Example 6: Validation middleware - ensure types match at compile time
/// </summary>
public class Example6_ValidationMiddleware
{
    public class TypeCheckingMiddleware : ITransformationMiddleware<Expression>
    {
        public Expression Transform(InterpretationContext context, Node node, TransformationDelegate<Expression> next)
        {
            // Validate binary operations have compatible types
            if (node is BinaryOperator binOp)
            {
                var leftType = context.GetResolvedType(binOp.LeftHandValue);
                var rightType = context.GetResolvedType(binOp.RightHandValue);

                if (leftType != null && rightType != null)
                {
                    if (leftType.ReflectedType != rightType.ReflectedType)
                    {
                        throw new InvalidOperationException(
                            $"Type mismatch in {node.GetType().Name}: " +
                            $"{leftType.Name} and {rightType.Name} are incompatible");
                    }
                }
            }

            return next(context, node);
        }
    }

    public void Run()
    {
        var interpreter = new InterpreterBuilder<Expression>()
            .UseTypeProvider(ClrTypeDefinitionRegistry.Shared)
            .UseSemanticAnalysis()
            .Use(new TypeCheckingMiddleware())
            .UseLinqExpression()
            .Build();

        // This works: int + int
        var validAst = new Constant<int>(5).Plus(new Constant<int>(3));
        var expr = interpreter.Interpret(validAst);
        
        Console.WriteLine($"Valid operation compiled successfully");

        // This would fail: int + string
        try
        {
            var invalidAst = new Constant<int>(5).Plus(new Constant<string>("hello"));
            expr = interpreter.Interpret(invalidAst);
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"Caught validation error: {ex.Message}");
        }
    }
}

/// <summary>
/// Example 7: Full pipeline with all stages visible
/// </summary>
public class Example7_FullPipeline
{
    public class LoggingMiddleware : ITransformationMiddleware<Expression>
    {
        private readonly string _stage;

        public LoggingMiddleware(string stage) => _stage = stage;

        public Expression Transform(InterpretationContext context, Node node, TransformationDelegate<Expression> next)
        {
            Console.WriteLine($"  [{_stage}] → {node.GetType().Name}");
            
            var result = next(context, node);
            
            if (result != null)
            {
                var nodeType = context.GetResolvedType(node);
                Console.WriteLine($"  [{_stage}] ← {result.GetType().Name} (type: {nodeType?.Name ?? "?"})");
            }

            return result;
        }
    }

    public void Run()
    {
        Console.WriteLine("=== Full Pipeline Example ===\n");

        // Build complex AST: (x + 10) * 2
        var x = new Variable("x");
        var addTen = x.Plus(new Constant<int>(10));
        var timesTwo = addTen.Multiply(new Constant<int>(2));

        // Create interpreter with visibility into each stage
        var interpreter = new InterpreterBuilder<Expression>()
            .UseTypeProvider(ClrTypeDefinitionRegistry.Shared)
            .UseLogging((ctx, node) => Console.WriteLine($"Raw AST: {node.GetType().Name}"))
            .Use(new LoggingMiddleware("Semantic"))
            .UseSemanticAnalysis()
            .Use(new LoggingMiddleware("CodeGen"))
            .UseLinqExpression()
            .Build();

        var expression = interpreter.Interpret(timesTwo);
        
        Console.WriteLine($"\nFinal expression type: {expression.GetType().Name}");
        Console.WriteLine($"Final expression: {expression}");
    }
}

// Usage: Run the examples
public class Program
{
    public static void Main()
    {
        Console.WriteLine("Example 1: Simple Arithmetic\n");
        new Example1_SimpleArithmetic().Run();

        Console.WriteLine("\n\nExample 2: Data Model Property Access\n");
        new Example2_DataModelPropertyAccess().Run();

        Console.WriteLine("\n\nExample 3: Nested Property Access\n");
        new Example3_NestedPropertyAccess().Run();

        Console.WriteLine("\n\nExample 4: Custom Middleware\n");
        new Example4_CustomMiddleware().Run();

        Console.WriteLine("\n\nExample 5: Shared Context\n");
        new Example5_SharedContext().Run();

        Console.WriteLine("\n\nExample 6: Validation Middleware\n");
        new Example6_ValidationMiddleware().Run();

        Console.WriteLine("\n\nExample 7: Full Pipeline\n");
        new Example7_FullPipeline().Run();
    }
}
