# Poly Introspection

`Poly.Introspection` defines the provider-based type abstraction used by analysis and code generation.

**Platform map:** [`docs/CORE.md`](../../docs/CORE.md) §3.5 — goal, providers, CLR as first host, what not to reinvent.

## Purpose

**Strategic goal:** Let **Interpretation simulate programs against any reasonable type system on any reasonable platform.** The core contracts are host-neutral; platforms plug in as `ITypeDefinitionProvider` implementations. The CLR under `CommonLanguageRuntime/` is the first working host, not the end state.

Instead of coupling analysis and backends to one runtime’s reflection API, Poly resolves types through interfaces:

- `ITypeDefinition`
- `ITypeMember`
- `ITypeDefinitionProvider`

This allows composition of multiple type sources while keeping consumers (for example analyzers) provider-agnostic and portable as new hosts appear.

## Core Interfaces

### ITypeDefinition

Represents a type and its members.

Common members include:

- `Name`
- `Namespace`
- `FullName`
- `ReflectedType`
- `Members`
- `GetMembers(string name)`
- `IsAssignableFrom` / `GetConversionFrom` (conversion operators are not in `Methods`)

### ITypeMember

Represents a field, property, or method.

- `Name`
- `MemberTypeDefinition`
- `DeclaringTypeDefinition`
- `Parameters`
- `IsStatic`

### ITypeDefinitionProvider

Resolves type definitions:

- `GetTypeDefinition(string name)`
- `GetTypeDefinition(Type type)`
- `GetTypeDefinition(PrimitiveType primitiveTypeId)`
- `GetDeferredTypeDefinitionResolver(string name)`

## Provider Composition

Use `TypeDefinitionProviderCollection` to stack multiple providers. Resolution is LIFO:

1. Most recently added provider is queried first.
2. Falls through until a provider returns a match.
3. Returns `null` when no provider can resolve.

## CLR Provider

`CommonLanguageRuntime/ClrTypeDefinitionRegistry` is the built-in CLR implementation.

Highlights:

- Shared singleton: `ClrTypeDefinitionRegistry.Shared`
- Thread-safe caching with `ConcurrentDictionary`
- Name and runtime-type resolution
- Deferred resolver support

## Minimal Usage

```csharp
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

var providers = new TypeDefinitionProviderCollection(new[] {
    ClrTypeDefinitionRegistry.Shared
});

var stringType = providers.GetTypeDefinition(typeof(string));
var intType = providers.GetTypeDefinition("System.Int32");
```

## Custom Provider Example

```csharp
public sealed class CustomProvider : ITypeDefinitionProvider {
    public ITypeDefinition? GetTypeDefinition(string name) {
        return name == "MyType" ? new MyTypeDefinition() : null;
    }

    public ITypeDefinition? GetTypeDefinition(Type type) {
        return type.FullName == "MyNamespace.MyType" ? new MyTypeDefinition() : null;
    }
}
```

Register custom providers by adding them to `TypeDefinitionProviderCollection`.
