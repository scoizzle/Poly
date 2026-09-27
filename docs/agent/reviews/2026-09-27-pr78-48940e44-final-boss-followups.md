# PR 78 re-verify — follow-ups

**Target:** https://github.com/scoizzle/Poly/pull/78 — `48940e446487e102e5f5e3ff2c895e4f0fcde965` (branch `docs/first-principles`, base `640025f7`, prior reviewed SHA `8ed927e3`).

Plain-English status of the four prior findings.

- **F1 — fixed.** The document used to call `Notify` a store member. `Notify` is actually a method on the runtime instance, and it calls the store's `NotifyTransition`. Both places in the doc (the "where it lives in code" line and the subscription observation) now say this correctly.
- **F2 — fixed.** The document used to imply the domain's name and its `uses` ids are children of the domain node. Only types, imported contracts, and contract bindings are children; the name and the extension ids are plain properties. The document now says exactly that.
- **F3 — fixed.** The unexplained shorthand "µop" and "bags" is gone. The two spots now read "per-micro-operation interrupt machinery" and "analysis metadata," and no such shorthand remains anywhere in the document.
- **F4 — fixed.** `Poly.Benchmarks` was missing from the list of projects that reference only the core `Poly` project. It is now in the list, which matches its project file.

None open.
