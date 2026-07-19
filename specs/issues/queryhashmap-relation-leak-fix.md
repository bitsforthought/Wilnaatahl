# Fix proposal: drop per-target query instances for concrete-target relation queries

> PR proposal for Koota. Targets Koota 0.6.6 / `main`.

## Problem recap

`world.query(Position, SnapTo(target))` — a relation pair with a **concrete**
target alongside other parameters — is cached in `queriesHashMap` under a key
that embeds the target entity's runtime value (id + generation). Every distinct
target queried mints a new persistent `QueryInstance` that is never evicted and
is re-checked on every relation mutation. The result is unbounded growth of
`queriesHashMap` / `queryInstances` / the relation's `relationQueries` set, plus
relation writes that slow down as more targets are seen.

The single-pair form (`world.query(SnapTo(target))`) already avoids this: it
bypasses the cache and reads the reverse index via `getEntitiesWithRelationTo`.
This proposal extends that approach to the multi-parameter case.

## Fix (1): reverse-index intersection

A per-target `QueryInstance` keeps an incrementally-maintained `SparseSet`, so a
read is `O(result)` rather than a recomputation. The catch is that this payoff is
workload-dependent: it only beats recomputing from the reverse index when
`result` is much smaller than the target's source set. Whether or not that holds
for a given query, the instance still carries a fixed, permanent cost — it is
registered in `relationQueries`, re-checked on every relation mutation, and never
reclaimed. That per-target registration is the sole cause of the growth, which is
what makes replacing it worthwhile.

The fix is to stop persisting an instance per concrete target. Decompose the query into:

- a **target-independent base query** — the relation-present (wildcard) shape plus
  the other parameters (`Position`, modifiers, …) — cached normally by
  `queriesHashMap`; and
- a **run-time intersection** with the relation's existing
  `relationSourcesByTarget[target]` bucket (already maintained for every
  relation; see `packages/core/src/relation/relation.ts`).

At call time:

```
base    = cached query for (Position, <relation present>, ...)   // O(1) lookup
sources = getEntitiesWithRelationTo(relation, target)            // O(1) + O(|sources|)
result  = sources.filter(e => base.entities.has(e))             // O(|sources|)
```

### Complexity

|                           | Find instance    | Read (per call)          | Per relation mutation                     | Memory                      |
| ------------------------- | ---------------- | ------------------------ | ----------------------------------------- | --------------------------- |
| Today (cached per target) | O(1)             | O(\|result\|)            | **O(#concrete-target queries ever made)** | **unbounded**               |
| Fix (1)                   | O(1) base lookup | O(\|sources of target\|) | O(1)                                      | bounded by query **shapes** |

`result ⊆ sources`, so the per-call read is the same order as today, larger only
by the sources that fail the other filters. In exchange, the per-mutation cost
stops scaling with target cardinality and the unbounded growth disappears:
`queriesHashMap` is bounded by the number of distinct query _shapes_, and
`relationQueries` holds only those target-independent base shapes.

This keeps behavior consistent with the existing single-pair path.

## Fix (2, optional): version-guarded memo for hot, high-in-degree targets

The one workload where (1) is slower than today: a target with a large source
set, queried every frame, whose result stays much smaller than the bucket —
today reads `O(result)`, (1) re-intersects `O(sources)` each call.

To recover `O(result)` reads there, memoize the materialized result per
`(relation, target)` behind a version guard:

- invalidate when the base query's `version` changes (already bumped on
  add/remove) **or** when the target's source bucket changes; and
- drop the memo entry when the target entity is destroyed.

The memo is consulted _outside_ the per-mutation `relationQueries` loop, so it
never reintroduces the per-mutation scaling. Memory is bounded by live, hot
`(relation, target)` pairs and reclaimed on destruction; cold/ephemeral targets
fall back to (1) and store nothing.

## Rollout

- (1) alone fixes both the unbounded growth and the per-mutation scaling and is
  the smaller change. Land it first.
- (2) is a follow-up that closes the high-in-degree hot-read gap; only worth it
  if a benchmark shows the regression matters.

## Testing & benchmarks

- **Regression test for the leak**: query N distinct concrete targets in a loop
  and assert `queriesHashMap.size` stays flat.
- **No behavioral regression**: the existing relation/query/tracking suites
  should pass unchanged, since results are identical to the single-pair path.
- **Benchmarks** (`AGENTS.md`): compare before/after on the relation suites, e.g.
  `pnpm bench "@relation"`, and add a case that hammers concrete-target queries
  across many targets to capture the per-mutation improvement.
