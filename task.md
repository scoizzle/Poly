# Task checklist — grammar-integration full-send

Source: `docs/plans/grammar-integration.md`

## Done

- [x] GIP preflight (C99 dual-run)
- [x] GI-1 DslTokenReader
- [x] GI-2 DslGrammar structure table
- [x] GI-3 Matcher structure dispatch + hybrid handlers
- [x] GI-4 E2 hybrid locked (expr/effects remain RD; documented on DslGrammar)
- [x] GI-5 Annotation MatchRule + `AnnotationRegistry.ContributePatterns` / `RegisterGrammarContributor`
- [x] GI-6 `DslTokenWriter` + printer smoke; DomainDslPrinter remains product façade (domain walk)
- [x] GI-7 Delete `PolyDslTokenizer`; golden DslTokenReader tests; CORE + DomainModeling README

## Park

- [ ] GI-8 JSON expr parser on Grammar
- [ ] GI-9 non-text streams
- [ ] E1 nested expression grammar (temporal pack admit)

## Verify

- [x] Full suite green (1893)
