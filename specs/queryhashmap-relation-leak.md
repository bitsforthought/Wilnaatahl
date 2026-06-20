# Unbounded `queriesHashMap` growth for relation queries with a concrete target

> Issue report for Koota maintainers. Tested against Koota 0.6.6.

## Summary

A query whose parameters include a relation pair with a **concrete** target
(e.g. `world.query(Position, SnapTo(targetEntity))`) is cached under a key that
embeds the target entity's runtime value (id **and** generation). This mints a
new persistent `QueryInstance` for every distinct target value ever queried, and
the instance is never evicted — not when the target is destroyed, not ever
(short of a `world` reset). The instances accumulate in `queriesHashMap`, and
each one is also re-checked on every mutation of the relation, so relation writes
slow down as more targets are queried.

This only affects the **concrete-target, multi-parameter** form. The single-pair
form `world.query(SnapTo(target))` bypasses the cache and answers from the
relation's reverse index — `relationSourcesByTarget`, the source-list-per-target
that Koota already maintains. The wildcard form `world.query(Position,
SnapTo('*'))` encodes the target as a constant, not a per-entity value. Neither
leaks.

## Where it comes from

`createQueryHash` folds the target's numeric value into the persistent key:

```js
// packages/core/src/query/utils/create-query-hash.ts
const target = param.target;
const targetId = typeof target === "number" ? target : -1;
sortBuf[cursor2++] = relationId * RELATION_FACTOR + targetId + RELATION_OFFSET;
```

`targetId` is the full entity value, including generation bits. So every distinct
target queried — whether many entities alive at once, or one entity id reused
across destroy/respawn (each respawn increments its generation) — produces a
distinct key, and therefore a distinct cached instance. Each instance is then
wired into structures that are only cleared on `world` reset:

- `queriesHashMap` — holds the instance and its maintained entity `SparseSet`.
- the relation trait's `relationQueries` set — **iterated in full on every
  relation add/remove** (`updateQueriesForRelationChange`), so each surviving
  instance adds fixed cost to every subsequent relation mutation.
- `queryInstances[id]` — indexed by a monotonic id that is never reclaimed.

Destroyed targets are not even a special case that gets cleaned up: entity
id+generation values are monotonic, so a destroyed value is never reissued and
its query can never match again, yet the entry is still held and still walked on
every relation mutation.

## A motivating scenario

This shows up in graph visualizations where some objects follow the motion of
others — connectors tracking the nodes they join, labels pinned to shapes, or
nodes positioned relative to a parent. The followers are the relation's
**subjects** and the objects they track are its **targets**. A system reacts to
motion by asking, for one object that just moved, which subjects are bound to it:

```js
const SnapTo = relation(); // a subject "snaps to" (follows) a target object

// When `moved` changes position, reposition everything snapped to it.
function onMoved(world, moved) {
  for (const subject of world.query(Position, SnapTo(moved))) {
    // ...update `subject` relative to `moved`...
  }
}
```

`moved` is a concrete target, so each distinct value passed to `onMoved`
registers a permanent query. Over a session every node can be the one that
moves, so the system queries a large set of distinct targets; and editing the
graph spawns and destroys nodes, recycling entity ids with fresh generations, so
even the "same" node yields new target values over time. The set of distinct
targets queried — and with it the cache — grows without bound.

## Minimal repro

```js
// Uses the public API only; `$internal` is read solely to observe the cache size.
import { createWorld, trait, relation, $internal } from "koota";

const world = createWorld();
const Position = trait({ x: 0, y: 0 });
const SnapTo = relation();

const size = () => world[$internal].queriesHashMap.size;

// A source that snaps to a series of distinct targets, each queried once.
const source = world.spawn(Position);

for (let i = 0; i < 5000; i++) {
  const target = world.spawn(Position); // a fresh target entity each iteration

  source.add(SnapTo(target));
  // The multi-parameter, concrete-target form: this is what gets cached.
  world.query(Position, SnapTo(target));
  source.remove(SnapTo(target));
}

console.log(size()); // 5000 — one entry per distinct target, none reclaimed
```

The cache grows by one entry per distinct target value and never shrinks. The
same entries also remain in the relation's `relationQueries` set, so the cost of
`source.add(SnapTo(...))` / `source.remove(...)` rises as more targets are seen.
Destroying and respawning targets does not help — each respawn produces a fresh
target value (new generation), which is just another distinct key.

For contrast, neither of these writes a per-entity value into a cache key, so
neither leaks:

```js
world.query(SnapTo(target)); // single concrete pair → bypasses the cache; answers from relationSourcesByTarget
world.query(Position, SnapTo("*")); // wildcard target → encoded as a constant; one stable entry
```

## Environment

- koota: 0.6.6
- Node: v23.11.0
