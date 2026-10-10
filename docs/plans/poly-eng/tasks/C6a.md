# TASK C6a - Remove auto-link; linking is explicit
Status: merged #144 `7b6f69e2` 2026-10-09. Branch slice/c6a-remove-auto-link-v3. Lane B. Review 2. Implement mill: Grok (review on OpenCode).
## Scope
Decision 10 (no guessing) is unconditional. Delete `TryAutoLinkUnambiguousOutbound` (`Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs:599-610`; only caller is the `relationshipName is null` else at `DomainInstanceStore.cs:219-223` — card `HostAbi.cs:820` / `:782` are stale; file is 645 lines).
Drop BindCreate's `outs.Count == 1` / `autoLink` / `wireUnambiguousBackRef: autoLink` (`StoreBind.cs:105-116, 123-124`) and the `BuildTargetCreateArgs` flag plus its `this` arm (`:237-257`; `FindAutoWireBackReference` is `Actions.cs:941`, not `:846`). BindCreate then calls `BuildTargetCreateArgs` like BindProbeCreate `:147`.
Leave `FindAutoWireBackReference` at `Notify.cs:126` (CreateNav still passes `this`) and `BuildCreateNavArgs` (`StoreBind.cs:277` is create-in args, not by-name as the card said). Leave `TryLinkCreateInBackReference` (`HostAbi.cs:617-627`, also `LinkRelated` `:62` and store create-in `:217`).
Edit `Poly.Mcp/Docs/poly-dsl-guide.md:73-79` (Type-create auto-link paragraph, including the stale `_fines.Add` sentence) and `:141` (`when` `create Fine`). The create-in auto-wire note at `:91-100` stays.
No new type.
## Files
- `Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs` (delete `TryAutoLinkUnambiguousOutbound`)
- `Poly/DomainModeling/Runtime/DomainInstanceStore.cs` (drop the else at `:219-223`)
- `Poly/DomainModeling/Lowering/DomainToCSharpExporter.StoreBind.cs` (BindCreate like BindProbeCreate `:147`; delete `wireUnambiguousBackRef` and the `autoWire` `this` arm)
- `Poly.Mcp/Docs/poly-dsl-guide.md` (`:73-79`, `:141`; `:992` "auto-links" on named create-in → "links")
- `docs/probes/dogfood/simulate-create-type.poly` (comments `:5-8` currently expect auto-link)
- `docs/probes/dogfood/simulate-create-create-in.poly` (comments `:5-8` currently "Both actions link")
- `Poly.Tests/DomainModeling/Lowering/ParityTests.cs` (two new rows; `Invoke_CreateInAction_AgreesOnLinkedChildren` `:244-252` stays)
- `Poly.Tests/Mcp/SimulateCreateDogfoodTests.cs` (class comment `:12-13`; `TypeOnly_UnambiguousManyRel_ListsAndLinks` `:98-117`; `Combined_TypeThenRel_OnOnePatron_BothLinked` `:140-180`; comment `:184`)
- `Poly.Tests/DomainModeling/Lowering/DomainToCSharpExporterTests.cs` (`Export_CreateType_UnambiguousManyRel_CreateAttaches_BindCreateDoesNotDoubleAdd` `:2070-2118`)
- `Poly.Tests/DomainModeling/Compile/EmitGoldenTests.cs` + BindCreate-`this` goldens (regenerate; PR names them)
- Named and left alone: `Notify.cs:126`, `StoreBind.cs:277` `BuildCreateNavArgs`, `Actions.cs:941` `FindAutoWireBackReference`, `TryLinkCreateInBackReference`, `Export_CreateIn_AutoWiresUnambiguousSingularBackRef` `:511-543`, CreateNav `Fine.Create(..., this)` (`simulate-create-type/Patron.cs.golden:31`)
## Shape matrix
| Input kind | Before | After | Test that proves it |
|---|---|---|---|
| Bare `create Type` with one many-rel (`Patron.fines: many Fine`, Fine has `patron: Patron`) | Simulate: `CreateCore` else `:219-223` → `TryAutoLinkUnambiguousOutbound` links `fines` + reverse. Print: BindCreate `Fine.Create(..., this)` (`simulate-create-type/Patron.cs.golden:334`) → `AttachFines` | Both unlinked: Patron.fines `0`, Fine registered. BindCreate Fine.Create takes patron from values / null, not `this` | new `ParityTests` row, red on master if it asserts `fines=="0"` (today both sides `"1"`); flip `SimulateCreateDogfoodTests.TypeOnly_UnambiguousManyRel_ListsAndLinks` to list=1, HasFines false, fines/patron links 0 |
| Two many-rels to Fine (`fines` + `waived`) | already unlinked (`outs.Count != 1`) | unchanged | `TypeOnly_AmbiguousManyRel_ListsButDoesNotLink` `:183-227` (drop the TryAutoLink comment) |
| Explicit `create in fines` | store `:205-217` Link + `TryLinkCreateInBackReference`; print `CreateFines` → `Fine.Create(..., this)` (`Notify.cs:126`; golden `:31`) | unchanged; reverse still wired | new `ParityTests` row: after invoke, `fines=="1"` and policy `any fines where patron exists` true; `RelOnly_CreateIn_ListsAndLinksBothDirections`; `Export_CreateIn_AutoWiresUnambiguousSingularBackRef` |
| BindCreate of a type whose source owns one many-rel (university Student→Enrollment `Student.cs.golden:557`, crm Account Contact/Ticket, hotel Room Reservation, …) | `wireUnambiguousBackRef: autoLink` passes `this` | BindProbeCreate shape: `StoreBind.cs:147` (no flag); printed `values["patron"] ?? null` (`simulate-create-type/Patron.cs.golden:375`) | flip `Export_CreateType_UnambiguousManyRel_*` so the Fine.Create call in BindCreate does not contain `this`; regenerate BindCreate-`this` goldens; CreateNav `:31` stays |
| Combined AssessByType then AssessByRel on one Patron | both Fines linked, fines=2 | Type Fine unlinked, Rel Fine linked both ways; list=2, fines links=1 | flip `Combined_TypeThenRel_OnOnePatron_BothLinked`; probe comments |
| Guide Type-create auto-link | `:73-79` documents unambiguous many-rel auto-link; `:141` is `create Fine` in `when` | paragraph gone; `:141` is `create in fines` so the example still names the relationship; `:992` says create-in "links"; `:91-100` create-in auto-wire stays | `git grep -n -i auto-link -- Poly.Mcp/Docs/poly-dsl-guide.md` empty |
## Done when
- `git grep -n TryAutoLinkUnambiguousOutbound` is empty (product, tests, comments).
- `git grep -n wireUnambiguousBackRef -- Poly` and `git grep -n autoLink -- Poly/DomainModeling` are empty. BindCreate no longer computes `outs.Count == 1`.
- T2 parity row: bare `create Type` with one matching many-relationship is unlinked in both simulate and print (`fines=="0"`). Second parity row: explicit `create in Rel` still links outbound and the unambiguous reverse.
- Guide no longer describes auto-link. `FindAutoWireBackReference` at Notify and `TryLinkCreateInBackReference` remain. Full suite green. PR calls out the printed BindCreate change and names regenerated goldens.
## SHIP if / NOT SHIP if
SHIP if the greps hold, the guide no longer describes auto-link, both parity rows pass (Type-create unlinked row red on master, green after), and create-in reverse still agrees.

NOT SHIP if simulate and print disagree, if BindCreate still passes `this` for by-name create, if CreateNav / `FindAutoWireBackReference` at Notify or `TryLinkCreateInBackReference` was deleted without Scot's word, or if goldens were edited by hand.
## Tests
```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ParityTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/SimulateCreateDogfoodTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainToCSharpExporterTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/EmitGoldenTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/StoreBindCreateTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainEntityInstanceTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj
```
## Hand-edit
One method and one store else-branch go away, BindCreate stops guessing `this`; create-in still names the relationship in Notify and HostAbi. Scot can open those three spots cold.
## Needs Scot
Answered (Scot 2026-10-09 23:16): KEEP. Decision 10 does not cover the create-in back-reference slot choice, so `FindAutoWireBackReference` and `TryLinkCreateInBackReference` stay.
## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-09 | planner | grok/grok-4.6 | `a5530ba9` | planned | - |
| 2026-10-09 | implementer | grok/grok-4.6 | `1dabe15f` | pushed | tests 3488/3488; sweep: greps empty, BindCreate matches BindProbeCreate |
| 2026-10-09 | planner | opencode/opencode-go/deepseek-v4.1-flash | `7b6f69e2` | merged #144 | squash-merged to master |
