# pack-3b-gate

**Status:** `[x]`  
**Prereq:** pack-3b-1 … 3 `[x]`  
**Claimed by:** fleet agent pack-3b-gate (opencode, deepseek-v4-flash) 2026-08-13

pr1 + full suite. No entity merge. No import keyword.

## Status

**Status:** Done 2026-08-13 — full suite green (2159/2159), build clean. No entity merge (producer projects `ValueType`s + action endpoints only; `ImportedContract` holds `Types`/`Endpoints`, never `Entity`). No `import` keyword added. pr1 findings: 0 🔴 / 0 🟠, 3 🟡 (filed), 2 ⚪ (deferred/pre-existing). pack-3c not started.

## pr1 findings (pack-3b scope)

| Severity | Finding |
|----------|---------|
| 🟡 | `InternalDomainProducer.Produce` copies only `ValueType`. A single-parameter action whose parameter is an `EnumType` (or a type not on the source value-type list) produces a contract whose endpoint payload fails `ContractIntegrationAnalyzer.PayloadTypeExists` at analyze-time — fail-closed, but the error surfaces at analysis with a generic message rather than at produce time. Known v1 gap (producer is value-types + actions only). |
| 🟡 | Contract value types are copied verbatim; property types referencing e.g. an enum (`Status: ChargeStatus`) are neither copied nor validated (`PayloadTypeExists` checks endpoints only) — a produced contract can silently carry a dangling type reference. Not pack-3b-fixable (analyzer changes are prior-phase); filed for follow-up. |
| 🟡 | `Fill` endpoint merge is by name: an authored endpoint with the same name as a produced one silently wins even if payload differs ("authored members win by name", documented). Acceptable v1; noted. |
| ⚪ | `PolyDslRoundTripTests.cs` has no trailing newline — pre-existing at HEAD too, not pack-3b. |
| ⚪ | `Fill` is not on `IContractProducer` (interface exposes only `Produce`); `DomainSuite` calls the concrete `InternalDomainProducer.Fill`. Intentional minimal interface; noted. |

**Three-layer defense (producer fill):** parse-time contract surface (strict `contract internal source version { value | inbound|outbound operation|event }`, unknown shapes → parse error) · analyze-time clash/leak via `ContractIntegrationAnalyzer` (value-type name clash + "Stored state must use parent-domain types" leak + payload existence) — both preserved and exercised by `FillInternalContracts_FilledDomain_StillFailsClashAndLeakAnalysis` · runtime none (contracts are IR, no runtime enforcement). **Fail-closed:** unresolved `SourceIdentifier` throws (`DomainSuite.FillInternal`, tested); clash/leak rules unchanged. 
