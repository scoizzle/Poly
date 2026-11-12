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
- `RuntimeType` (via `IClrTypeDefinition`)
- `Kind`
- `PrimitiveType`
- `Members`
- `GetMembers(string name)`
- `IsAssignableFrom` / `GetConversionFrom` (conversion operators are not in `Methods`)
- `ResolveConversionFrom` / `GetOperators` (internal, default-bodied: what the platform defines for conversions and operators; see below)

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

## Conversions and operators (T-03b)

A definition answers what its platform defines, through two internal members of `ITypeDefinition`:
`ResolveConversionFrom(source)` (a `TypeConversion`: the kind, and the declared method or null when the platform
applies it itself) and `GetOperators(kind)` (`DeclaredOperator`: a signature and whether the platform applies it).
`GetOperator(kind, right)` (`TypeDefinitionOperatorExtensions`) is the consumer door: candidates are the operators
either operand declares; a candidate applies when each operand is identical to its parameter or converts
implicitly; all-identity wins, otherwise exactly one applicable candidate, otherwise none. `GetCommonType(types)`
is the unique type the others identically or implicitly convert to. Optionality is the caller's (T-06).

- **.NET rules** live in `CommonLanguageRuntime/`: `ClrTypeDefinition` reflects `op_Implicit`/`op_Explicit` and
  static special-name `op_*` members, then `ClrBuiltInRules` supplies what C# leaves to the compiler (the closed
  {Int32, Int64, Float64} conversions, and the intrinsic operators of those, Boolean and String). A closed set
  answers none, not unknown. `ClrBuiltInRules` is the only source of the built-in answers; three older branches
  in this folder's root still name .NET classes and move in T-16.
- **Modeled definitions** answer from declared static `op_*` members: Poly's modeled convention, shared with
  `op_Implicit`, not a platform fact. `And` and `Or` have no declared form.
- **Introspection-internal today.** The members are `internal` and neutral at signature level only
  (`ITypeMember.EmitRead` still returns an `Expression`). A platform in another assembly cannot implement them
  yet; they go public, non-breaking, when the first non-CLR platform lands. The long-term goal is a VM and
  Introspection general enough to simulate almost any platform, with .NET the first.
- **The contract carries no representation fact.** It says what a platform's types do, never how the VM stores
  them; the tree states conversions and the ABI executes exactly those or rejects (CORE §3.1, row T-21).

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
