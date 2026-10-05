# pack-3b-2 — Resolve contract internal from a loaded domain

**Difficulty:** M  
**Status:** `[x]`  
**Prereq:** pack-3b-1 `[x]`  
**Claimed by:** fleet agent pack-3b-2 (opencode) 2026-08-13 

## Objective

`SourceIdentifier` of `contract internal billing` resolves to a loaded domain named `billing` (or file stem). Producer fills empty/partial contract. Hand-authored body remains valid.

## Exact steps

1. Failing test: two domains in a test host; parent has `Billing: contract internal billing v1 {}`; after produce, ChargeRequest resolves.
2. Session or a `DomainSuite` helper holds multiple `Domain`s. Keep it small — no nested Domain IR.
3. Clash/leak rules still apply (`ContractIntegrationAnalyzer`).
4. MCP: only if a session already has a place to hang a second domain; otherwise test-host only and document the MCP gap.

## Verification

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj --treenode-filter "/*/*/*/*/*InternalDomain*"
```

- [x] Empty internal contract fills  
- [x] Hand-authored body still parses  

## File ownership

| Edit | Do not edit |
|------|-------------|
| session / suite helper | `DslGrammar` core keywords |
| MCP only if required | exporter host |

## Status

**Status:** Done — `DomainSuite` (multi-`Domain` holder, `Poly/DomainModeling/Packs/DomainSuite.cs`) resolves `ImportedContract.SourceIdentifier` → loaded domain by name and fills declared `contract internal` bodies via `InternalDomainProducer.Fill` (new method: grows `Types`/`Endpoints`, preserves hand-authored body — authored members win by name, no duplication, declared version/name kept). Fail-closed on unresolved source. Clash/leak rules still apply unchanged because fill runs before the normal analysis gate (`ContractIntegrationAnalyzer` rejects a filled contract that clashes with a parent type or leaks into stored properties). 4 new tests (2154 → 2158, suite green).

**MCP gap (documented, not built):** `McpSessionState` holds a **single** `Domain` — there is no seam to hang a second domain in a session, so the multi-domain resolve/fill is test-host + product-`DomainSuite` only. Enabling it in MCP requires a session registry of loaded domains (e.g. a `register`/import surface or a suite member in `McpSessionState`) plus wiring `FillInternalContracts` into `apply_dsl`/`evolve` — that is a later slice (pack-3b-3 roundtrip / pack-3c) and was not touched here per the file-ownership table.

**Stopped at the slice gate (pr1) per pack-README — pack-3b-3 not started.**
