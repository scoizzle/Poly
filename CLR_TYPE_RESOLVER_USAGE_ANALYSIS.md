# CLR Type Resolver - Usage Pattern Analysis

## Current Usage Patterns

### Pattern 1: Type Classification/Interrogation
**Goal**: Determine what kind of type something is (nullable? array? specific numeric type?)
**Current approach**: Resolve to CLR Type, then use reflection (Nullable.GetUnderlyingType, Type.IsArray, Type == typeof(int), etc.)
**Files**: NumericTypePromotion.cs, IndexAccess.cs

```csharp
var clrType = InterpretationContext.ClrTypeResolver.ResolveClrType(leftType);
var underlying = Nullable.GetUnderlyingType(clrType) ?? clrType;
if (underlying == typeof(decimal)) { /* ... */ }
```

### Pattern 2: Creating Lambda Parameters
**Goal**: Create ParameterExpression with correct CLR type
**Current approach**: Resolve ITypeDefinition → Type, then Expression.Parameter(clrType, name)
**Files**: Parameter.cs

```csharp
var clrType = ClrTypeResolver.Instance.TryResolveClrType(type) 
    ?? throw new InvalidOperationException(...);
return Expression.Parameter(clrType, name);
```

### Pattern 3: Type Conversion/Casting
**Goal**: Determine if conversion needed, create Expression.Convert()
**Current approach**: Resolve both types to CLR, compare, convert if different
**Files**: NumericTypePromotion.cs, TypeCast.cs, DataModelPropertyAccessor.cs

```csharp
var promotedClrType = InterpretationContext.ClrTypeResolver.ResolveClrType(promotedType);
var convertedLeft = leftExpr.Type == promotedClrType
    ? leftExpr
    : Expression.Convert(leftExpr, promotedClrType);
```

### Pattern 4: Reflection-based Element Access
**Goal**: Get array element types, handle array indexing
**Current approach**: Check if Type.IsArray, then Type.GetElementType()
**Files**: IndexAccess.cs

```csharp
var reflected = InterpretationContext.ClrTypeResolver.ResolveClrType(valueType);
if (reflected.IsArray) {
    var elementType = reflected.GetElementType()!;
    return context.GetTypeDefinition(elementType) /* ... */;
}
```

## What These Patterns Really Need

**Not**: "Give me the CLR Type" (which requires all downstream code know about .NET reflection)

**But**: A "Type System" abstraction that provides:

1. **Type Characteristics** - Is it nullable? Array? Numeric? What kind?
2. **Type Operations** - Can I convert between these? What's the promoted type?
3. **Type Navigation** - What's the element type? Underlying type?
4. **Type Construction** - How do I build an expression for this type?

## Proposed Abstraction: ITypeSystem

```csharp
/// <summary>
/// Provides type system operations for a specific target platform/runtime.
/// This abstracts away platform-specific concepts (CLR reflection, WASM type tables, etc.)
/// and exposes them as semantic operations the AST needs.
/// </summary>
public interface ITypeSystem
{
    // Interrogation
    bool IsNullable(ITypeDefinition type);
    bool IsArray(ITypeDefinition type);
    bool IsNumeric(ITypeDefinition type);
    ITypeDefinition? GetUnderlyingType(ITypeDefinition type);
    ITypeDefinition? GetElementType(ITypeDefinition type);
    
    // Numeric operations
    ITypeDefinition GetNumericPromotedType(ITypeDefinition left, ITypeDefinition right);
    
    // Comparison
    bool CanConvertImplicitly(ITypeDefinition from, ITypeDefinition to);
    bool CanConvertExplicitly(ITypeDefinition from, ITypeDefinition to);
    
    // For LINQ-specific operations (only needed by LinqExpressionInterpreter)
    Type GetClrType(ITypeDefinition type);
}
```

This way:
- ✅ Core AST logic (NumericTypePromotion, etc.) uses semantic operations
- ✅ Only LinqExpressionInterpreter knows about CLR reflection
- ✅ TreeWalkEvaluator can have a different implementation (no CLR dependency)
- ✅ Easy to support other runtimes (WASM, JavaScript, etc.)
- ✅ Better testability - mock ITypeSystem instead of CLR reflection
