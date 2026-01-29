# Middleware-Based Interpreter Implementation Plan

## Status: Phase 1-3 Complete ✅ | Restoration Phase In Progress 🔄

**Completed:**
- ✅ Core infrastructure (TransformationDelegate, ITransformationMiddleware, InterpreterBuilder, Interpreter)
- ✅ InterpretationContext enhancements (TypeProvider, Variables, ScopeStack, Properties)
- ✅ SemanticExtensions with context caching
- ✅ Node hierarchy converted to immutable records (35+ classes)
- ✅ Compatibility layer (BuildNode/ToParameterExpression extensions)
- ✅ All projects compile (0 errors, 0 warnings)

**Current Priority: Restore Pre-Refactor Functionality**
The middleware architecture is in place, but semantic analysis and type resolution need to be fully implemented to restore all functionality that existed before the Node refactor. This includes making all existing test cases pass.

## Overview

Implement an ASP.NET Core-inspired middleware pipeline for AST interpretation that performs semantic analysis and code generation in a single pass. The pipeline uses `InterpreterBuilder<TResult>` for configuration, `Interpreter<TResult>` for orchestration, and `InterpretationContext` for sharing state across middleware.

## Architecture

```
InterpreterBuilder<TResult>
  ├─ Configures middleware pipeline
  ├─ Holds ITypeDefinitionProvider
  └─ .Build() → Interpreter<TResult>

Interpreter<TResult>
  ├─ Owns the built pipeline
  ├─ Creates InterpretationContext
  └─ .Interpret(ast) → TResult

InterpretationContext
  ├─ References ITypeDefinitionProvider
  ├─ Tracks scope, variables, resolved symbols
  └─ Passed through middleware pipeline

Middleware Pipeline
  ├─ Each middleware enriches/transforms
  └─ Single pass through AST

Node Hierarchy
  ├─ Simple immutable records (no type resolution logic)
  ├─ AST data structure only
  └─ Type information resolved by semantic middleware, not nodes
```

## Node Definition

Nodes are **pure data structures** with no semantic responsibility:

```csharp
public abstract record Node
{
    /// <summary>
    /// Transforms this node using the provided transformer.
    /// Type information is resolved by semantic analysis middleware, not by the node itself.
    /// </summary>
    public abstract TResult Transform<TResult>(ITransformer<TResult> transformer);
}
```

**Key principle:** Nodes do NOT have a `GetTypeDefinition()` method. Type resolution is the exclusive responsibility of semantic analysis middleware. This separation of concerns keeps nodes clean, testable, and decoupled from the type system.

## Core Delegate Signature

**CRITICAL**: Context comes first, enabling natural extension method chaining:

```csharp
public delegate TResult TransformationDelegate<TResult>(InterpretationContext context, Node node);
```

## Semantic Analysis Information Transfer

Semantic analysis produces rich information that needs to flow through the pipeline:
- Resolved types for nodes
- Resolved members for access operations
- Variable bindings and scope

The approach is to **cache semantic info in context using extension methods**. This provides:
- ✅ Clean, discoverable API via IntelliSense
- ✅ Efficient caching using `ReferenceEqualityComparer` for node identity
- ✅ No node mutation—nodes remain immutable data structures
- ✅ Request-scoped storage in `InterpretationContext.Properties`
- ✅ Easy to extend with additional semantic properties

## Custom Transformer Registry (Domain-Specific Overrides)
/// Terminal middleware that delegates to an injected ITransformer (e.g., LINQ expression transformer).
/// Custom transformers (registry) can short-circuit before reaching this.
Goal: allow domain-specific transformations (e.g., DataModel member access) without polluting the core AST.

### Interfaces
    private readonly ITransformer<Expression> _transformer;

    public LinqExpressionMiddleware(ITransformer<Expression> transformer)
    {
        _transformer = transformer;
    }

    public Expression Transform(InterpretationContext context, Node node, TransformationDelegate<Expression> next)
    {
        // If a custom transformer handled it, we never get called. Otherwise, produce the expression here.
        return _transformer.Transform(node);
    }
**File**: `Poly/Interpretation/TransformationDelegate.cs`

```csharp
namespace Poly.Interpretation;

/// <summary>
/// Represents a transformation operation in the middleware pipeline.
/// </summary>
/// <param name="context">The interpretation context containing type information and state.</param>
/// <param name="node">The AST node to transform.</param>
/// <returns>The transformation result.</returns>
public delegate TResult TransformationDelegate<TResult>(InterpretationContext context, Node node);
```

#### 1.2 Create `ITransformationMiddleware<TResult>`

**File**: `Poly/Interpretation/ITransformationMiddleware.cs`

```csharp
namespace Poly.Interpretation;

/// <summary>
/// Represents middleware in the transformation pipeline.
/// Middleware can inspect, modify, or enhance nodes before passing to the next stage.
/// </summary>
public interface ITransformationMiddleware<TResult>
{
    /// <summary>
    /// Transforms a node, potentially enriching it before passing to the next middleware.
    /// </summary>
    /// <param name="context">The interpretation context.</param>
    /// <param name="node">The AST node to transform.</param>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <returns>The transformation result.</returns>
    TResult Transform(InterpretationContext context, Node node, TransformationDelegate<TResult> next);
}
```

#### 1.3 Enhance `InterpretationContext`

**File**: `Poly/Interpretation/InterpretationContext.cs`

```csharp
namespace Poly.Interpretation;

public class InterpretationContext
{
    public ITypeDefinitionProvider TypeProvider { get; }
    
    /// <summary>
    /// Variable bindings for the current scope.
    /// </summary>
    public Dictionary<string, ITypeDefinition> Variables { get; } = new();
    
    /// <summary>
    /// Type scope stack for nested scopes.
    /// </summary>
    public Stack<ITypeDefinition> ScopeStack { get; } = new();
    
    /// <summary>
    /// Request-scoped properties for storing middleware-specific data.
    /// Similar to HttpContext.Items in ASP.NET Core.
    /// </summary>
    public Dictionary<string, object?> Properties { get; } = new();

    public InterpretationContext(ITypeDefinitionProvider typeProvider)
    {
        TypeProvider = typeProvider ?? throw new ArgumentNullException(nameof(typeProvider));
    }
}
```

#### 1.4 Create Semantic Extensions

**File**: `Poly/Interpretation/SemanticExtensions.cs`

```csharp
namespace Poly.Interpretation;

/// <summary>
/// Extension methods for accessing and storing semantic analysis information in InterpretationContext.
/// </summary>
public static class SemanticExtensions
{
    private const string SemanticInfoKey = "__SemanticInfo__";
    
    private static Dictionary<Node, SemanticInfo> GetCache(InterpretationContext context)
    {
        if (!context.Properties.TryGetValue(SemanticInfoKey, out var cache))
        {
            cache = new Dictionary<Node, SemanticInfo>(ReferenceEqualityComparer.Instance);
            context.Properties[SemanticInfoKey] = cache;
        }
        return (Dictionary<Node, SemanticInfo>)cache!;
    }
    
    public static ITypeDefinition? GetResolvedType(this InterpretationContext context, Node node)
    {
        var cache = GetCache(context);
        return cache.TryGetValue(node, out var info) ? info.ResolvedType : null;
    }
    
    public static void SetResolvedType(this InterpretationContext context, Node node, ITypeDefinition type)
    {
        var cache = GetCache(context);
        var info = cache.TryGetValue(node, out var existing) ? existing : new SemanticInfo();
        cache[node] = info with { ResolvedType = type };
    }
    
    public static ITypeMember? GetResolvedMember(this InterpretationContext context, Node node)
    {
        var cache = GetCache(context);
        return cache.TryGetValue(node, out var info) ? info.ResolvedMember : null;
    }
    
    public static void SetResolvedMember(this InterpretationContext context, Node node, ITypeMember member)
    {
        var cache = GetCache(context);
        var info = cache.TryGetValue(node, out var existing) ? existing : new SemanticInfo();
        cache[node] = info with { ResolvedMember = member };
    }
    
    public static bool HasSemanticInfo(this InterpretationContext context, Node node)
    {
        var cache = GetCache(context);
        return cache.ContainsKey(node);
    }
    
    public static SemanticInfo GetSemanticInfo(this InterpretationContext context, Node node)
    {
        var cache = GetCache(context);
        return cache.TryGetValue(node, out var info) ? info : new SemanticInfo();
    }
}

public record SemanticInfo
{
    public ITypeDefinition? ResolvedType { get; init; }
    public ITypeMember? ResolvedMember { get; init; }
}
```

#### 1.5 Create `InterpreterBuilder<TResult>`

**File**: `Poly/Interpretation/InterpreterBuilder.cs`

```csharp
namespace Poly.Interpretation;

/// <summary>
/// Builds an interpreter with a configured middleware pipeline.
/// </summary>
public class InterpreterBuilder<TResult>
{
    private readonly List<Func<TransformationDelegate<TResult>, TransformationDelegate<TResult>>> _components = new();
    private ITypeDefinitionProvider? _typeProvider;

    /// <summary>
    /// Sets the type definition provider for the interpreter.
    /// </summary>
    public InterpreterBuilder<TResult> UseTypeProvider(ITypeDefinitionProvider provider)
    {
        _typeProvider = provider ?? throw new ArgumentNullException(nameof(provider));
        return this;
    }

    /// <summary>
    /// Adds middleware to the pipeline.
    /// </summary>
    public InterpreterBuilder<TResult> Use(ITransformationMiddleware<TResult> middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        
        _components.Add(next =>
        {
            return (context, node) => middleware.Transform(context, node, next);
        });
        return this;
    }

    /// <summary>
    /// Adds inline middleware using a delegate.
    /// </summary>
    public InterpreterBuilder<TResult> Use(
        Func<InterpretationContext, Node, TransformationDelegate<TResult>, TResult> middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        
        _components.Add(next =>
        {
            return (context, node) => middleware(context, node, next);
        });
        return this;
    }

    /// <summary>
    /// Builds the interpreter with the configured middleware pipeline.
    /// </summary>
    public Interpreter<TResult> Build()
    {
        if (_typeProvider == null)
        {
            throw new InvalidOperationException(
                "Type provider must be configured. Call UseTypeProvider() before Build().");
        }

        // Build pipeline from middleware components (reverse order like ASP.NET Core)
        TransformationDelegate<TResult> pipeline = (context, node) =>
        {
            throw new InvalidOperationException(
                "Pipeline reached end without producing a result. " +
                "Add a terminal middleware that returns a value.");
        };

        for (int i = _components.Count - 1; i >= 0; i--)
        {
            pipeline = _components[i](pipeline);
        }

        return new Interpreter<TResult>(pipeline, _typeProvider);
    }
}
```

#### 1.6 Create `Interpreter<TResult>`

**File**: `Poly/Interpretation/Interpreter.cs`

```csharp
namespace Poly.Interpretation;

/// <summary>
/// Interprets an AST using a configured middleware pipeline.
/// </summary>
public class Interpreter<TResult>
{
    private readonly TransformationDelegate<TResult> _pipeline;
    private readonly ITypeDefinitionProvider _typeProvider;

    internal Interpreter(TransformationDelegate<TResult> pipeline, ITypeDefinitionProvider typeProvider)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _typeProvider = typeProvider ?? throw new ArgumentNullException(nameof(typeProvider));
    }

    /// <summary>
    /// Interprets the AST, creating a fresh interpretation context.
    /// </summary>
    public TResult Interpret(Node ast)
    {
        ArgumentNullException.ThrowIfNull(ast);
        
        var context = new InterpretationContext(_typeProvider);
        return _pipeline(context, ast);
    }

    /// <summary>
    /// Interprets the AST using an existing context.
    /// Useful for interpreting multiple AST fragments in the same scope.
    /// </summary>
    public TResult Interpret(Node ast, InterpretationContext context)
    {
        ArgumentNullException.ThrowIfNull(ast);
        ArgumentNullException.ThrowIfNull(context);
        
        return _pipeline(context, ast);
    }
}
```

### Phase 2: Semantic Analysis Middleware

#### 2.1 Create `SemanticAnalysisMiddleware<TResult>`

**File**: `Poly/Interpretation/Middleware/SemanticAnalysisMiddleware.cs`

```csharp
namespace Poly.Interpretation.Middleware;

/// <summary>
/// Middleware that enriches AST nodes with semantic information (resolved types, members, etc.).
/// </summary>
public class SemanticAnalysisMiddleware<TResult> : ITransformationMiddleware<TResult>
{
    public TResult Transform(InterpretationContext context, Node node, TransformationDelegate<TResult> next)
    {
        // Skip if already analyzed
        if (context.HasSemanticInfo(node))
        {
            return next(context, node);
        }

        // Resolve and cache type information via provider (nodes do not resolve themselves)
        var resolvedType = context.GetResolvedType(node);
        if (resolvedType != null)
        {
            context.SetResolvedType(node, resolvedType);
        }

        // Handle specific node types
        switch (node)
        {
            case MemberAccess memberAccess:
                AnalyzeMemberAccess(context, memberAccess);
                break;
                
            case InvocationOperator invocation:
                AnalyzeInvocation(context, invocation);
                break;
        }

        // Pass enriched node to next middleware
        return next(context, node);
    }

    private void AnalyzeMemberAccess(InterpretationContext context, MemberAccess memberAccess)
    {
        var instanceType = context.GetResolvedType(memberAccess.Value) 
            ?? ResolveNodeType(context, memberAccess.Value);
        
        if (instanceType != null)
        {
            var member = instanceType.Members.FirstOrDefault(m => m.Name == memberAccess.MemberName);
            if (member != null)
            {
                context.SetResolvedMember(memberAccess, member);
                context.SetResolvedType(memberAccess, member.MemberTypeDefinition);
            }
        }
    }

    private void AnalyzeInvocation(InterpretationContext context, InvocationOperator invocation)
    {
        var targetType = context.GetResolvedType(invocation.Target)
            ?? ResolveNodeType(context, invocation.Target);
        
        if (targetType != null)
        {
            var argumentTypes = invocation.Arguments
                .Select(arg => context.GetResolvedType(arg) ?? ResolveNodeType(context, arg))
                .Where(t => t != null)
                .ToList();
            
            var methods = targetType.FindMatchingMethodOverloads(invocation.MethodName, argumentTypes!);
            var method = methods.FirstOrDefault();
            
            if (method != null)
            {
                context.SetResolvedMember(invocation, method);
                context.SetResolvedType(invocation, method.MemberTypeDefinition);
            }
        }
    }

    private static ITypeDefinition? ResolveNodeType(InterpretationContext context, Node node)
    {
        // Implement using context.TypeProvider and node shape; nodes do not resolve themselves.
        return context.TypeProvider.Resolve(node);
    }
}

```

### Phase 3: LINQ Expression Generation Middleware

Prefer keeping middleware as the orchestrator and using a well-defined `ITransformer` as the terminal adapter. The terminal middleware should:
- Assume semantic info already populated (types/members in context).
- Look for custom transformers first (registry), then delegate to the injected `ITransformer` for the default path.
- Remain swap-friendly (LINQ, delegates, interpreter) without changing upstream middleware.

#### 3.1 Create `LinqExpressionMiddleware`

**File**: `Poly/Interpretation/Middleware/LinqExpressionMiddleware.cs`

```csharp
namespace Poly.Interpretation.Middleware;

/// <summary>
/// Terminal middleware that generates LINQ expressions from semantically-analyzed AST nodes.
/// </summary>
public class LinqExpressionMiddleware : ITransformationMiddleware<Expression>
{
    private readonly ITransformer<Expression> _transformer;

    public LinqExpressionMiddleware(ITransformer<Expression> transformer)
    {
        _transformer = transformer;
    }

    public Expression Transform(InterpretationContext context, Node node, TransformationDelegate<Expression> next)
    {
        // If a custom transformer handled it, we never get called. Otherwise, produce the expression here.
        return _transformer.Transform(node);
    }
}
        return param;
    }
}
```

### Phase 4: Extension Methods & Fluent API

#### 4.1 Create Builder Extension Methods

**File**: `Poly/Interpretation/InterpreterBuilderExtensions.cs`

```csharp
namespace Poly.Interpretation;

public static class InterpreterBuilderExtensions
{
    /// <summary>
    /// Adds semantic analysis middleware to the pipeline.
    /// </summary>
    public static InterpreterBuilder<TResult> UseSemanticAnalysis<TResult>(this InterpreterBuilder<TResult> builder)
    {
        return builder.Use(new SemanticAnalysisMiddleware<TResult>());
    }
    
    /// <summary>
    /// Adds LINQ expression generation middleware to the pipeline.
    /// </summary>
    public static InterpreterBuilder<Expression> UseLinqExpression(this InterpreterBuilder<Expression> builder)
    {
        return builder.Use(new LinqExpressionMiddleware());
    }
    
    /// <summary>
    /// Adds logging middleware that logs each node transformation.
    /// </summary>
    public static InterpreterBuilder<TResult> UseLogging<TResult>(
        this InterpreterBuilder<TResult> builder,
        Action<InterpretationContext, Node> logger)
    {
        return builder.Use((context, node, next) =>
        {
            logger(context, node);
            return next(context, node);
        });
    }
    
    /// <summary>
    /// Adds data model support to the type provider stack.
    /// </summary>
    public static InterpreterBuilder<TResult> UseDataModel<TResult>(
        this InterpreterBuilder<TResult> builder,
        DataModel model)
    {
        var provider = new DataModelTypeDefinitionProvider(model);
        
        // Wrap with TypeDefinitionProviderCollection if needed
        // This would require checking if existing provider is already a collection
        return builder.UseTypeProvider(provider);
    }
}
```

### Phase 5: Usage Examples

#### Example 1: Basic Arithmetic

```csharp
var ast = new Constant<int>(42).Plus(new Constant<int>(8));

var interpreter = new InterpreterBuilder<Expression>()
    .UseTypeProvider(ClrTypeDefinitionRegistry.Shared)
    .UseSemanticAnalysis()
    .UseLinqExpression()
    .Build();

var expression = interpreter.Interpret(ast);
var lambda = Expression.Lambda<Func<int>>(expression);
var result = lambda.Compile()(); // 50
```

#### Example 2: Data Model with Logging

```csharp
var dataModelProvider = new DataModelTypeDefinitionProvider(dataModel);
var providers = new TypeDefinitionProviderCollection(
    dataModelProvider,
    ClrTypeDefinitionRegistry.Shared
);

var interpreter = new InterpreterBuilder<Expression>()
    .UseTypeProvider(providers)
    .UseLogging((ctx, node) => Console.WriteLine($"Processing: {node.GetType().Name}"))
    .UseSemanticAnalysis()
    .UseLogging((ctx, node) => 
    {
        var type = ctx.GetResolvedType(node);
        if (type != null)
            Console.WriteLine($"  Resolved type: {type.Name}");
    })
    .UseLinqExpression()
    .Build();

var expression = interpreter.Interpret(dataModelAst);
```

#### Example 3: Custom Middleware

```csharp
public class ConstantFoldingMiddleware : ITransformMiddleware<Expression>
{
    public Expression Transform(InterpretationContext context, Node node, TransformationDelegate<Expression> next)
    {
        // If both operands of Add are constants, fold them
        if (node is Add add && 
            add.LeftHandValue is Constant<int> left &&
            add.RightHandValue is Constant<int> right)
        {
            return Expression.Constant(left.Value + right.Value);
        }
        
        return next(context, node);
    }
}

var interpreter = new InterpreterBuilder<Expression>()
    .UseTypeProvider(typeProvider)
    .UseSemanticAnalysis()
    .Use(new ConstantFoldingMiddleware()) // Optimization pass
    .UseLinqExpression()
    .Build();
```

## Implementation Order

1. **Core delegates and interfaces** (Phase 1.1-1.2)
   - `TransformationDelegate<TResult>`
   - `ITransformationMiddleware<TResult>`

2. **Context enhancements** (Phase 1.3-1.4)
/// <summary>
/// Terminal middleware that delegates to an injected ITransformer (e.g., LINQ expression transformer).
/// Custom transformers (registry) can short-circuit before reaching this.
/// </summary>
public class LinqExpressionMiddleware : ITransformationMiddleware<Expression>
{
    private readonly ITransformer<Expression> _transformer;

    public LinqExpressionMiddleware(ITransformer<Expression> transformer)
    {
        _transformer = transformer;
    }

    public Expression Transform(InterpretationContext context, Node node, TransformationDelegate<Expression> next)
    {
        // If a custom transformer handled it, we never get called. Otherwise, produce the expression here.
        return _transformer.Transform(node);
    }
}

## Benefits

- **Separation of Concerns**: Nodes are pure data; middleware handles logic
- **Composability**: Pipeline stages are independent and reusable
- **Testability**: Each middleware can be tested in isolation
- **Extensibility**: New transformation passes can be added without modifying nodes
- **Performance**: Single-pass processing with efficient context caching
- **Flexibility**: Custom transformers for domain-specific scenarios

---

# Phase 4: Restoration - Recover Pre-Refactor Functionality

## Goal
Restore all functionality that existed before the Node refactor while maintaining the new middleware architecture. Ensure all valid test cases pass.

## Current State Assessment

### ✅ Infrastructure Complete
- Node hierarchy converted to records
- Middleware pipeline operational
- Compatibility layer in place (BuildNode, ToParameterExpression)
- All projects compile successfully

### ❌ Missing Functionality
The following components need implementation to restore pre-refactor behavior:

## 4.1 Semantic Analysis Implementation

**Status:** Middleware exists but has placeholder logic

**File:** `Poly/Interpretation/TransformationPipeline/SemanticAnalysisMiddleware.cs`

**Required Implementation:**

### Type Resolution for All Node Types

```csharp
public class SemanticAnalysisMiddleware<TResult> : ITransformationMiddleware<TResult>
{
    public TResult Transform(InterpretationContext context, Node node, TransformationDelegate<TResult> next)
    {
        // Skip if already analyzed
        if (context.HasSemanticInfo(node))
        {
            return next(context, node);
        }

        // Resolve type based on node type
        ITypeDefinition? resolvedType = node switch
        {
            // Literals
            Constant<int> => context.TypeProvider.GetTypeDefinition(typeof(int)),
            Constant<double> => context.TypeProvider.GetTypeDefinition(typeof(double)),
            Constant<string> => context.TypeProvider.GetTypeDefinition(typeof(string)),
            Constant<bool> => context.TypeProvider.GetTypeDefinition(typeof(bool)),
            // Add all other constant types...
            
            // Parameters - already have type
            Parameter p => p.Type,
            
            // Variables - lookup from context
            Variable v => context.Variables.TryGetValue(v.Name, out var varType) ? varType : null,
            
            // Arithmetic - use numeric promotion
            Add add => ResolveArithmeticType(context, add.LeftHandValue, add.RightHandValue),
            Subtract sub => ResolveArithmeticType(context, sub.LeftHandValue, sub.RightHandValue),
            Multiply mul => ResolveArithmeticType(context, mul.LeftHandValue, mul.RightHandValue),
            Divide div => ResolveArithmeticType(context, div.LeftHandValue, div.RightHandValue),
            Modulo mod => ResolveArithmeticType(context, mod.LeftHandValue, mod.RightHandValue),
            UnaryMinus neg => ResolveType(context, neg.Operand),
            
            // Boolean operators - always bool
            And => context.TypeProvider.GetTypeDefinition(typeof(bool)),
            Or => context.TypeProvider.GetTypeDefinition(typeof(bool)),
            Not => context.TypeProvider.GetTypeDefinition(typeof(bool)),
            
            // Comparison operators - always bool
            Equal => context.TypeProvider.GetTypeDefinition(typeof(bool)),
            NotEqual => context.TypeProvider.GetTypeDefinition(typeof(bool)),
            LessThan => context.TypeProvider.GetTypeDefinition(typeof(bool)),
            LessThanOrEqual => context.TypeProvider.GetTypeDefinition(typeof(bool)),
            GreaterThan => context.TypeProvider.GetTypeDefinition(typeof(bool)),
            GreaterThanOrEqual => context.TypeProvider.GetTypeDefinition(typeof(bool)),
            
            // Member access - resolve from instance type
            MemberAccess ma => ResolveMemberAccessType(context, ma),
            
            // Method invocation - resolve from method signature
            MethodInvocation mi => ResolveMethodInvocationType(context, mi),
            
            // Index access - resolve from indexer return type
            IndexAccess ia => ResolveIndexAccessType(context, ia),
            
            // Type cast - target type
            TypeCast tc => tc.TargetType,
            
            // Conditional - common type of branches
            Conditional cond => ResolveConditionalType(context, cond),
            
            // Coalesce - common type of operands
            Coalesce coal => ResolveCoalesceType(context, coal),
            
            // Block - type of last expression
            Block block => block.Expressions.Any() ? ResolveType(context, block.Expressions.Last()) : null,
            
            // Assignment - type of target
            Assignment assign => ResolveType(context, assign.Destination),
            
            _ => null
        };

        if (resolvedType != null)
        {
            context.SetResolvedType(node, resolvedType);
        }

        return next(context, node);
    }

    private ITypeDefinition? ResolveType(InterpretationContext context, Node node)
    {
        // Recursively resolve or get cached
        return context.GetResolvedType(node) ?? 
               (Transform(context, node, (ctx, n) => default!) as ITypeDefinition);
    }

    private ITypeDefinition? ResolveArithmeticType(InterpretationContext context, Node left, Node right)
    {
        var leftType = ResolveType(context, left);
        var rightType = ResolveType(context, right);
        return NumericTypePromotion.GetPromotedType(context, leftType, rightType);
    }

    private ITypeDefinition? ResolveMemberAccessType(InterpretationContext context, MemberAccess memberAccess)
    {
        var instanceType = ResolveType(context, memberAccess.Value);
        if (instanceType == null) return null;

        var member = instanceType.Members.FirstOrDefault(m => m.Name == memberAccess.MemberName);
        if (member != null)
        {
            context.SetResolvedMember(memberAccess, member);
            return member.MemberTypeDefinition;
        }
        return null;
    }

    private ITypeDefinition? ResolveMethodInvocationType(InterpretationContext context, MethodInvocation invocation)
    {
        var targetType = ResolveType(context, invocation.Target);
        if (targetType == null) return null;

        var argumentTypes = invocation.Arguments
            .Select(arg => ResolveType(context, arg))
            .Where(t => t != null)
            .ToList();

        var methods = targetType.FindMatchingMethodOverloads(invocation.MethodName, argumentTypes!);
        var method = methods.FirstOrDefault();

        if (method != null)
        {
            context.SetResolvedMember(invocation, method);
            return method.MemberTypeDefinition;
        }
        return null;
    }

    private ITypeDefinition? ResolveIndexAccessType(InterpretationContext context, IndexAccess indexAccess)
    {
        var instanceType = ResolveType(context, indexAccess.Value);
        if (instanceType == null) return null;

        var argumentTypes = indexAccess.Arguments
            .Select(arg => ResolveType(context, arg))
            .Where(t => t != null)
            .ToList();

        // Find indexer member (typically named "Item")
        var indexers = instanceType.Members
            .Where(m => m.Name == "Item")
            .WithParameterTypes(argumentTypes!)
            .ToList();

        var indexer = indexers.FirstOrDefault();
        if (indexer != null)
        {
            context.SetResolvedMember(indexAccess, indexer);
            return indexer.MemberTypeDefinition;
        }

        // Fallback for arrays
        if (instanceType.ReflectedType?.IsArray == true)
        {
            return context.TypeProvider.GetTypeDefinition(instanceType.ReflectedType.GetElementType()!);
        }

        return null;
    }

    private ITypeDefinition? ResolveConditionalType(InterpretationContext context, Conditional conditional)
    {
        var trueType = ResolveType(context, conditional.IfTrue);
        var falseType = ResolveType(context, conditional.IfFalse);
        
        // Return common type - for now, just return trueType
        // TODO: Implement proper common type resolution
        return trueType ?? falseType;
    }

    private ITypeDefinition? ResolveCoalesceType(InterpretationContext context, Coalesce coalesce)
    {
        var leftType = ResolveType(context, coalesce.LeftHandValue);
        var rightType = ResolveType(context, coalesce.RightHandValue);
        
        // Coalesce returns non-nullable version of left type or right type
        return leftType ?? rightType;
    }
}
```

**Tasks:**
- [ ] Implement type resolution for all Constant<T> types
- [ ] Implement arithmetic type promotion (integrate NumericTypePromotion)
- [ ] Implement member access resolution
- [ ] Implement method invocation resolution with overload matching
- [ ] Implement indexer resolution
- [ ] Implement conditional and coalesce type resolution
- [ ] Handle generic types and nullable types
- [ ] Add proper error handling and diagnostics

## 4.2 LinqExpressionTransformer Enhancement

**Status:** Basic implementation exists but doesn't use semantic analysis

**File:** `Poly/Interpretation/TransformationPipeline/LinqExpressionTransformer.cs`

**Required Changes:**

### Use Semantic Info for Type Conversions

```csharp
public Expression Transform(Add add)
{
    var left = Transform(add.LeftHandValue);
    var right = Transform(add.RightHandValue);
    
    // Get pre-resolved types from semantic analysis
    var leftType = _context?.GetResolvedType(add.LeftHandValue);
    var rightType = _context?.GetResolvedType(add.RightHandValue);
    
    // Apply numeric promotion if types differ
    if (leftType != null && rightType != null && leftType != rightType)
    {
        var (convertedLeft, convertedRight) = NumericTypePromotion.ConvertToPromotedType(
            _context, left, right, leftType, rightType);
        return Expression.Add(convertedLeft, convertedRight);
    }
    
    return Expression.Add(left, right);
}
```

### Store InterpretationContext Reference

```csharp
public class LinqExpressionTransformer : ITransformer<Expression>
{
    private readonly Dictionary<string, ParameterExpression> _variables = new();
    private InterpretationContext? _context;

    public static LinqExpressionTransformer Shared { get; } = new();

    public void SetContext(InterpretationContext context)
    {
        _context = context;
    }
    
    // ... rest of implementation
}
```

**Tasks:**
- [ ] Add context reference to transformer
- [ ] Use semantic info for arithmetic operations
- [ ] Use resolved member info for MemberAccess
- [ ] Use resolved method info for MethodInvocation
- [ ] Use resolved indexer info for IndexAccess
- [ ] Handle type conversions based on semantic analysis
- [ ] Properly handle Parameter expression caching

## 4.3 NumericTypePromotion Integration

**Status:** Exists but not integrated with middleware

**File:** `Poly/Interpretation/NumericTypePromotion.cs`

**Required:**
- [ ] Update to work with semantic analysis cached types
- [ ] Ensure promotion rules are applied in LinqExpressionTransformer
- [ ] Add tests for all promotion scenarios

## 4.4 Test Suite Validation

**Status:** Tests compile but may not pass

**Required Actions:**

### Run Test Suite
```bash
cd Poly.Tests
dotnet test --verbosity normal
```

### Expected Test Categories
- [ ] Arithmetic operations with type promotion
- [ ] Boolean operations
- [ ] Comparison operations
- [ ] Member access
- [ ] Method invocation
- [ ] Index access
- [ ] Conditional expressions
- [ ] Coalesce operations
- [ ] Block scoping
- [ ] Variable assignment
- [ ] Type casting
- [ ] Parameter handling

### Fix Failing Tests
For each failing test:
1. Identify root cause (semantic analysis, type resolution, or code generation)
2. Fix the underlying issue in middleware or transformer
3. Verify test passes
4. Document any behavior changes

## 4.5 Validation System Integration

**Status:** Validation system uses BuildNode compatibility layer

**Files:**
- `Poly/Validation/RuleSet.cs`
- `Poly/DataModeling/Validator.cs`

**Required:**
- [ ] Test validation scenarios work correctly
- [ ] Consider migrating to direct middleware usage
- [ ] Ensure DataModel validation still functions

## 4.6 Remove Compatibility Layer (Future)

**Status:** Compatibility extensions marked obsolete

**When to Remove:**
After all functionality is restored and tests pass, remove:
- [ ] `BuildNode(Node, InterpretationContext)` extension
- [ ] `GetTypeDefinition(Node, InterpretationContext)` extension
- [ ] `InterpretationContext.Transformer` property
- [ ] `InterpretationContext.GetParameterNodes()` method

**Migration Path:**
All code should use:
- Middleware pipeline via `InterpreterBuilder`
- `ToParameterExpression()` for Parameter conversion
- Semantic analysis for type information

## Implementation Priority

### Phase 4A: Core Functionality (Critical) 🔴
1. Complete SemanticAnalysisMiddleware implementation
2. Enhance LinqExpressionTransformer to use semantic info
3. Run and fix all arithmetic/boolean/comparison tests

### Phase 4B: Advanced Features (Important) 🟡
4. Member access and method invocation resolution
5. Index access and type casting
6. Block scoping and variable handling

### Phase 4C: Integration & Cleanup (Nice to Have) 🟢
7. Validation system integration
8. Complete test suite pass
9. Remove compatibility layer
10. Performance optimization

## Success Criteria

- ✅ All pre-refactor test cases pass
- ✅ No functionality lost from original implementation
- ✅ Semantic analysis correctly resolves all types
- ✅ LinqExpressionTransformer generates correct expressions
- ✅ Validation system works end-to-end
- ✅ Performance is comparable or better than original
- ✅ Code is cleaner and more maintainable than before

## Notes

- The middleware architecture provides better separation of concerns than the original
- Type resolution is now centralized in semantic analysis instead of scattered across node classes
- The compatibility layer allows gradual migration while maintaining functionality
- Tests serve as regression suite to ensure no functionality is lost
