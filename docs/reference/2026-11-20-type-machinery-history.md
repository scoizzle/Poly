# Type machinery history (#972 I-series)

Moved from `docs/CORE.md` §3.1 on 2026-11-20, unchanged. These paragraphs described the #972 slices I1, I2, I4, I5, I6, S5, S6, I8c and I8d when they landed. #998 replaced the machinery they name: `ITypeDefinitionProvider`, `PrimitiveType`, `IsSameTypeAs` and the conversion and operator answers on a definition are gone, and identity, nullability, conversions and operators are type-system rules. The current map is CORE §3.5. This record is why the earlier shape existed, not how the code works now.

**The AST constructs no CLR type (I8d).** `TypeReference.To<T>()` and `To(Type)` are C# 14 static extension members declared in `TypeReferenceExtensions`, C#'s view of a CLR type in a tree (`Poly/Interpretation/CSharp` since #998 T13, so neither the AST nor Introspection names the other's types), so the AST names no `typeof` at all (comments aside). Every producer keeps the same node semantics for now; the node itself (`ClrTypeReference`) still carries a `System.Type`, and removing it is its own row (#972).

**The analysis states definitions, not CLR type references (I8c).** Where the analyzer builds a type reference it states `TypeDefinitionReference(ITypeDefinition)` — the cast of an implicit native-integer conversion, the native shift-count cast, a dotted type name rewritten to a type, and the lifetime pass's member replacement — so the meaning layers no longer inject `System.Type` into the tree. `TypeDefinitionReference` prints as its type name, so a printed tree stays deterministic. `ClrTypeReference` remains for a source-level reference (the C# surface's `typeof`) and in the AST node itself; the node's removal is its own row (#972).

**Nullability is a platform-stated relation (I5).** `ITypeDefinition.NullableOf` names the type a definition wraps when it is the host's nullable form; the CLR answers `Nullable<T>`, and a modeled definition answers null (optionality is the caller's, T-06). The CLR's `T -> T?` lift is therefore the CLR's answer for a destination whose `NullableOf` is `T`, not a rule outside the contract, and a host with a different model states its own.

**Which platform answers a mixed pair (I4).** The contract's shape decides it: an operator is asked of the left operand's definition (`GetOperator(kind, right)`), so the left operand's platform selects — the owner ruling on #942 — and a conversion is asked of the destination (`destination.ResolveConversionFrom(source)`), so the destination's platform selects and reads the source's declarations through the neutral `Conversions`. Both mixed directions are exercised today (`NumericBesideANonNumericOperand_OffersItsOwnOperator_SoAModeledImplicitConversionReachesIt`, `ResolveConversionFrom_ClrToObject_IsImplicit`), so no new selection rule is introduced.

**A provider states what it holds (I2).** `ITypeDefinitionProvider.Types` is the enumeration a consumer uses instead of a caller-supplied universe: the CLR registry lists what it has resolved (its universe is open, so that is a snapshot, not a claim), a declaration registry lists its declarations, and the composed provider lists each definition once by identity. It is not used for derived answers that must not depend on cache state: the outbound conversion view stays vocabulary-driven because a conversion exists whether or not the target was resolved yet.

**Identity is the platform's answer (I1).** `ITypeDefinition.IsSameTypeAs` is each platform's notion — a CLR definition is its registry instance, a modeled collection its kind and element, a generic parameter its name on its declaring type — and `TypeDefinitionExtensions.IsSameType` only delegates, so a third platform's definitions compare by their own rule and the root names no platform's definition type.

**The neutral contract asks only what every platform can answer (S6).** `ITypeDefinitionProvider` states name and primitive resolution; the runtime-`Type` handle is `IClrTypeDefinitionProvider` in `CommonLanguageRuntime/`, and a consumer asks through `GetClrTypeDefinition`, which answers null for a host with no CLR types. `ITypeDefinition.IsCollectionShape` and `SequenceElementType` are platform answers (the CLR's from its representation, a modeled collection's from its definition), and `GetElementType` prefers an indexer then asks the platform. C# 11's native-integer rule (`NativeIntegerConversionFrom`) and the `ITypeDefinition -> Type?` view live in `CommonLanguageRuntime/` with the rest of the CLR's facts.

**The shared primitive vocabulary is not a CLR list (I6).** `PrimitiveType` names what a host may have, not what the CLR has: `Undefined`, `Null`, `Symbol` and `BigInt` were appended for a protocol host, with no CLR type behind them (`ClrPrimitiveTypes` answers none) and no ordinal moved. A platform maps the subset it has, and `ITypeDefinitionProvider.GetTypeDefinition(PrimitiveType)` resolves and `Types` enumerates it. A primitive the shared vocabulary does not name at all is answered by its platform on that platform's own interface (S4, below); `PrimitiveAtom` and `PlatformAtom` are gone, and `SecondHost_StatesAPrimitiveTheVocabularyDoesNotName_AsAFactOfItsOwn` pins it.

**The root carries no platform's encoding (S5).** `PrimitiveType` holds only what every platform shares (`IsInteger`, `GetDisplayName`); the CLR's `Type` maps are `CommonLanguageRuntime/ClrPrimitiveTypes` and C#'s keywords are `Poly/Interpretation/CSharp/CSharpKeywords`. `ITypeDefinitionProvider.GetTypeDefinition(PrimitiveType)` has no default body, so a provider answers for its own primitives instead of inheriting the CLR's through a runtime `Type` it never mentioned.

## From `Poly/Introspection/README.md` (pre-#998 T9b)

### Conversions and operators (T-03b, T-24)

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
reference until its own row removes it, and the factory that builds one is C#'s internal shorthand (`TypeReference.To<T>()`, a static extension member declared on the C# surface), not
the AST's: `Poly/Ast` names no `typeof`, and Introspection names and uses no AST type (#998 T13, pinned by
`ArchitectureBoundaryTests`). `IsAssignableWithoutConversion` is its first half: the same type, a base type or an interface, and for .NET `Type.IsAssignableFrom`, the lift from `T` to `T?` and `object` accepting every definition (the VM boxes a modeled entity). The box into `object` is also `ClrBuiltInRules`' implicit conversion, so it is stated twice; the two agree on every CLR definition (`ObjectAcceptsEveryClrDefinition_IsAssignableFromAndResolveConversionFromAgree`) and differ for a modeled source by design. Still outside the contract, with no removing row yet: the tier sum itself (a private copy of overload resolution that keeps the first of equal candidates; it is not C# 12.6.4 better-conversion), the boxing rule stated twice, and `SyntaxTypeCompatibilityAnalyzer`'s `IsImplicitlyConvertible(Type, Type)` with its nullable lifting, `CoerceTo`, `IsIntegerType`, `ConvertMethodName` and `IsIeeeFloating`. T-23 publishes `IsAssignableWithoutConversion`. The collection shape is the type system's `ElementType` (T9c), and the root names no CLR collection. Type identity is the type system's (I1, #998 T9a): a definition names the system that made it
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

