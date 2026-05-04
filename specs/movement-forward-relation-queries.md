# Movement system: forward relation queries to stop the query-registration leak

> Status: **proposal**, not yet implemented. Audience: a future agent session
> that will make the change, plus the repo owner for review. This describes a
> targeted, self-contained fix to a confirmed performance bug. It is independent
> of (but complementary to) the import-UX redesign and the
> [`koota-relation-model.md`](./koota-relation-model.md) proposal.

## TL;DR

The Movement system issues **target-specific relation queries** every frame
(`world.QueryTraits(Position, SnapToX => changedEntityId)`, `Bisects => lineId`,
`Parallels => lineId`, `BoundingBoxOn => changedEntityId`). In Koota 0.6.x each
distinct `(relation, target)` pair is a **separate query registered permanently**
in the world's `queriesHashMap`, keyed by the **full entity value including
generation**. Generations increment on every destroy/respawn, so each scene
reload registers a fresh batch (~1356 in the sample data) of queries that never
recur and are never freed. Every subsequent `createEntity`/`addTrait` re-checks
new entities against **all** accumulated queries (`checkQueryWithRelations`),
making scene spawn **O(entities × accumulated_queries)** — quadratic across
imports. By the third import this is a ~22-second main-thread stall.

**Fix:** query each relation in the **forward (subject → target) direction** using
a **single, stable wildcard query per relation** (`rel.Wildcard()`), and read each
subject's target with `targetFor` and the relation value via the per-entity
accessor. This keeps the registered-query count **constant** across imports.

Do **not** rely on the per-import `world.reset()` workaround as the _only_ fix —
that is a separate lever (see [Relationship to other work](#relationship-to-other-work)).
This change removes the leak **at its source** in the system code.

## Why this is the right diagnosis (evidence)

- CPU trace of the 22 s stall: **100% Scripting**; top self-time
  `checkQueryWithRelations` 13.2 s, then `createEntity` 5.5 s, `addTrait` 2.9 s —
  all Koota relation/query machinery (not GPU, DOM, or React).
- Reproduced in isolation against real koota 0.6.6: a persistent world running the
  app's target-specific relation queries grows `queriesHashMap` by ~1356 per
  import with super-linear latency (64 → 175 → 329 → 532 → 847 → 1227 ms …).
- The forward/wildcard form (below) was probed under the same load: `queriesHashMap`
  stays at a small constant and latency is flat (~1–8 ms/import).

See repo memory "koota performance" for the recorded specifics.

## Root cause, precisely

Koota stores relations **subject → target** (`relationTargets[subjectEid]`). The
Movement system asks the **reverse** question — "which entities are snapped to
_this target_?" — by querying `rel => target`. Koota answers reverse queries by
registering a distinct query per concrete target. Target entity values embed a
generation that increments on respawn, so the keys never repeat across imports →
unbounded `queriesHashMap` growth. The forward direction needs **one** query per
relation regardless of how many targets exist.

## The pattern to adopt

Replace, for each relation `Rel` currently queried as `Rel => target`:

```fsharp
// BEFORE (reverse, target-keyed → leaks one query per target value per import):
world.QueryTraits(Position, SnapToX => changedEntityId).UpdateEachWith AlwaysTrack
<| fun ((pos, distance), _) -> pos.x <- newPos.x + distance.x
```

with a **single stable wildcard query** over all subjects of the relation, reading
each subject's target and value explicitly:

```fsharp
// AFTER (forward, wildcard → one stable query for the whole relation):
world.QueryTrait(Position, With(SnapToX.Wildcard())).UpdateEachWith AlwaysTrack
<| fun (pos, subject) ->
    match subject |> targetFor SnapToX with
    | Some target when target = changedEntityId ->          // membership filter (see below)
        match subject |> get (SnapToX => target) with
        | Some distance ->
            let tpos = target |> get Position |> Option.get  // or read via the query
            pos.x <- tpos.x + distance.x
        | None -> ()
    | _ -> ()
```

Key API facts (verified in `src/Wilnaatahl.Core/ECS`):

- `IRelation.Wildcard(): ITagTrait` exists (`Types.fs:170`); use it inside
  `With(...)` to match all subjects of the relation.
- `targetFor : IRelation -> EntityId -> EntityId option` (`ECS.fs:151`) is an
  **O(1)** per-entity read for exclusive relations — no query registered.
- `targetsFor : IRelation -> EntityId -> EntityId[]` (`ECS.fs:167`) returns all
  targets (use for the non-exclusive `BoundingBoxOn`).
- `targetWithValueFor` (`ECS.fs:155`) returns `(target, value)` in one call and is
  the cleanest accessor when you need both.
- **Do not** read a relation value through the query's value tuple for the pair
  itself — per repo memory, a relation pair in `updateEach` yields an empty/agnostic
  state. Read the value via `get (rel => target)` / `targetWithValueFor` **after**
  resolving the target with `targetFor`. (`get (rel => target)` does **not**
  register a query; only `query/QueryTrait(... rel => target ...)` does.)

## Per-operation mapping (current → proposed)

All references are to `src/Wilnaatahl.Core/Systems/Movement.fs`.

1. **`moveSnappedPoints` (SnapToX/Y/Z), lines ~15–22.**
   Currently three `QueryTraits(Position, SnapTo* => changedEntityId)` calls.
   Proposed: three wildcard queries `QueryTrait(Position, With(SnapToX.Wildcard()))`
   (and Y, Z), each iterating all snap subjects, using `targetFor SnapToX` to get
   the target and `get (SnapToX => target)` for the offset. Filter to the changed
   target via the `changedSet` membership test (below) to preserve the change-driven
   early-out.

2. **`moveBoundingBoxes` (BoundingBoxOn), lines ~26–64.**
   Currently `QueryTrait(Size, With(BoundingBoxOn => changedEntityId))` to find the
   box containing the changed entity. Proposed: iterate boxes via
   `QueryTrait(Size, With(BoundingBoxOn.Wildcard()))`; for each box, `targetsFor
BoundingBoxOn` gives its members. Process a box only if `changedSet` intersects
   its members. (Note the existing in-method `boxId |> targetsFor BoundingBoxOn` at
   line ~40 is already forward and fine — keep it.) `BoundingBoxOn` is
   **non-exclusive**, so use `targetsFor`, not `targetFor`.

3. **`moveLineDependants` (Bisects, Parallels), lines ~90–124.**
   - `EndpointOf` (line ~91): already forward via `targetFor EndpointOf` — keep.
   - `Bisects => lineId` (line ~96): replace with
     `QueryTrait(Position, With(Bisects.Wildcard()))`, filtering subjects whose
     `targetFor Bisects = Some lineId`.
   - `Parallels => lineId` (line ~105): replace with
     `QueryTrait(... With(Parallels.Wildcard()))`, filtering subjects whose
     `targetFor Parallels = Some lineId`, reading the offset via
     `targetWithValueFor Parallels`.

Also audit `src/Wilnaatahl.Core/Entities/BoundingBox.fs` (`updateCorners` uses
`CornerOf => boxId`) and any other `=> entityId` query sites that run per-frame.
`CornerOf => boxId` is invoked during movement, so it is part of the same leak and
should get the same forward treatment (iterate `CornerOf.Wildcard()` corners,
filter by `targetFor CornerOf = boxId`). One-shot setup-time `=> target` queries
(spawn/layout) are far less harmful but recur per import too; prefer converting any
that are cheap to convert, and note the rest.

## Preserving the change-driven fixpoint

The current loop only touches dependents of entities that moved, and iterates to a
fixpoint (`Movement.fs:128–155`). Preserve that behavior without target-keyed
queries:

- Keep the module-level `movementTracker = createChanged()` (`Runner.fs:31`). It is
  a **single stable tracker**, not part of the leak — do not remove it.
- Each iteration, materialize the set of changed entity IDs from the tracker query
  (the loop already enumerates `(Position, Changed <=> [Position])` results) into a
  `changedSet: Set<EntityId>` (or a `HashSet`).
- In each forward wildcard pass, process a subject only when its resolved target is
  in `changedSet`. This is O(relation-subjects) per iteration (bounded by scene
  size, ~170–340), with a constant number of registered queries.

Simpler alternative (only if the `changedSet` bookkeeping proves awkward):
**unconditional relaxation** — every iteration, recompute all snapped/bisect/
parallel/box positions from their targets, looping to a fixpoint. Cleaner but does
work even at rest. For a few-hundred-entity scene this is cheap; choose it only if
profiling shows the early-out doesn't matter. Default to preserving the early-out
via `changedSet`.

## Explicitly rejected alternative

**Manual reverse index** (store a list of subject `EntityId`s on each target):
gives O(matches) reverse lookups but reintroduces stale-`EntityId`-across-frames
hazards (see the `FamilyMember` "must be ephemeral" warning in
`Entities/Connectors.fs`), manual add/remove on every relation change, and teardown
bookkeeping. The wildcard-forward approach needs no extra state. Do not build a
manual reverse index.

## Acceptance criteria

- No `world.QueryTrait*`/`world.Query` call anywhere on a per-frame path passes a
  concrete `rel => entityId` pair. Per-frame relation queries use `rel.Wildcard()`
  and resolve targets with `targetFor`/`targetsFor`.
- Behavior is unchanged: snapping, bounding-box corner tracking, bisect, and
  parallel-line offsets render identically; undo/redo and dragging still work.
- **Regression guard:** add a test that runs N spawn/destroy (scene swap) cycles
  and asserts the world's registered-query count (white-box: `queriesHashMap.size`
  via the wrapper, or a latency/“entity-spawn cost” proxy) stays **bounded** —
  i.e. does not grow with the number of imports. This pins the fix against future
  regressions and against a future Koota change.
- Existing ECS tests (portable mock + Koota) and the F# suite pass.

## Validation steps (for the implementing agent)

1. Make the Movement (and `BoundingBox.updateCorners`/`CornerOf`) changes.
2. `dotnet test` (mock + non-ECS F#) and `npm run test:koota` (Koota-backed ECS).
3. `npm run build` (Fable → TS → Vite) — Fable can emit invalid TS for code that
   compiles under `dotnet test`, so this is mandatory.
4. `npm run coverage:check`.
5. Manual repro of the original bug: load a file, then re-import the same file 3×;
   confirm no progressive slowdown and no main-thread stall.
6. Optionally, confirm the leak is gone with the standalone Node probe in
   [Appendix: standalone query-growth probe](#appendix-standalone-query-growth-probe):
   it should print a **constant** `queriesHashMap` size and flat latency for the
   forward/wildcard form, versus growth for the old `=> target` form.

## Appendix: standalone query-growth probe

This is the self-contained reproduction used to diagnose the bug and verify the
fix. It does **not** depend on the app — it models the relevant relations directly
against the installed `koota` and prints the world's registered-query count per
simulated import. Run it from the repo root (so `koota` resolves) with
`node probe.mjs`. The **old** (reverse, `rel(target)`) movement form makes
`queriesHashMap` grow by a fixed amount each round; the **new** (wildcard +
`targetFor`) form keeps it constant.

```js
// probe.mjs — run with: node probe.mjs   (from the repo root)
import { createWorld, trait, relation } from "koota";
import { $internal } from "koota";

const Position = trait({ x: 0, y: 0, z: 0 });
const Size = trait({ x: 1, y: 1, z: 1 });
const SnapToX = relation({ exclusive: true, store: { x: 0 } });
const Parallels = relation({ exclusive: true, store: { offset: 0 } });
const BoundingBoxOn = relation(); // non-exclusive

const world = createWorld();
const ctx = world[$internal];

function spawnScene(n) {
  const nodes = [];
  for (let i = 0; i < n; i++) nodes.push(world.spawn(Position, Size));
  const lines = [];
  for (let i = 1; i < n; i++) {
    const line = world.spawn(Position);
    line.add(SnapToX(nodes[i], { x: 0.1 }));
    line.add(Parallels(nodes[i - 1], { offset: 0.3 }));
    lines.push(line);
  }
  return [...nodes, ...lines];
}

// Toggle this to compare the two strategies.
const FORWARD = true;

function movement(ents) {
  if (FORWARD) {
    // NEW: one stable wildcard query per relation; resolve target per subject.
    for (const subject of world.query(SnapToX("*"))) {
      const target = subject.targetFor(SnapToX);
      if (target === undefined) continue;
      const dist = subject.get(SnapToX(target));
      const tp = target.get(Position);
      const p = subject.get(Position);
      subject.set(Position, { x: tp.x + dist.x, y: p.y, z: p.z });
    }
    for (const subject of world.query(Parallels("*"))) {
      subject.targetFor(Parallels); // O(1) read, no query registered
    }
    for (const box of world.query(BoundingBoxOn("*"))) {
      box.targetsFor(BoundingBoxOn); // members, no per-target query
    }
  } else {
    // OLD: target-specific queries — registers one query per target value.
    for (const e of ents) {
      world.query(Position, SnapToX(e));
      world.query(Parallels(e));
      world.query(Size, BoundingBoxOn(e));
    }
  }
}

const N = 170;
for (let round = 0; round < 6; round++) {
  const t0 = performance.now();
  const ents = spawnScene(N);
  for (let f = 0; f < 3; f++) movement(ents);
  for (const e of [...world.entities]) if (world.has(e)) e.destroy();
  const dt = performance.now() - t0;
  console.log(
    `round ${round}: ${dt.toFixed(0).padStart(6)} ms | queriesHashMap=${ctx.queriesHashMap.size}`
  );
}
```

Expected output: with `FORWARD = true`, `queriesHashMap` is a small constant and
latency is flat; with `FORWARD = false`, `queriesHashMap` grows by a fixed amount
each round and latency climbs super-linearly. `$internal` and `queriesHashMap` are
Koota-internal and version-dependent — this probe is a diagnostic aid, not a
committed test; the committed regression guard is the one in
[Acceptance criteria](#acceptance-criteria).

## Relationship to other work

- **`world.reset()`-per-import** (discussed in the import-UX design notes) also
  cures the symptom by clearing `queriesHashMap` each load. It is a coarser,
  lifecycle-level lever. This proposal fixes the **source** so the system is
  correct even on a long-lived world that is never reset. The two are compatible;
  this one should be preferred as the durable fix, with `reset()` optional defense
  in depth.
- **`koota-relation-model.md`** proposes realigning the F# relation model so a
  relation is a filter + keyed value store (never a value-slot trait). If that
  lands first, the accessors named here (`targetFor`, `get (rel => target)`) may be
  renamed/reshaped; the **forward-query principle in this doc is unchanged** —
  adapt the call syntax to whatever that proposal settles on.
- The `<Canvas>` remount-per-import (separate, non-leaking issue confirmed via
  browser `__diag()`) is addressed by the overlay redesign, not here.
