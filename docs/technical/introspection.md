# Introspection — Technical Deep Dive

**Files:** `Poly/Introspection/` + `Poly/Introspection/CommonLanguageRuntime/` (22 files, ~1,800 lines)

## Qualification Standard

Throughout this document, "dead code" means a value or method that is **computed or defined but the result is never consumed by any code path**. If the code is part of a coherent, self-contained system that produces a data model, and the unused parts are simply parts of that model that no current consumer queries, it is considered **dormant infrastructure** — not dead code — and is kept.

## Architecture

Provider-based type abstraction decoupled from CLR reflection. Interfaces allow composition of multiple type sources.

## Reviewed Recommendations

### Read/Write/Initialize delegates on ITypeProperty/ITypeField — KEEP

**What they are:** `MemberReadDelegate? Read`, `MemberWriteDelegate? Write`, `MemberWriteDelegate? Initialize` on `ITypeProperty` and `ITypeField`. Compiled via `System.Linq.Expressions` in `ClrTypeProperty.cs` (~120 lines) and `ClrTypeField.cs` (~60 lines).

**Why they exist:** They implement the introspection contract — `ITypeProperty` and `ITypeField` promise that member values can be read and written through delegates. No current backend reads these delegates (VM uses `MethodInfo`, LINQ backend uses `Expression.MakeMemberAccess`).

**Why keep:** Part of the coherent member-introspection data model. `ITypeProperty` and `ITypeField` define a complete interface for type members that can be read and written. Removing the delegates means every CLR property/field introspection becomes metadata-only (name, type, modifiers) with no dynamic access capability. Any future backend that needs dynamic property access (data binding, marshaling, serialization) would rebuild the delegate infrastructure.

### ClrTypeSyntheticProperty — KEEP

**What it does:** Builds synthetic array indexer properties for array types, with Read/Write delegates.

**Why keep:** Same reasoning — part of the complete introspection data model for CLR types. Array indexers should be introspectable like any other member.

### TypeMemberExtensions (CanRead/CanWrite/CanInitialize) — KEEP

**What they do:** Extension methods checking whether the Read/Write/Initialize delegates are non-null.

**Why keep:** Consumed by `DomainTools.cs` (MCP server) and test files. Not dormant — actively used.

### What a type is — asked of the system that owns the question

**What it is:** `ITypeDefinition` states no classification (#998 T8). A consumer asks the owner of its question: C#'s rule (`ICSharpTypeRules`: `SimpleTypeOf`, `IsEnumType`, `HasValueSemantics`) and C#'s words `string` and `object`; the CLR host's own table (`ClrBuiltInRules.IsPrimitive`, `IsCalendarType`, `IsTimestamp`, `IsGuid`, `IsByteArray`); a foreign host's own rule; the domain's words and declarations. See `docs/decisions/2026-11-19-a-type-system-owns-definitions-vocabulary-and-rules.md` §3.

**Why it replaced `TypeKind`, `PrimitiveType`, the fact lookup and the definition's own answers:** the closed enums (`TypeKind`, `PrimitiveType`'s ids) made every platform map onto one list and every consumer switch on it. The answers that replaced them (`IsNumber`, `IsEnumeration`, …) still let a foreign definition decide what C# or the domain would do with it by answering a question it did not own; T8 removed them. The form a declaration is written in (`TypeDeclarationForm`: class, interface, enum, struct) is syntax on the AST node.

**Where the old distinction went:** `Collection` is structural (the type system's `ElementType`, which merged `IsCollectionShape` and `SequenceElementType` in T9c); an enumeration and value semantics are C#'s rule; integer-ness is C#'s simple types; ground-ness is the CLR host's own identity facts (`ClrBuiltInRules.IsPrimitive` and its siblings; `ClrPrimitive` is gone, T10b). Domain vocabulary lives in the domain's type system (`DomainTypeSystem`) and `Poly.DomainModeling.Ontology.PrimitiveSemantics`, not in core.

### TypeDefinitionProviderCollection ICollection surface — KEEP

**What it does:** Implements `ICollection<ITypeDefinitionProvider>`, forcing `Contains`, `CopyTo`, `IsReadOnly`. No consumer calls these via the interface. `ProviderCount` duplicates `Count`.

**Why keep:** The interface signals "this is a collection" to the type system and forces `Count`, `Add`, `Remove`, `Clear` — which ARE used. The unused interface methods are compliance code that works correctly. Not dead, just dormant.

### IClrType + TypeDefinitionRuntimeTypeExtensions — KEEP

**What they do:** Separate interface for CLR-backed types, with extension methods for getting the `System.Type`.

**Why keep:** Adding `Type? RuntimeType` to `ITypeDefinition` couples every type definition implementation — including AST-backed domain types — to `System.Type`, which is a CLR concept. The separate interface keeps the core type system abstraction clean.

### ITypeMethod + ITypeConstructor merge — KEEP SEPARATE

**What they are:** Both add only `new IEnumerable<IParameter> Parameters` but represent different member kinds.

**Why keep:** Methods and constructors are semantically distinct — they appear in different collections (`Methods` vs `Constructors`), have different CLR representations (`MethodInfo` vs `ConstructorInfo`), and are resolved differently in lowering and code gen. Merging them into a single `ICallableMember` would lose the type-level distinction that drives correct member resolution.
