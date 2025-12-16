# Poly Introspection System

A unified, extensible type abstraction layer that decouples type definitions from their underlying implementation sources (CLR reflection, data models, custom providers).

## Overview

The Introspection system provides a pluggable architecture for resolving type information at interpretation time. Rather than coupling directly to System.Reflection, it uses interfaces (`ITypeDefinition`, `ITypeMember`, `ITypeDefinitionProvider`) to abstract type sources:

- **Multiple backends**: CLR types, data models, custom types via provider plugins
- **Lazy resolution**: Deferred type definition creation to avoid upfront reflection costs
- **Caching**: Thread-safe caching of type definitions for performance
- **Composability**: Chain multiple providers with fallback semantics

## Core Concepts

### Type Definition (`ITypeDefinition`)
Represents a type with introspectable members and metadata.

**Properties:**
- `Name` - Simple name (e.g., "String")
- `Namespace` - Namespace qualification (e.g., "System")
- `FullName` - Computed: `Namespace.Name` or just `Name`
- `ReflectedType` - Backing CLR type for runtime type comparisons
- `Members` - All members (properties, fields, methods) on the type

**Methods:**
- `GetMembers(string name)` - Retrieve members by name
- `GetIndexers()` - Helper to find indexer members (named "Item")

### Type Member (`ITypeMember`)
Represents a single member (property, field, method) on a type.

**Properties:**
- `Name` - Member name
- `MemberTypeDefinition` - The type of this member
- `DeclaringTypeDefinition` - The type that declares this member
- `Parameters` - Parameters if member is a method

**Methods:**
- `GetMemberAccessor(Value instance, IEnumerable<Value> parameters)` - Create an interpretable `Value` for accessing this member

### Type Definition Provider (`ITypeDefinitionProvider`)
Interface for pluggable type sources.

**Methods:**
- `GetTypeDefinition(string name)` - Resolve type by full name
- `GetTypeDefinition(Type type)` - Resolve type by CLR type
- `GetDeferredTypeDefinitionResolver(string name)` - Return a lazy-initialized resolver to defer lookup

### Provider Collection (`TypeDefinitionProviderCollection`)
Composes multiple providers with fallback semantics:

```
Provider 1 (checked first)
    ↓ if null
Provider 2
    ↓ if null
Provider 3
    ↓ if null
null (not found)
```

Providers are stored in a stack; most recently added is checked first.

### CLR Type Definition Registry (`ClrTypeDefinitionRegistry`)
Built-in provider for System.Reflection types.

- **Thread-safe caching** via `ConcurrentDictionary`
- **Singleton**: `ClrTypeDefinitionRegistry.Shared`
- **Deferred creation**: Types are lazily constructed from CLR metadata
- **Manual registration**: `AddType()`, `RemoveType()` for custom type definitions

## Architecture

```
┌─────────────────────────────────┐
│  Interpretation System          │
│  (uses ITypeDefinition)         │
└────────────┬────────────────────┘
             │
┌────────────▼────────────────────┐
│ TypeDefinitionProviderCollection│
│  (composes multiple providers)  │
└────────────┬────────────────────┘
             │
    ┌────────┴────────┬──────────┬──────────┐
    │                 │          │          │
┌───▼──┐         ┌───▼──┐  ┌──▼──┐  ┌───▼──┐
│ CLR  │         │Data  │  │User │  │Other │
│Reg.  │         │Model │  │Type │  │      │
└───┬──┘         └───┬──┘  └──┬──┘  └───┬──┘
    │                │         │        │
    └────────────────┴─────────┴────────┘
     (all implement ITypeDefinitionProvider)
```

## Usage

### Basic Type Resolution

```csharp
var context = new InterpretationContext();

// Resolve by CLR type
var stringType = context.GetTypeDefinition<string>();

// Resolve by type
var intType = context.GetTypeDefinition(typeof(int));

// Resolve by full name
var customType = context.GetTypeDefinition("MyNamespace.MyClass");
```

### Member Introspection

```csharp
var stringType = context.GetTypeDefinition<string>();

// Find all members named "Length"
var lengthMembers = stringType.GetMembers("Length");

// Find all indexers
var indexers = stringType.GetIndexers();

// Access member metadata
foreach (var member in stringType.Members) {
    Console.WriteLine($"{member.Name}: {member.MemberTypeDefinition.Name}");
}
```

### Custom Type Provider

```csharp
public class MyCustomTypeProvider : ITypeDefinitionProvider {
    public ITypeDefinition? GetTypeDefinition(string name) {
        if (name == "MySpecialType") {
            return new MyCustomTypeDefinition();
        }
        return null;
    }
    
    public ITypeDefinition? GetTypeDefinition(Type type) {
        if (type.Name == "MySpecialType") {
            return new MyCustomTypeDefinition();
        }
        return null;
    }
}

// Register with context
var context = new InterpretationContext();
context.AddTypeDefinitionProvider(new MyCustomTypeProvider());

// Now custom types resolve alongside CLR types
var myType = context.GetTypeDefinition("MySpecialType");
```

### Lazy Type Resolution

```csharp
var provider = ClrTypeDefinitionRegistry.Shared;

// Deferred resolver (doesn't throw if type not found immediately)
var resolver = provider.GetDeferredTypeDefinitionResolver(typeof(int));

// Later: force resolution
var intType = resolver.Value;  // Throws if type wasn't found
```

## Current Features

✅ Multi-provider type resolution with fallback semantics  
✅ Thread-safe caching of CLR types  
✅ Member introspection (properties, fields, methods)  
✅ Parameter metadata for methods/indexers  
✅ Lazy evaluation of type definitions  
✅ Integration with Poly Expression system  
✅ Singleton global CLR registry  

## CLR Implementation Details

### ClrTypeDefinition
Wraps CLR `Type` with member reflection:

- **Fields** (`ClrTypeField`) - Expose field metadata
- **Properties** (`ClrTypeProperty`) - Include indexer parameters
- **Methods** (`ClrMethod`) - Full method signatures
- **Parameters** (`ClrParameter`) - Method and indexer parameters

### Caching Strategy

```csharp
// Per-type cache in CLR registry
private readonly ConcurrentDictionary<string, ClrTypeDefinition> _types;

// Per-member lazy initialization
private readonly Lazy<ClrTypeDefinition> _memberTypes;
```

**Benefits:**
- First access creates definition via reflection
- Subsequent accesses hit cache (O(1))
- Thread-safe for concurrent interpretation

## Future Plans

### High Priority

1. **Generic Type Support**
   - Handle `List<int>`, `Dictionary<K,V>` type parameters
   - Preserve type arguments through definition chain
   - Generic method instantiation

2. **Null-Safe Member Lookup**
   - `TryGetMember()` instead of `GetMembers()` to reduce null-coalescing
   - Prioritize by member type (properties > fields > methods)
   - Clear error messages for ambiguous lookups

3. **Provider Lifecycle Management**
   - `IDisposable` support for cleanup
   - Unregister providers cleanly
   - Clear cache scoped to provider

4. **Member Filtering**
   - `GetMembers(name, MemberType.Property | MemberType.Method)`
   - Skip private/internal members by default
   - Public/protected/internal options

5. **Generic Member Handling**
   - Support generic properties/methods with type argument binding
   - Recursive type parameter resolution

### Medium Priority

6. **Type Hierarchy Introspection**
   - `BaseType` / `Interfaces` properties
   - `IsAssignableTo()` method
   - Type compatibility checking

7. **Attribute Metadata**
   - `IAttributeProvider` to expose custom attributes
   - Use in validation/interpretation via attributes
   - Cache attribute lookups

8. **Static Member Support**
   - Flag static vs instance members
   - Support for static methods/properties in interpretation
   - Singleton type definitions

9. **Performance Optimization**
   - Member lookup by hash instead of linear scan
   - Parallel member collection building
   - Reflection metadata caching strategy

10. **Documentation Generator**
    - Generate API docs from type definitions
    - Export to OpenAPI/JSON Schema
    - Validation rule documentation

## Testing

Comprehensive tests in `Poly.Tests/Introspection/`:

- CLR type definition creation
- Member reflection and caching
- Provider fallback behavior
- Generic type edge cases
- Lazy resolution behavior
- Thread safety of registries

Run tests:
```bash
cd Poly.Tests
dotnet test
```

## Contributing

When adding provider types:

1. Implement `ITypeDefinitionProvider` interface
2. Implement `ITypeDefinition` for your type representation
3. Implement `ITypeMember` for your member types
4. Add `GetMemberAccessor()` to create interpretable `Value` instances
5. Ensure thread-safety if caching
6. Document provider-specific behavior
7. Add comprehensive tests

## References

- [System.Reflection Documentation](https://docs.microsoft.com/en-us/dotnet/api/system.reflection)
- [Poly Interpretation System](../Interpretation/README.md)
- [Poly Data Modeling](../DataModeling/README.md)
