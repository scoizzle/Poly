# PR 78 Final Boss — follow-ups (2026-09-27)

Open F# list for `docs/domain-modeling/first-principles.md`. Plain English; each entry gives the doc line, what is wrong, and what the code actually does.

- **F1 (line 219; also 324)** — The doc lists `Notify` as a method of `DomainInstanceStore.cs`. It is not on the store: `Notify` is on the dictionary-backed instance (`Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs:21`), and the store's fan-out method is `NotifyTransition` (`Poly/DomainModeling/Runtime/DomainInstanceStore.cs:434`). Fix: call the store method `NotifyTransition`, or say the instance's `Notify` fans out via `NotifyTransition`.

- **F2 (line 119)** — The doc lists `name`, types, imported contracts, contract bindings, and `Extensions`, then says "Children of the domain node are those members." `Domain.Children` is only types, imported contracts, and contract bindings (`Poly/DomainModeling/Ontology/Domain.cs:32`); `Name` is a string and `Extensions` is a string list (`:20`, `:30`), so neither is a child node. Fix: state that the child nodes are types, imported contracts, and contract bindings.

- **F3 (lines 81, 176, 301)** — Wording nit: "µop" and "bags" are internal terms used without a gloss in an otherwise plain-English doc. Fix: use "per-micro-operation" and "analysis metadata".

- **F4 (line 251)** — Wording nit: the project-reference list leaves out `Poly.Benchmarks`, which also references only `Poly` (`Poly.Benchmarks/Poly.Benchmarks.csproj:14`). The "every chain ends at Poly" conclusion is still true. Fix: add it to the list.
