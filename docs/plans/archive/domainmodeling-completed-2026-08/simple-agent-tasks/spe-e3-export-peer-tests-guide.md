# SPE-E3 — Export peer tests + guide

**Stream:** E  
**Difficulty:** S  
**Status:** `[x]`  
**Soft prereq:** E2  

## Objective

Lock export peer behavior with tests and product guide honesty; retire refuse-only oracle.

## Required reading

- E1/E2 behavior  
- `Poly.Mcp/Docs/poly-dsl-guide.md` §7 (export bullet only)  
- `Export_PeerDependentSubscription_Throws`  

## Exact steps

1. Replace or invert refuse test: export **succeeds** with peer binder; assert method params + notify shape.  
2. Golden: domain with `when Tracks Active as order { assign Status to order Code }` → export contains peer param usage.  
3. Guide §7: remove “C# export does not support peer-dependent”; document generated handler shape in one short note.  
4. Keep fail-closed for nested peer under binder if it reaches export.

## Verification

- [x] Tests green; no `Export_*_Throws` for happy peer-dependent path  
- [x] Guide matches code  
- [x] Full suite green (export peer + E class green; 1 pre-existing O-stream fail unrelated: `SimulatePolicy_RelationshipJson_Accepted`)  

## File ownership

- **Edit:** export tests; `poly-dsl-guide.md` §7 export bullets only  
- **Do not rewrite** owned/policy sections  

## Progress notes

### 2026-08-02 — implement + verify (pass, severity none)

**Implement success:** true · **Verify pass:** true · **Severity:** none  
Static AC check of E3 (no shell: suite/git not re-run).

- **Refuse oracle retired:** E1 already removed `Export_PeerDependentSubscription_Throws`; no `Export_*_Throws` for happy peer path.
- **Success oracles:** `Export_PeerDependentSubscription_HandlerHasPeerParameterAndNotifyPassesThis` + `Export_PeerDependentSubscription_LowersPeerPathPrefixToParameterMember`.
- **Golden:** `Export_PeerDependentSubscription_DslGolden_HandlerParamNotifyAndPeerMember` — exact DSL `when Tracks Active as order { assign Status to order Code }` → `WhenOrderActive(Order order)`, notify `ThisReference`, value `Member(Parameter order, Code)` (body `this.Status = order.Code`).
- **Fail-closed nested:** `Export_NestedPeerPathPrefix_Throws` asserts `InvalidOperationException` Nested path-prefix (matches `DomainExpressionLoweringPass`).
- **Guide §7:** C# export bullet honest (handler param / notify(this) / scalar lower / nested rejected); refuse wording gone from product guide.
- **Sibling paths:** notification-only parameterless still tested; analysis nested covered in `DomainEntityInstanceTests`.

## Status

**Status:** Done — implement success; verify pass (severity none) 2026-08-02
