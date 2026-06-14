# Anti-Pattern 004: Interface Inheritance with `new` (ITypeMethod / ITypeConstructor)

**Problem:** `ITypeMethod` and `ITypeConstructor` both use `new IEnumerable<IParameter> Parameters` to tighten `ITypeMember.Parameters` (nullable) to non-null. This is legal C# but creates a runtime ambiguity — callers holding an `ITypeMember` reference see nullable; callers holding `ITypeMethod` see non-null. Same pattern in `ClrMethod`, `ClrConstructor`, `AstMethodDefinition`, `AstConstructorDefinition`.

## Plan

1. **Make `ITypeMember.Parameters` non-null.** Change the signature from `IEnumerable<IParameter>?` to `IEnumerable<IParameter>`. Have field implementations return `[]` instead of null.

2. **Remove the `new` declaration from `ITypeMethod` and `ITypeConstructor`.** They no longer need to override the nullability. The two interfaces become empty — they add nothing over `ITypeMember`. Decide whether to keep them as empty marker interfaces or remove them and have `ITypeDefinition.Methods` return `IEnumerable<ITypeMember>`.

3. **Update implementations.** `ClrTypeField` currently returns null for `Parameters`. Change to `[]`. Same for the Ast equivalents.

4. **Remove the marker interfaces** if the decision is to eliminate them. This requires updating all consumers that accept `ITypeMethod` or `ITypeConstructor` — mostly in MemberResolutionPass and TypeResolutionPass — to use `ITypeMember` instead.

**Lines saved:** ~24 (2 interface files) + ~30 lines of implementation boilerplate across Clr and Ast types = ~54 net.

**Risk:** Medium — this is an interface-breaking change. Every consumer that references `ITypeMethod` or `ITypeConstructor` needs to be updated. The change itself is mechanical (replace with `ITypeMember`) but requires touching ~20 call sites.

**Timeline:** 1-2 hours.
