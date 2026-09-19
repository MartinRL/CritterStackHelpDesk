---

kanban-plugin: board

---

## Backlog

- [ ] Spec transformer as interpreter (awaiting article reference)
- [ ] Wire Emlang.Generators: Helpdesk.Domain (Commands/Events/Errors/Decider.g.cs) + Decider.Impl + generated SpecTests (decide = Wolverine [AggregateHandler] Handle, evolve = Marten Apply)
- [ ] Rewire Helpdesk.Api endpoints onto the Decider (Critter shell)
- [ ] Blazor standalone SPA UI, dev auto-login (after Decider rewiring)
- [ ] state, ie emlang view, always as given in GWT, as per the decider pattern and kvissig.seadde
- [ ] upgrade xmlang pkg and fix breaking changes
- [ ] Diagram for dialect 1.1 specs (Go `emlang diagram` rejects a:/auto:/s:; em has no diagram yet)
- [ ] ACMM Code Health


## Doing
- [ ] ACMM Ponytail


## Done

- [x] wire up emlang/xmlang like kvissig.se: tracked local-nuget/ (Emlang 0.5.0, Xmlang 0.6.1 + CLIs), NuGet.config, Spec.cs on Emlang.EmParser
- [x] upgrade emlang pkg and fix breaking changes (em 0.4.0, dialect 1.1: s:/a:/auto:, fold tests)


## Archive

- [x] Decider-true creation: Incident.Initial + Decide(state, cmd) receives initialState (Log New Incident)
- [x] Reverse-engineer emlang spec from the Incident domain (specs/helpdesk.em.yaml, lint green)
- [x] net11 preview + C# 15 native `union` types in the Incident domain
- [x] Upgrade Critter Stack: Marten 9 / Wolverine 6
- [x] Land in-flight net10 upgrade (TFMs, OpenApi swap, delete stray .sln)
- [x] Create ticket board (BOARD.md, Obsidian Kanban)


%% kanban:settings
```
{"kanban-plugin":"board"}
```
%%