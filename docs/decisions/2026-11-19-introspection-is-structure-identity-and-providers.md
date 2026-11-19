# Introspection is structure, identity and providers

Status: **Proposed, 2026-11-19. Waiting for the owner's ruling; no code follows until it rules.** Stream
`stream/introspection` (#998, draft PR #997), base `dfafe16b4`.

Owner, 2026-11-19: "we need a better abstraction. 43 members on the thing that *is* the definition of a type is
wrong." The recount at `dfafe16b4` is **42** (13 structural, 5 internal relations, 21 classification answers, 3
conversion/operator doors); an earlier message said 43.

## Why

`ITypeDefinition` at `16a62dcc8` (2026-02-01, the commit before `TypeCategory`) described a type's structure:
name, namespace, base type, interfaces, generic parameters, members, plus a CLR `ReflectedType`. It held no
classification. `TypeCategory` and `PrimitiveTypeId` arrived in `80b0d50ae` (2026-02-02) for one consumer:
validation, to decide which constraint applies to which property type. Since then that consumer's question has
stayed on the contract and only changed form: `[Flags] TypeCategory` → `TypeKind` + `PrimitiveType` → `GetFact`
facts → 21 booleans (`dfafe16b4`), which is `TypeCategory` again.

**Decision.** `ITypeDefinition` returns to structure, identity and providers, and keeps only what has proved its
keep since: identity (`IsSameTypeAs`), the platform's conversion and operator doors, nullability as a relation, and
the element type. **What kind of type something is, is not the definition's question.** Each consumer that
classifies owns its rule, keyed by definition identity. Sources and scopes (`ITypeSource`, `UnitTypeScope`) are
unchanged.

## 1. The proposed contract: 14 members

| # | Member | Why it is on the contract (asker today; every platform can answer) |
|---|---|---|
| 1 | `string Name` | Every diagnostic, printer and lookup names the type (83 references). |
| 2 | `string? Namespace` | Scope binding lists a source's namespaces (`AstTypeRegistry.TypesIn`, cast printing). |
| 3 | `string FullName` (default body) | Display and qualified lookup (56 references). Not identity: that is #8. |
| 4 | `ITypeDefinition? BaseType` | Hierarchy assignability (`IsAssignableByHierarchy`) and inherited members (`AstTypeDefinition`). |
| 5 | `IEnumerable<ITypeDefinition> Interfaces` | The same two askers. |
| 6 | `IEnumerable<IParameter> GenericParameters` | Generic lookup by arity, collection matching, Mermaid. |
| 7 | `IEnumerable<ITypeMember> Members` | Member access resolution. Typed views (fields, methods, …) become extensions over it. |
| 8 | `internal bool IsSameTypeAs(ITypeDefinition)` | Identity (I1). Scope ambiguity, `IsSameType`, and every rule below. |
| 9 | `internal ITypeDefinition? NullableOf` | Nullability is the platform's relation (I5): the analyzer, `DateOffsetRule`, `ClrConvertMethods`. A host with no nullable form answers null. |
| 10 | `internal ITypeDefinition? ElementType` | Replaces `IsCollectionShape` + `SequenceElementType`: null when not a sequence. The analyzer and `GetElementType`. |
| 11 | `internal IEnumerable<ITypeConversion> Conversions` | **Must stay:** a conversion declared by the *other* end of a mixed pair is visible only through it (`ModeledConversions.Find`, `ClrTypeDefinition.FindDeclaredConversion`; `MixedPairAuthorityTests`). |
| 12 | `internal ITypeConversion? ResolveConversionFrom(ITypeDefinition)` | The conversion door (T-03b): analyzer, VM, domain type model, overload scoring (~17 production sites). |
| 13 | `internal bool IsAssignableWithoutConversion(ITypeDefinition)` (default body) | Assignability: analyzer, `IsAssignableFrom`, `ClrTypeReferences.Denotes`. |
| 14 | `internal ITypeOperator? ResolveOperator(OperatorKind, ITypeDefinition?)` | The operator door: `GetOperator`, the domain's operator table. |

Members 1–7 are the `16a62dcc8` contract minus `Fields`/`Properties`/`Methods` (now views) and `ReflectedType`
(now `IClrTypeDefinition.RuntimeType`, the host's own). Members 8–14 are what has proved its keep. The contract
has no `bool` or `int` property: a relation takes another definition; a classification is a consumer's rule.

## 2. Where each of today's 42 members goes

Askers are production sites at `dfafe16b4` (tests and implementers excluded). "CLR rule" means the CLR host's own
table (`ClrPrimitive` through `GetClrPrimitive()`, or `IClrTypeDefinition.RuntimeType`), used by the rules that
implement C# semantics. "Domain prelude" means the domain's names bound to definitions (§3.1).

| Member | Askers | New home |
|---|---|---|
| `Name`, `Namespace`, `FullName`, `BaseType`, `Interfaces`, `GenericParameters`, `Members` | see §1 | stays (1–7) |
| `AccessModifier` | 0 (4 test asserts) | host's own (`ClrTypeDefinition`, `TypeDefinitionNode`) |
| `Fields` | 3: `AstTypeDefinition` ×2, `AbiEmitter.GetterField` | extension: `Members.OfType<ITypeField>()` |
| `Properties` | 5: member resolver, `AstTypeDefinition` ×2, `FindMatchingIndexers`, `GetElementType` | extension |
| `Methods` | 9: `AwaitPattern` ×2, analyzer ×3, invocation resolver, `AstTypeDefinition` ×2, `FindMatchingMethodOverloads` | extension |
| `Constructors` | 3: constructor resolver, `AstTypeDefinition`, `FindMatchingConstructors` | extension (CLR `Members` must include constructors: row M6) |
| `Operators` | 1: `ClrOperatorResolution.OfferedBy` (the CLR's own) | host's own; `ResolveOperator` is the door |
| `IsSameTypeAs` | 2: `UnitTypeScope`, `IsSameType` | stays (8) |
| `NullableOf` | 8 in 3 files: analyzer, `DateOffsetRule`, `ClrConvertMethods` | stays (9) |
| `IsCollectionShape` | 2: analyzer `ElementOf`, `GetElementType` | merged into `ElementType` (10) |
| `SequenceElementType` | 1: `GetElementType` | becomes `ElementType` (10); the host answers its own indexer preference |
| `Conversions` | 2: `ModeledConversions`, `ClrTypeDefinition` | stays (11) |
| `ResolveConversionFrom` | ~17: analyzer, VM, `DomainTypeModel`, extensions, CLR host | stays (12) |
| `IsAssignableWithoutConversion` | 5: analyzer ×3, `IsAssignableFrom`, `Denotes` | stays (13) |
| `ResolveOperator` | 1: `GetOperator` | stays (14) |
| `IsEnumeration` | 7: analyzer ×6, domain `Family` | host's own: `RuntimeType.IsEnum`; `AstTypeDefinition`'s declared form. Asked through one analysis helper that knows those two hosts; any other host is not seen (#998 Q5) |
| `HasValueSemantics` | 2: analyzer `IsPlainValueType`, `Family` | host's own, as above |
| `IsInterface`, `IsDelegate` | 1 each: `Family` (to exclude from Entity) | deleted: `Family` asks the domain whether the definition is one of its entity declarations |
| `IsBoolean` | 7: analyzer ×3, `IsGround`, coercion, lowering, `DomainGround` | CLR rule (analyzer); domain prelude (the rest) |
| `IsText` | 11: analyzer ×5, coercion ×2, `IsGround`, unique compiler, exporter, `DomainGround` | CLR rule (analyzer); domain prelude (the rest) |
| `IsCharacter` | 9: analyzer ×4, coercion ×2, unique compiler, exporter, `DomainGround` | the same |
| `IsNumber` | 14: constant folding ×6, analyzer ×3, extensions ×2, `DateOffsetRule`, shift policy, `DomainGround` | CLR rule; domain prelude for `DomainGround` |
| `IsIntegral` | 14: folding ×6, `DomainGround` ×4, analyzer ×2, indexer pick, shift policy | CLR rule; domain prelude |
| `IsSigned` | 8: folding ×5, `DomainGround` ×2, shift policy | the same |
| `IsDecimal` | 3: analyzer, `DateOffsetRule`, `DomainGround` | the same |
| `IsNativeSized` | 11: folding ×6, `DomainGround` ×2, analyzer, indexer pick, shift policy | the same |
| `NumberBits` | 9: folding ×4, `DomainGround` ×4, shift policy | the same |
| `IsTemporal`, `HasDate`, `HasTime` | 4, 3, 3: analyzer (`IsTimestamp`, `CategoryOf`), `IsGround`, `DomainGround` | CLR rule; domain prelude |
| `HasOffset`, `IsDuration` | 1 each: `DomainGround` | domain prelude |
| `IsUniqueIdentifier`, `IsBinary` | 3, 2: analyzer, `IsGround`, `DomainGround` | CLR rule; domain prelude |
| `IsTopType` | 3: indexer pick, `IsGround`, product pack `Classify` | the host's `ElementType` answer (indexer pick); CLR rule (`Classify`) |
| `IsGround()` (extension) | 4: `IsPlainValueType`, `Family`, `ContributedMethods`, `Classify` | deleted; each asks its real question: C# struct rule (CLR rule), "a domain prelude type", "one of the module's declarations", "a CLR scalar" |

## 3. How classification works without the contract

Each rule holds definitions it obtained by binding or from its own host, and compares by identity. None asks a type
what kind it is.

### 3.1 The domain's `Number` family

The domain language has its own type names (`Number`, `Int32`, `Decimal`, `Float`, `Text`, `Date`, …). They are
the DSL's vocabulary, defined by the DSL guide, and the domain prelude binds each to the definition the platform
supplies. For the CLR that table already exists (`DomainTypeMapping.ToClrTypeName` + `TryClrType`), and it is
reused.

```csharp
// Domain-owned. Each family is a set of definitions the prelude bound, not a category of types.
sealed class DomainPrelude {
    readonly Dictionary<string, ITypeDefinition> _bound;   // "Number" → long, "Int32" → int, "Decimal" → decimal, …
    public IReadOnlyList<ITypeDefinition> Numbers => [_bound["Number"], _bound["Int32"], _bound["Decimal"], _bound["Float"]];
    public string? NameOf(ITypeDefinition definition) =>    // the reverse lookup #998 planned for spelling
        _bound.FirstOrDefault(pair => pair.Value.IsSameTypeAs(definition)).Key;
}

bool isNumber = prelude.Numbers.Any(n => n.IsSameTypeAs(definition));
```

`nint`, `byte` or another host's 96-bit decimal joins no family unless the prelude binds a domain name to it. The
domain's families cannot widen by accident (the 2026-11-18 advisor finding), and `DomainGround` is deleted: its
switches become `prelude.NameOf(definition)`.

### 3.2 A validation constraint's applicability (why `TypeCategory` was added)

`range(0, 10)` applies to a numeric property and `length(3)` to a text one. Today `GuardClosedProperties` decides
that by **name string** (`typeName is "Number" or "Int" or "Decimal"`). The constraint owns the rule, keyed by
identity:

```csharp
// Validation-owned: the constraint says which definitions it applies to, using the domain's own bindings.
static bool Applies(Constraint constraint, ITypeDefinition propertyType, DomainPrelude prelude) => constraint switch {
    RangeConstraint => prelude.Numbers.Any(n => n.IsSameTypeAs(propertyType)),
    LengthConstraint or PatternConstraint => prelude["Text"].IsSameTypeAs(propertyType),
    _ => true,
};
```

The type is not asked anything. A user alias (`Price: Number`) resolves to the same definition, so it passes. A
platform type the domain never bound is outside every constraint's domain.

### 3.3 Constant folding's integer width

`x % 8` → `x & 7` is sound only for an unsigned fixed-width `x`, and the mask needs the width. Folding implements
C# semantics for the CLR target, so it asks the CLR host's own table (`ClrPrimitive`, internal, the C# spec's simple
types). It never asks the neutral contract:

```csharp
// CLR-owned (ClrBuiltInRules), keyed by the CLR's own definitions.
static int? UnsignedWidth(ITypeDefinition definition) => definition.GetClrPrimitive() switch {
    ClrPrimitive.UInt8 => 8, ClrPrimitive.UInt16 => 16, ClrPrimitive.UInt32 => 32, ClrPrimitive.UInt64 => 64,
    _ => null,
};
```

A definition that is not the CLR's answers null, and the fold is skipped. Folding is an optimisation, so skipping is
sound. A future host that wants the fold states its own rule. `ClrPrimitive` is a closed list, but it is the C#
specification's, owned by the CLR host and named only there (`ClrTable_IsNamedOnlyByTheClrHostTheVmAndTheRuntimeExecutor`);
it is not on the contract.

## 4. The guard that keeps it small

Reuse `TypeShapeTests.Contract_DeclaresNoClosedListOfKinds` and extend it with two checks:

1. **`Contract_IsExactlyTheDecidedMembers`.** Reflect `ITypeDefinition`'s declared properties and methods
   (accessors excluded) and compare them to a pinned list of names. During migration the list is a ratchet: it
   starts at today's 42, and each row removes names. At the end it is §1's 14. Adding a name means editing the list
   in the same change, citing this decision.
2. **`Contract_StatesNoClassification`.** `ITypeDefinition` declares no property of type `bool`, `int` or any
   enum. A relation (a method taking another definition, or a property returning one) is allowed. This bans the
   flag pattern outright, whatever it is named.

## 5. Migration rows

Each row starts with a failing test, run red and recorded, and ends with the full suite green and Benchmarks at
0 warnings.

| Row | Change | Failing test first |
|---|---|---|
| M0 | The guard (§4) as a ratchet pinned at today's 42 members. | A scratch 43rd member fails `Contract_IsExactlyTheDecidedMembers` (recorded, then removed). |
| M1 | A minimal domain prelude from `DomainTypeMapping`'s table. `DomainGround` is deleted. `Family`, `KindOf`, coercion, the unique compiler, the exporter, lowering and `GuardClosedProperties` ask the prelude by identity. **Interim rule:** the prelude is the CLR table, not yet scope-bound. Removing row: #998 S3. | A test-host definition that answers 64-bit signed integral, and that no domain name binds, is not in the Number family. Today it is `Int64`. |
| M2 | The analyzer (`CategoryOf`, `IsIntegerType`, `IsIeeeFloating`, `IsTimestamp`, text/boolean rules), constant folding, `DateOffsetRule`, the shift policy and the product pack's `Classify` ask the CLR rule (`GetClrPrimitive()`). | A non-CLR definition that answers `IsNumber` is `Cat.Unknown` and is not folded. Today it is `Cat.Number`. |
| M3 | Shape through the hosts: one analysis helper reads `IClrTypeDefinition.RuntimeType` and `AstTypeDefinition`'s form. `Family`'s Entity test asks the domain's declarations. | A third-host definition that answers `IsEnumeration` is not the Enum family. |
| M4 | Delete `IsGround()`, the 21 answers and `ClrGround`. Ratchet 42 → 21. | The ratchet list is lowered first (red). |
| M5 | One `ElementType`, answered by each host, including the CLR's indexer preference. `IsCollectionShape` and `SequenceElementType` go. Ratchet → 20. | Pins first: `byte[]` → null, `List<int>` → `int`, `string` → today's answer, an AST collection → its element. Then the ratchet. |
| M6 | `Fields`, `Properties`, `Methods` and `Constructors` become extensions over `Members`, and the CLR's `Members` includes constructors. Ratchet → 16. | `Members` of a CLR type contains its constructors (fails today). |
| M7 | `Operators` and `AccessModifier` move to the hosts' own types. Ratchet → 14. `Contract_StatesNoClassification` is switched on. | The ratchet lowered to §1's list. |

Not in scope: the analyzer's own `Cat` table (it keeps its removing row, I8b, #972; M2 only changes what feeds it);
`ClrTypeReferences`' 74 production callers; `ITypeDefinitionProvider`.
