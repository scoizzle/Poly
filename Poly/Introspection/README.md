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
- `IsEnumeration`, `HasValueSemantics`, `IsNumber`, `NumberBits` and their siblings (what the type is: see below)
- `Members`
- `Operators` (`ITypeOperator`, see below)
- `GetMembers(string name)`
- `IsAssignableFrom` (no conversion, or a declared implicit operator; a conversion the platform applies itself, such as numeric widening, is not assignment)
- `Conversions` (internal: the conversions a definition states — the operators it declares on either end, plus the ones the platform applies with it as the source) and `ResolveConversionFrom` / `ResolveOperator` (internal, every platform implements them): what a platform declares and defines for conversions and for operator selection; `IsAssignableWithoutConversion` is the internal default-bodied first half of assignability. See below. Conversion operators are not in `Methods`; each item is an `ITypeConversion` naming both ends, with the declared method or none when the platform applies it.

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

## Conversions and operators (T-03b, T-24)

A definition answers what its platform defines. Conversions answer through the internal `ResolveConversionFrom(source)`
(a `TypeConversion`: the kind, and the declared method or null when the platform applies it itself). Operators are
`ITypeDefinition.Operators` (`ITypeOperator`), and the platform selects one through the internal, abstract
`ResolveOperator(kind, right)` (#942): which operators are candidates and how one is chosen is the platform's rule, not
the root's. `GetOperator(kind, right)` (`TypeDefinitionOperatorExtensions`) is the consumer door: it checks the arity
and delegates. `GetCommonType(types)` is the unique type the others identically or implicitly convert to. Optionality is
the caller's (T-06). Shape and nullability are the type system's rules (#998 T9c): `ITypeSystem.ElementType` merges the collection
shape and the sequence element (C#'s from the CLR's representation or a modeled collection), and
`ITypeSystem.NullableOf` names what a nullable form wraps; `GetElementType` asks the system it is given.

- **.NET rules** live in `CommonLanguageRuntime/`. `ClrOperatorResolution` is C#'s selection (12.4.4, 12.4.5):
  candidates are the operators either operand offers beside the other (a CLR definition's are pair-aware,
  `ClrTypeDefinition.OperatorsBeside`, since C#'s `UInt32 + Int32` is `Int64`, which neither operand declares; any other
  definition's are its `Operators` of the kind); a candidate applies when each operand is identical to its parameter or
  converts implicitly; all-identity wins, otherwise exactly one applicable candidate, otherwise none.
  `ClrOperatorNames` is the CLR's special-name encoding (`op_Addition`, …). `ClrTypeDefinition` reflects
  `op_Implicit`/`op_Explicit` and static special-name `op_*` members (`ClrTypeOperator` with a `Method`), then `ClrBuiltInRules` supplies what C# leaves to the compiler: every
  conversion and operator C# defines for built-in types (the implicit and explicit numeric conversions,
  conversion into `object` from any other CLR type, numeric promotion through one `Promote` function, arithmetic,
  modulo, comparison, bitwise, shift, unary minus and plus, the Boolean operators, and string concatenation with
  any CLR operand). Decimal's conversions and operators are the CLR's declared members. `nint` and `nuint` are
  built-in numeric types as C# 11 defines them (T-18b): their IntPtr/UIntPtr `op_*` members stand down, an implicit
  built-in conversion answers before a declared one for a native pair (`ClrBuiltInRules.IsNativeInteger`), and their
  conversion to and from decimal is built in. Reference and delegate equality are C# 12.12.7's and 12.12.9's (T-22a),
  and a shift count of any built-in integer type is Poly's permanent policy (`IsPolicyShiftCount`). Deliberately none:
  enum and lifted operators. `Method` is null for every operator of the
  simple types except decimal's, and for concatenation. `CSharpBuiltIns` (C#'s system, T10) is the only source of the built-in answers. Overload scoring reads the provider since T-16: tier 0 is the same type (`IsSameType`), tier 1 is `IsAssignableFrom`, tier 2 is an implicit `ResolveConversionFrom` (a conversion with no method, which the platform applies itself; since T-21c that includes C# 10.6.1's lifted conversion into a `Nullable<U>` from `S` or `S?`, stated with no method even where `S` to `U` declares one, so it is never assignability); the private numeric rank table and the public `GetConversionFrom` door are gone. `IsAssignableFrom` stays "no conversion, or a declared implicit operator" because assignment, casts, type tests and the emitter also read it; widening it would loosen all of them. Nullability is the type system's relation (#998 T9c): `ITypeSystem.NullableOf` names the type a nullable form wraps (C#'s answers `Nullable<T>` for the CLR; a modeled definition wraps nothing, where optionality is the caller's, T-06), so the lift into a nullable destination is that system's answer. The analysis keeps no CLR type table of its own: implicit conversion, integer-ness, IEEE-ness and the type category
(Text/Number/Boolean/Date/Enum/Guid) are asked of the platform (`IsAssignableWithoutConversion`, `ResolveConversionFrom`, `IsNumber` and its siblings), and the CLR-only `Convert.To*`
names are `ClrConvertMethods` in this namespace. An architecture test (`ArchitectureBoundaryTests`) fails if the
meaning layers take a `System.Linq.Expressions` or `System.Reflection` using, and holds CLR type facts to a shrinking
per-file budget. Where the analysis builds a type reference it states the definition (`TypeDefinitionReference`), so
`System.Type` does not enter the tree from the meaning layers; `ClrTypeReference` remains the AST's source-level
reference until its own row removes it, and the factory that builds one is the host's internal shorthand (`ClrTypeReferences.To<T>()`), not
the AST's: `Poly/Ast` names no `typeof`. `IsAssignableWithoutConversion` is its first half: the same type, a base type or an interface, and for .NET `Type.IsAssignableFrom`, the lift from `T` to `T?` and `object` accepting every definition (the VM boxes a modeled entity). The box into `object` is also `ClrBuiltInRules`' implicit conversion, so it is stated twice; the two agree on every CLR definition (`ObjectAcceptsEveryClrDefinition_IsAssignableFromAndResolveConversionFromAgree`) and differ for a modeled source by design. Still outside the contract, with no removing row yet: the tier sum itself (a private copy of overload resolution that keeps the first of equal candidates; it is not C# 12.6.4 better-conversion), the boxing rule stated twice, and `SyntaxTypeCompatibilityAnalyzer`'s `IsImplicitlyConvertible(Type, Type)` with its nullable lifting, `CoerceTo`, `IsIntegerType`, `ConvertMethodName` and `IsIeeeFloating`. T-23 publishes `IsAssignableWithoutConversion`. The collection shape is the type system's `ElementType` (T9c), and the root names no CLR collection. Type identity is the type system's (I1, #998 T9a): a definition names the system that made it
(`ITypeDefinition.System`), and `TypeDefinitionExtensions.IsSameType` is "the same system, and its `IsSame` says so"
(for C#: a CLR definition is its instance, a modeled collection is its kind and element, a generic parameter is its name
on its declaring type).
- **Modeled definitions** (`AstTypeDefinition`) declare an operator with a static method marked
  `MethodDefinitionNode.Operator`; the method's name plays no part. The C# reader marks a static member named like a
  CLR operator, the CLR's own encoding, since the C# surface has no `operator` declaration yet (#406); the domain
  provider marks the equality it synthesizes on enums, entities and value types. They select with C#'s rule
  (`ClrOperatorResolution`). A modeled definition declares a conversion with a static method marked
  `MethodDefinitionNode.Conversion`; the C# reader marks a static `op_Implicit`/`op_Explicit` member
  (`ClrConversionNames`), the CLR's own encoding, until the C# surface reads `implicit operator`. The AST definitions
  select through `ModeledConversions`; the root names no conversion spelling. `ITypeConversion` (`Kind`,
  `SourceTypeDefinition`, `ResultTypeDefinition`, and `Method`, null when the platform applies it) is listed by
  `ITypeDefinition.Conversions`, which states the operators the definition declares on either end — naming both ends
  is what lets a declaration whose source is another type appear — plus the platform's own answers with it as the
  source. A conversion the other end declares, what a later-defined type may add, and the lift into a nullable
  target (the caller's, T-06) are `ResolveConversionFrom`; reference conversions are assignability, and neither
  member lists them.
- **Introspection-internal today.** `ITypeOperator` and `Operators` are public, as `ITypeMethod` and `Methods` are;
  `ResolveConversionFrom`, `IsAssignableWithoutConversion` and `ResolveOperator` are `internal`. A platform in another assembly cannot implement them
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
