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

### TypeCategory unused flags — KEEP

**What they are:** 12 of 26 `TypeCategory` flags with no current consumers.

**Why keep:** `TypeCategory` is a classification system that describes types across multiple dimensions. Removing flags because no current code queries them couples the classification scheme to current consumers. The flags represent real type characteristics — a type can be `DateOnly`, `Boolean`, or `FlagEnumeration` regardless of whether anyone checks. Part of the coherent type-classification model.

### TypeDefinitionProviderCollection ICollection surface — KEEP

**What it does:** Implements `ICollection<ITypeDefinitionProvider>`, forcing `Contains`, `CopyTo`, `IsReadOnly`. No consumer calls these via the interface. `ProviderCount` duplicates `Count`.

**Why keep:** The interface signals "this is a collection" to the type system and forces `Count`, `Add`, `Remove`, `Clear` — which ARE used. The unused interface methods are compliance code that works correctly. Not dead, just dormant.

### IClrType + TypeDefinitionRuntimeTypeExtensions — KEEP

**What they do:** Separate interface for CLR-backed types, with extension methods for getting the `System.Type`.

**Why keep:** Adding `Type? RuntimeType` to `ITypeDefinition` couples every type definition implementation — including AST-backed domain types — to `System.Type`, which is a CLR concept. The separate interface keeps the core type system abstraction clean.

### ITypeMethod + ITypeConstructor merge — KEEP SEPARATE

**What they are:** Both add only `new IEnumerable<IParameter> Parameters` but represent different member kinds.

**Why keep:** Methods and constructors are semantically distinct — they appear in different collections (`Methods` vs `Constructors`), have different CLR representations (`MethodInfo` vs `ConstructorInfo`), and are resolved differently in lowering and code gen. Merging them into a single `ICallableMember` would lose the type-level distinction that drives correct member resolution.
