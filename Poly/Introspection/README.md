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
- `IsAssignableFrom` (no conversion, or a declared implicit operator; a conversion the platform applies itself, such as numeric widening, is not assignment)
- `ResolveConversionFrom` / `IsAssignableWithoutConversion` / `GetOperators` (internal, default-bodied: what the platform defines for conversions, assignability and operators; see below). Conversion operators are not in `Methods`; a declared one is a `TypeConversion` with a method, a built-in one has none.

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

## Conversions and operators (T-03b, T-24)

A definition answers what its platform defines, through two internal members of `ITypeDefinition`:
`ResolveConversionFrom(source)` (a `TypeConversion`: the kind, and the declared method or null when the platform
applies it itself) and `GetOperators(kind, other)` (`DeclaredOperator`: a signature and whether the platform applies
it). `other` is the other operand (null: the same type): a platform's answer depends on the pair, since C#'s
`UInt32 + Int32` is `Int64`, which neither operand owns.
`GetOperator(kind, right)` (`TypeDefinitionOperatorExtensions`) is the consumer door: candidates are the operators
either operand declares beside the other; a candidate applies when each operand is identical to its parameter or converts
implicitly; all-identity wins, otherwise exactly one applicable candidate, otherwise none. `GetCommonType(types)`
is the unique type the others identically or implicitly convert to. Optionality is the caller's (T-06).

- **.NET rules** live in `CommonLanguageRuntime/`: `ClrTypeDefinition` reflects `op_Implicit`/`op_Explicit` and
  static special-name `op_*` members, then `ClrBuiltInRules` supplies what C# leaves to the compiler: every
  conversion and operator C# defines for built-in types (the implicit and explicit numeric conversions,
  conversion into `object` from any other CLR type, numeric promotion through one `Promote` function, arithmetic,
  modulo, comparison, bitwise, shift, unary minus and plus, the Boolean operators, and string concatenation with
  any CLR operand). Decimal's conversions and operators are the CLR's declared members. `nint` and `nuint` are
  built-in numeric types as C# 11 defines them (T-18b): their IntPtr/UIntPtr `op_*` members stand down, an implicit
  built-in conversion answers before a declared one for a native pair (`ClrBuiltInRules.IsNativeInteger`), and their
  conversion to and from decimal is built in. Reference equality is C# 12.12.7's (T-22a), and a shift count of any
  built-in integer type is Poly's permanent policy (C# rejects, Poly defines). Deliberately none: enum,
  lifted and delegate operators. `PlatformApplied` is true for every operator of the
  simple types except decimal's, and for concatenation. `ClrBuiltInRules` is the only source of the built-in answers. Overload scoring reads the provider since T-16: tier 0 is the same type (`IsSameType`), tier 1 is `IsAssignableFrom`, tier 2 is an implicit `ResolveConversionFrom` (a conversion with no method, which the platform applies itself); the private numeric rank table and the public `GetConversionFrom` door are gone. `IsAssignableFrom` stays "no conversion, or a declared implicit operator" because assignment, casts, type tests and the emitter also read it; widening it would loosen all of them. `IsAssignableWithoutConversion` is its first half: the same type, a base type or an interface, and for .NET `Type.IsAssignableFrom`, the lift from `T` to `T?` and `object` accepting every definition (the VM boxes a modeled entity). The box into `object` is also `ClrBuiltInRules`' implicit conversion, so it is stated twice; the two agree on every CLR definition (`ObjectAcceptsEveryClrDefinition_IsAssignableFromAndResolveConversionFromAgree`) and differ for a modeled source by design. Still outside the contract, with no removing row yet: the tier sum itself (a private copy of overload resolution that keeps the first of equal candidates; it is not C# 12.6.4 better-conversion), the boxing rule stated twice, and `SyntaxTypeCompatibilityAnalyzer`'s `IsImplicitlyConvertible(Type, Type)` with its nullable lifting, `CoerceTo`, `IsIntegerType`, `ConvertMethodName` and `IsIeeeFloating`. T-23 publishes `IsAssignableWithoutConversion`. `IsCollectionShape` and `GetElementType` also name CLR types in the Introspection root but do not answer a conversion.
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
