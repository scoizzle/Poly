# Poly Introspection

`Poly.Introspection` defines the provider-based type abstraction used by analysis and code generation.

**Platform map:** [`docs/CORE.md`](../../docs/CORE.md) §3.5 — goal, providers, CLR as first host, what not to reinvent.

## Purpose

**Strategic goal:** Let **Interpretation simulate programs against any reasonable type system on any reasonable platform.** The core contracts are host-neutral; a platform plugs in as an `ITypeSystem` with the `ITypeSource`s it holds. The CLR under `CommonLanguageRuntime/` is the first working host, not the end state.

Instead of coupling analysis and backends to one runtime’s reflection API, Poly resolves types through interfaces:

- `ITypeDefinition`
- `ITypeMember`
- `ITypeSystem` and `ITypeSource`

This allows composition of multiple type sources while keeping consumers (for example analyzers) provider-agnostic and portable as new hosts appear.

## Core Interfaces

### ITypeDefinition

Represents a type and its members.

Its ten members (#998 T11) state structure only: `System` (the type system that made it), `Name`, `Namespace`,
`FullName`, `BaseType`, `Interfaces`, `GenericParameters`, `Members`, `Operators` (`ITypeOperator`) and
`Conversions` (`ITypeConversion`). Typed member views (`Fields`, `Properties`, `Methods`, `GetMembers(name)`) are
extensions. What a type is, and every rule between types (identity, nullability, element type, assignability,
conversion, operator selection), is its type system's answer, never the definition's (see below). The CLR's runtime
`Type` is the CLR host's own fact (`IClrTypeDefinition`), not part of this contract.

### ITypeMember

Represents a field, property, indexer, method, or constructor as facts: `Kind` (which shape it declares),
`Name`, `MemberTypeDefinition`, `DeclaringTypeDefinition`, `Parameters`, `AccessModifier`,
`LifetimeModifier`, `Mutability`, `IsStatic`. A representation fact belongs to the host that stores the
member, never to this contract: the ABI owns how a field or property is read and written, and the
declaration states only what the member is.

### ITypeOperator

Represents an operator a type defines: its `OperatorKind`, `DeclaringTypeDefinition`, `Parameters` (the operands)
and `ResultTypeDefinition`, plus `Method`, the member that computes it, or null when the platform evaluates it itself.
Like `ITypeMethod`, it is implemented per platform (`ClrTypeOperator`, `AstOperatorDefinition`); nothing in the
Introspection root says how a platform spells an operator.

- `Name`
- `MemberTypeDefinition`
- `DeclaringTypeDefinition`
- `Parameters`
- `IsStatic`

### ITypeConversion

Represents a conversion a platform states: its `Kind` (`Implicit`/`Explicit`), the `SourceTypeDefinition` and
`ResultTypeDefinition` it converts between, and `Method`, the member that computes it, or null when the platform
applies it itself. Implemented per platform (`ClrTypeConversion`, `AstConversionDefinition`); like `ITypeOperator`,
nothing in the Root-adjacent contract says how a platform spells one.

### ITypeSystem and ITypeSource

A type system (`ITypeSystem`, #998) holds what a set of definitions is: its `Sources` (`ITypeSource`: the definitions a
source declares in a namespace, `TypesIn`/`TypeIn`), `Resolve(qualifiedName)`, its words (`Bind`/`Spell`) and its rules;
a layered system names its `Target`. An analysis decides by its own layer (`Poly.Analysis.AnalysisTypeSystem`): its own
sources answer a name first, then its target. `ITypeDefinitionProvider` and the fallback stack
`TypeDefinitionProviderCollection` are gone (T12b). The runtime-`Type` handle is not on any contract: the CLR registry
answers it internally, and a consumer reaches it through the internal `ClrDefinitionOf`, which walks the system and its
target chain and answers null for a system with no CLR beneath it. `TypeDefinitionRuntimeTypeExtensions` (the
`ITypeDefinition -> Type?` view) lives in `CommonLanguageRuntime/`.

**What a type is, is its type system's rule (#998 T8).** `ITypeDefinition` states no classification: the answers
(`IsEnumeration` … `IsTopType`), `IsGround()` and the CLR's answer table are gone. A consumer asks the system that owns
the question: C#'s rule (`Poly.Interpretation.CSharp.ICSharpTypeRules`) and its words, the CLR host's own table
(`ClrBuiltInRules.IsPrimitive`, `IsCalendarType`, `IsTimestamp`, `IsGuid`, `IsByteArray`), a foreign host's own rule
(the test-only `LedgerHost.ILedgerRules`), the domain's words and declarations. There is no shared list of kinds
(`TypeKind` is gone), no shared list of primitive types (`PrimitiveType`, `PrimitiveAtom` and the
`PrimitiveTypeReference` node are gone), and no fact lookup (`GetFact`, `TypeFact` are gone).

The CLR's definitions live in `CommonLanguageRuntime/` (`ClrTypeDefinition`, from the runtime type). A question only one
host can answer goes on that host's own rule or interface, never on the neutral contract (`IClrTypeDefinition`). The
CLR host keeps identity facts only (`ClrBuiltInRules.IsPrimitive` and its siblings); C#'s built-in rules are C#'s
(`Poly.Interpretation.CSharp/CSharpBuiltIns`, keyed on `SimpleType`; `ClrPrimitive` is gone, #998 T10b); C#'s keywords are the C# surface's names for CLR definitions (`Poly.Interpretation.CSharp/CSharpTypeNames`). A
type position in a tree is a definition reference (`TypeDefinitionReference`), which the writer spells as its language
does.

The target type system decides a mixed operator or conversion pair, whichever system each operand comes from (#998 T3);
`MixedPairAuthorityTests` pins that a foreign definition's own answer never decides.

## Conversions and operators

A conversion or an operator is a type system's rule (#998 T9b): `ITypeSystem.Conversion(source, destination)`,
`Operator(kind, left, right)` and `IsAssignableWithoutConversion(destination, source)`. The consumer doors take the
system they ask: `GetOperator(kind, right, system)` and `GetCommonType(system)`
(`TypeDefinitionOperatorExtensions`), and `IsAssignableFrom(other, system)` (`TypeDefinitionExtensions`). A
definition only lists what it declares (`Operators`, `Conversions`, each naming both ends and the method that
computes it, or none when the platform applies it). The target system decides a mixed pair, whichever system each
operand comes from (#998 T3, `MixedPairAuthorityTests`).

C# is the rule set over CLR definitions: `Poly/Interpretation/CSharp/CSharpBuiltIns` holds every conversion and
operator C# defines for its built-in types, and `CommonLanguageRuntime/ClrOperatorResolution` is C#'s selection
(12.4.4, 12.4.5). Modeled definitions declare an operator or a conversion with a static method marked
`MethodDefinitionNode.Operator` or `.Conversion`; the method's name plays no part. The section that stood here before
#998 described these as internal definition members (`ResolveConversionFrom`, `ResolveOperator`); it is kept in
[`docs/reference/2026-11-20-type-machinery-history.md`](../../docs/reference/2026-11-20-type-machinery-history.md).

## CLR Provider

`CommonLanguageRuntime/ClrTypeDefinitionRegistry` is the built-in CLR implementation.

Highlights:

- Shared singleton: `ClrTypeDefinitionRegistry.Shared`
- Thread-safe caching with `ConcurrentDictionary`
- Name and runtime-type resolution
- Deferred resolver support

## Minimal usage

```csharp
using Poly.Introspection.CommonLanguageRuntime;

var stringType = ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(string));
var intType = ClrTypeDefinitionRegistry.Shared.GetTypeDefinition("System.Int32"); // qualified names only
```

An analysis decides by a type system (`AnalysisSettings.Default.With<ITypeSystem>(system)`); C# over the CLR is the
default. A second platform implements `ITypeSystem` and its `ITypeSource`s: `Poly.Tests/TestHelpers/LedgerHost.cs`
is a complete one, with its own definitions, operator and rule.
