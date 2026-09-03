# C4-adversarial-rt — Dogfood2 Findings

**Date:** 2026-07-18
**Total findings:** 6 (5 OK, 1 pain)

## Findings

| ID | Cat | Title | Severity | PainScore | Bucket |
|----|-----|-------|----------|-----------|--------|
| C4-F2 | A | DMAS001 hints still invisible via GetAnalysisSummary | 3 | 15 | other |
| C4-F1 | X | DMAS001 hints generated | 0 | 0 |  |
| C4-F1b | X | DMAS001 code present | 0 | 0 |  |
| C4-F6 | X | Micro-built Clinic round-trips | 0 | 0 |  |
| C4-F7 | X | No subscription → no fan-out (honest) | 0 | 0 |  |
| C4-F8 | X | Subscription via evolution works | 0 | 0 |  |

## Details

### C4-F2: DMAS001 hints still invisible via GetAnalysisSummary
- **Category:** A
- **PainScore:** 15 (S=3 F=3 B=2 C=4)
- **Notes:** Hints (2) generated but filtered out of summary. Agent calling get_domain_analysis won't see them. Must call get_domain_suggestions separately.
- **Repro:**
  1. Create entity with properties
  1. Don't add stages
  1. Call get_domain_analysis
- **Workaround:** Call get_domain_suggestions instead
- **Quotes:**
  > GetAnalysisSummary only passes Error+Warning to Messages, not Hint severity

