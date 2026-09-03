# Contract substrate — sub-domain cut

**Date:** 2026-08-13  
**Status:** Executed 2026-08-13. Suite 2087 green.  
**Does not:** OpenAPI/gRPC ingest, generated clients, nested `Domain` merge, InternalDomain producer (project another `.poly` into this IR). Producer hook: [`pack-host-2026-08-13.md`](pack-host-2026-08-13.md) wave 4.

## Poly-to-Poly composition (locked, not built)

`InternalDomain` and `ExternalProvider` are **producers**. The consumer is one IR: used sub-domain + ACL value types + endpoints + `bind`. A large Poly product is several small domains that *use* each other the same way Shop uses Stripe.

Locks:

1. **Do not merge domains.** A parent never gains the child's entities, stages, or relationships. Navigation (`invoice: Invoice`) is same-domain only.
2. **Published surface only.** What crosses the door is value types + operations/events. Child entity instances stay in the child store.
3. **Same leak / clash rules.** Parent stored properties must not be typed as the used contract's value types. Duplicate type names fail closed. Internal is not a shared kernel.
4. **Same door.** Parent actions take the ACL payload; `bind` attaches the adapter. No `import` keyword; no silent file include.
5. **SourceIdentifier** for `internal` is the other domain's name (session member or file stem). The producer *fills* `Types` + `Endpoints` from that domain — it does not invent a second grammar.
6. **Do not project instance actions as the v1 surface.** External endpoints are singleton operations. Child instance actions need a recipient identity the current `ContractBinding` does not carry. v1 InternalDomain projector emits only what bind can already express: domain-level operations (façade / entry-shaped actions) + value types + events. Instance-targeted bind is a later slice.
7. **Split when the file is too big for one agent context, one team, or one deployable** — not because a domain has two entities.
8. **The composition root is the public product.** Export / REST / gRPC emit the *root* domain’s entities and actions — the façade that binds. Child domains are not unioned onto the public route table. External contracts are consumed; they never become your routes.
9. **Honesty today:** `MinimalApiGenerator` / `DomainToCSharpExporter` do not read `ImportedContract` or `ContractBinding`. Exporting one `.poly` still means “that file’s entities.” Multi-domain host + bind-as-call is the producer/codegen slice, not a merge.

Hand-authored `contract internal billing { ... }` remains valid (duplicate the published types). The missing producer deletes that duplication the same way an OpenAPI pack deletes hand-authoring Stripe.

```poly
// billing.poly — owned domain
ChargeRequest: value { Amount: Number  Currency: Text }
Ledger: entity {
  Charge: action (request: ChargeRequest) { assign Posted to true }
}

// shop.poly — uses billing; does not contain Ledger
Billing: contract internal billing v1 {
  ChargeRequest: value { Amount: Number  Currency: Text }
  Charge: outbound operation ChargeRequest
}
Order: entity {
  Pay: action (request: ChargeRequest) { assign Paid to true }
}
ChargeOrder: bind Billing Charge to Pay request
```

## Locks

1. An imported contract is a **used sub-domain**: name, version, source, **value types**, endpoints.
2. **ACL is those value types** (owned by the contract). Bind is adapter attachment only.
3. Payload type is a **local** name: primitive or a value type on that contract (then parent-domain value types).
4. Entity **stored** properties must not be typed as a contract value type (analysis error). Action parameters at the bind seam may.
5. No `import` keyword. Hand-authored `contract` / `bind` is the IR an importer will emit later.
6. Duplicate value-type names across contracts (or vs parent `Types`) fail closed.

## IR

- `ImportedContract.Types` — `IReadOnlyList<ValueType>`, init-only, default empty.
- `AddContractValueTypeChange(contractName, valueType)`.
- Endpoints and binds unchanged.

## Known gaps (pack-3c gate, 2026-08-13)

- **Bind resolves by action name only** (`ContractBinding.ActionName`). If two root entities
  share the same action name and one is bound, the generated adapter call is prepended to
  **both** entities' methods — no ambiguity rejection at analysis. Fail-loud (the adapter
  throws), never a silent no-op, but semantically wrong for the unbound twin. Fix options:
  reject ambiguous action names when a bind references them, or scope `ContractBinding` to
  an entity. Not blocking pack-3c — file as follow-up.

## Surface

```poly
Stripe: contract external stripe v1 {
  ChargeRequest: value {
    Amount: Number
    Currency: Text
  }
  Charge: outbound operation ChargeRequest
}

Order: entity {
  Pay: action (request: ChargeRequest) { assign Total to Total }
}
ChargeOrder: bind Stripe Charge to Pay request
```

## Files

| Touch | Change |
|-------|--------|
| `ImportedContract.cs` | `Types` + Children |
| `DomainChange.cs` / `DomainEvolution.cs` | add value type to contract |
| `PolyDslParser.cs` | `Name: value` inside contract body |
| `DomainDslPrinter.cs` | print nested value types |
| `ContractIntegrationAnalyzer.cs` | resolve payload; stored-property leak; name clash |
| `DomainProgramProjection.cs` | emit contract value types |
| `DomainTools.cs` | MCP `contract_value_type` |
| guide + `demo/live/checkout.poly` | honesty |
| tests | round-trip, analyzer, clash |
