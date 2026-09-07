---

kanban-plugin: board

---

## Backlog

- [ ] Spec transformer as interpreter (awaiting article reference)
- [ ] Wire Emlang.Generators: Helpdesk.Domain (Commands/Events/Errors/Decider.g.cs) + Decider.Impl + generated SpecTests (decide = Wolverine [AggregateHandler] Handle, evolve = Marten Apply)
- [ ] Rewire Helpdesk.Api endpoints onto the Decider (Critter shell)
- [ ] Blazor standalone SPA UI, dev auto-login (after Decider rewiring)


## Doing



## Done

- [x] state, ie emlang view, always as given in GWT, as per the decider pattern and kvissig.se
- [x] Decider-true creation: Incident.Initial + Decide(state, cmd) receives initialState (Log New Incident)
- [x] Reverse-engineer emlang spec from the Incident domain (specs/helpdesk.em.yaml, lint green)
- [x] net11 preview + C# 15 native `union` types in the Incident domain
- [x] Upgrade Critter Stack: Marten 9 / Wolverine 6
- [x] Land in-flight net10 upgrade (TFMs, OpenApi swap, delete stray .sln)
- [x] Create ticket board (BOARD.md, Obsidian Kanban)


## Archive





%% kanban:settings
```
{"kanban-plugin":"board"}
```
%%