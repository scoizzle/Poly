# PR 79 / `5fbac3a7` — Final Boss re-verify follow-ups

Plain-English status of prior findings and carried items. Closed here: F28, F29, F30. New: F32 (blocking), F33 (pre-existing, non-blocking).

## Closed

- **F28** — The developer guide (`domain-execution-model.md`), `docs/interpretation/README.md:10`, and `domainmodeling-capability-inventory.md:99` no longer describe the deleted store jobs or the preprocess. No doc anywhere names `AnyRelated`/`AllRelated`/`NoneRelated`/`CountRelated`.
- **F29** — The DSL guide now says an `exists`-on-property is rejected only where every quantifier on the way to it resolves, and explicitly carves out an unresolved nested quantifier, instead of leading with "at any depth … is rejected".
- **F30** — The `e2e-2-README.md` Status marker is back to `[ ]`; the descriptive prose moved to a separate `Note:` line.

## Open (suggestion, blocking)

- **F32** — `docs/domainmodeling-capability-inventory.md:99` says "each quantifier lowers to a `ForEachLoop` … with a `BreakStatement` once the answer is known", but the table right above it lists `count Rel` / filtered. Bare `count Rel` is `this.Rel.Count` (no loop) and filtered `count … where` never breaks (`DomainExpressionLoweringPass.cs:437-441,482-483`). Qualify the sentence to match `domain-execution-model.md:222`.

## Open (nit)

- **F31** — Some historical/parked plan and task docs (`ef-and-api-codegen.md`, `live-demo-reliability-2026-08-13.md`, `domainmodeling-e2e-representation-2026-08-13.md`, `e2e-0-1-guide.md`, `e2e-2-1-implement.md`, `e2e-r-8-…`, `e2e-x-10-…`, `p1-temporal-design-lock.md`) still say Q3′ export throws. These are not product-truth docs and do not block; annotate/demote on next touch.

## Open (suggestion, pre-existing, non-blocking)

- **F33** — `docs/complexity-semantic-map.md:140,142,270,274` (linked from `docs/CORE.md:266`) and `Poly.Mcp/Docs/poly-dsl-guide.md:857` still describe a runtime quantifier/path "preprocess". Master had already removed it before this PR, and this PR did not touch these lines. Rewrite them the next time the map is touched, or put this on the board.

## Carried (unchanged, non-blocking)

- **F14** — to-one runtime read still uses `Store.GetRelatedInstances` (deferred).
- **F17** — unlinked path-prefix hop: simulate false, export `NullReferenceException` (pre-existing; now documented as a known gap).
- **F20** — `RuntimeAnalysisCache.cs:433` still has the "VM StoreQuantifier path" comment (moved to PR 80).
- **F24** — Number-valued policy: simulate treats nonzero as true, printed C# fails `CS0029`; scalar `exists` also differs (parked).
- **F25** — `PolicyConstraintAnalyzer` still does not validate a nested quantifier's relationship (parked).
