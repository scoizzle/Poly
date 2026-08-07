# Task checklist — grammar-integration (GI) execution

Source: `docs/plans/grammar-integration.md`

## Pre-work

- [x] Silent trace + suite green before GI-1

## GI-preflight (C99) — **DONE** 2026-08-07

- [x] GIP-0 inventory — `docs/plans/gi-preflight-c99-notes.md`
- [x] GIP-1 `C99Grammar` structure patterns; expr RD (E2 hybrid)
- [x] GIP-2 dual-run: full corpus dual-compiles; `DualRun_*` + throw dual-assert
- [x] GIP-3 gaps → hybrid preferred for cutover; E1 for temporal later

## GI-1 — DslTokenReader — **DONE**

- [x] CharSource + DslTokenReader + parity tests

## GI-2 — DslGrammar table — **DONE**

- [x] Structure patterns + dispatch tests + gap notes on type

## GI-3 — GrammarDslParser dispatch — **DONE**

- [x] Matcher structure dispatch + Unread + concurrent sort fix
- [x] Suite green

## GI-4 — Pack annotation grammar — **NEXT**

- [ ] Annotation rule + pack pattern registration API
- [ ] SQL pack (column/table) round-trips without core matcher edits

## GI-5 — DslPrinter port

- [ ] Printer + TokenWriter skeleton; round-trip corpus

## GI-6 — JSON parser port (optional / pull)

- [ ] When pulled

## GI-7 — Cutover + docs

- [ ] Legacy tokenizer non-product; CORE + guide if needed

## GI-8 — Non-text streams (defer)

- [ ] Foundation `ICharSource` already from GI-1

## Verify

- [x] Build + full suite green after GIP wrap-up
- [ ] Pre-ship review before further large GI slices merge if desired
