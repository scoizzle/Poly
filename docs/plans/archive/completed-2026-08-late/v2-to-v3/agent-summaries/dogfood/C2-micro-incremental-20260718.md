# C2-micro-incremental — Dogfood Findings

**Date:** 2026-07-18
**Total findings:** 1 (0 OK, 1 pain)
**Completed:** True

## Findings

| ID | Cat | Title | Severity | PainScore | Bucket |
|----|-----|-------|----------|-----------|--------|
| C2-F1 | A | Library evolution failed | 3 | 13 | other |

## Details

### C2-F1: Library evolution failed
- **Category:** A
- **PainScore:** 13 (S=3 F=1 B=3 C=3)
- **Notes:** [Structural Failure] Duplicate member name 'Return' in entity 'Loan'.
- **Expected:** Better behavior
- **Actual:** [Structural Failure] Duplicate member name 'Return' in entity 'Loan'.
- **Repro:**
  1. [Error] Duplicate member name 'Return' in entity 'Loan'.
- **Workaround:** Check entity/stage definitions

