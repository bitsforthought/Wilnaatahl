# Koota relation model

> Status: **proposal**, not yet implemented. Two audiences: the repo owner
> (review/tweak the design) and a future agent session (implement it). The
> interim guards described in [Interim state](#interim-state-what-this-replaces)
> are what this proposal is meant to undo.

## Motivation

Koota 0.6.x rewrote relations. A relation **pair** (`rel(target)`) is no longer
a `Trait` — it is an opaque, freshly-allocated handle. Our F# ECS interfaces
still model a pair _as_ an `ITrait` (`IRelation.WithTarget: EntityId -> 'TTrait`,
surfaced as the `rel => target` operator). That mismatch causes two problems:

1. **A footgun.** Because a pair looks like a value trait, it can be passed into
   the value channel of `QueryTrait`/`QueryTraits*`. Koota then either takes the
   _relation-only fast path_ (sole pair, no other filter → empty iterable state)
   or, for a non-exclusive value relation, surfaces a _target-agnostic array_
   instead of the per-target value. `UpdateEach` reads that broken state. See
   `kootaWrapper.ts` / `TestECS.fs` (`relationValueUpdateEachError`,
   `updateEachErrorFor`).
2. **Per-`=>` allocation.** Koota allocates a fresh pair on every `rel(target)`,
   so the wrapper cannot cache it and allocates a `WrappedRelationPair` object on
   every `=>` call (`fromKootaRelation.WithTarget` in `kootaWrapper.ts`). This is
   cheap (~1 small object) but happens in per-frame system loops (`Movement.fs`,
   `Dragging.fs`).

This proposal realigns the F# model with Koota's: **a relation is a filter and a
keyed value store, not a trait.** Reads/writes go through per-entity accessors
keyed by `(relation, target)`; queries use a relation **filter**, never a value
slot. This removes the footgun by construction (you cannot put a relation in a
value slot) and removes the per-pair wrapper allocation.

## Koota 0.6.x specifics to honour

- A pair is created by `relation(...)` returning `rel`, then `rel(target)`. The
  pair is opaque and freshly allocated each call (not cached). Source:
  `node_modules/koota/dist/types-*.d.ts` (`RelationPair`, `relationTargets`).
- `world.query(rel(target))` with **exactly one** concrete-target pair and no
  other params hits `createRelationOnlyQueryResult`; its `readEach`/`updateEach`/
  `useStores` call back with `[]`. Any second param, or a `*` wildcard target,
  avoids it. Source: `chunk-*.js` (`params.length === 1 && relationPair && typeof target === "number"`).
- `entity.get(rel(target))` returns the per-target scalar correctly for both
  exclusive and non-exclusive relations. **Query-state iteration is the only
  broken read path.** This is why the proposed accessors are `get`-based.
- One relation stores all of an entity's targets in `relationTargets[eid]`;
  non-exclusive relations scale without a trait-per-target explosion.

## Proposed F# API

The change is confined to the ECS interfaces (`ECS/Types.fs`), their convenience
modules (`ECS/ECS.fs`), the Koota wrapper, and the .NET mock. Systems and traits
change only at call sites.

### Relations are no longer `IRelation<'TTrait>`

Drop `WithTarget`/`Wildcard`-as-trait and the `=>` operator's "produce a trait"
contract. A relation becomes a first-class handle parameterised by its value
type (tag relations carry `unit`):

```fsharp
/// A relation between entities, optionally carrying a per-(subject,target) value.
type IRelation<'T, 'TMutable> =
    abstract IsExclusive: bool

/// A relation that carries no value (Koota storeless relation).
type ITagRelation = IRelation<unit, unit>
```

### Values via per-entity accessors (the `getRelationValue` family)

Reads/writes are keyed by `(relation, target)` and always go through `get`, the
one correct Koota path:

```fsharp
// in module Entity
val getRelationValue: IRelation<'T,'TMutable> -> target: EntityId -> EntityId -> 'T option
val setRelationValue: IRelation<'T,'TMutable> -> target: EntityId -> 'T -> EntityId -> unit
val addRelation:      IRelation<'T,'TMutable> -> target: EntityId -> EntityId -> unit
val removeRelation:   IRelation<'T,'TMutable> -> target: EntityId -> EntityId -> unit
val hasRelation:      IRelation<'T,'TMutable> -> target: EntityId -> EntityId -> bool
// navigation is unchanged in spirit:
val targetFor:  IRelation<'T,'TMutable> -> EntityId -> EntityId option
val targetsFor: IRelation<'T,'TMutable> -> EntityId -> EntityId[]
```

Naming: the value accessors are `getRelationValue`/`setRelationValue` (not bare
`getRelation`): they return the relation's **value**, not the relation or its
target — `targetFor`/`targetsFor` already cover navigation, and `getRelation`
alone would be ambiguous against them.

### Relations as query **filters**, never value slots

Querying _for_ a relation uses a dedicated operator; the value, if needed, is
read per entity inside the callback via `getRelationValue`. This promotes the
pattern the Movement and UndoRedo systems already use today — a
`Query(With(rel => target))` filter followed by a per-entity `get` — into a
first-class shape:

```fsharp
type QueryOperator =
    | ...
    /// Matches subjects related to the given target via the given relation.
    | Related of IRelation<'T,'TMutable> * EntityId
    /// Matches subjects related to any target via the given relation (wildcard).
    | RelatedToAny of IRelation<'T,'TMutable>
```

`QueryTrait`/`QueryTraits*` keep taking only genuine value traits, so a relation
**cannot** appear in a value slot — the footgun is unrepresentable. There is no
`QueryTraitWithRelation` overload explosion: the value comes from
`getRelationValue` in the callback, not from extra query arities.

### Migration sketch (call sites)

- `rel => target` as a trait argument to `add`/`remove`/`has`/`get`/`setValue`
  → `addRelation`/`removeRelation`/`hasRelation`/`getRelationValue`/
  `setRelationValue rel target`.
- `Query(With(rel => target))` → `Query(Related(rel, target))`.
- `QueryTrait(rel => target)` (Movement `Parallels`) → `Query(Related(Parallels,
lineId))` + `getRelationValue Parallels lineId` per entity.
- `QueryTraits(Position, SnapToX => id)` (Movement `moveSnappedPoints`) →
  `QueryTrait(Position, Related(SnapToX, id))` + read `SnapToX` value via
  `getRelationValue` (or fold the offset read into the callback).
- `rel.Wildcard()` → `RelatedToAny rel`.
- Relation definitions in `ConnectorTraits.fs`, `BoundingBox.fs`, `ViewTraits.fs`,
  `UndoRedo.fs` keep `valueRelation`/`tagRelation` constructors; only the trait
  parameterisation changes.

### Wrapper / mock impact

- `kootaWrapper.ts`: delete `WrappedRelationPair`, `isWrappedRelationPair`, the
  `WithTarget` allocation, and the `updateEachError` plumbing. `Related`/
  `RelatedToAny` unwrap to `rel(target)` / `rel("*")` at the query boundary;
  `getRelationValue` maps to `entity.get(rel(target))`. No per-`=>` allocation
  remains.
- `TestECS.fs`: delete `updateEachErrorFor` and the `relationOnly`/non-exclusive
  guards; model relations as a `(subject,target) -> value` map. Mock and Koota
  stay in lock-step via the existing portable conformance tests.

## Interim state (what this replaces)

Until this redesign lands, two **intentional, minimal** guards keep the current
trait-shaped model honest. They are deliberate stopgaps, to be **removed** when
this proposal is implemented:

1. **`UpdateEach` fail-fast on a relation value slot.** When a relation pair
   occupies a query's value slot — as the sole query trait, or for any
   non-exclusive relation — `UpdateEach` throws rather than surface a broken
   value. `ForEach` is unaffected (it reads each value via `get`). Parity is
   enforced in both `kootaWrapper.ts` and `TestECS.fs` and verified by
   `RelationTests`/`QueryTests`.
2. **Non-exclusive value relations restricted in practice.** They remain usable
   via `get` / `Query(With(rel => target))` / `targetsFor` (the UndoRedo
   `SnapshottedBy` snapshot stack genuinely needs many targets), but are blocked
   from the `UpdateEach` value-slot path by guard 1. Forbidding them at
   construction was rejected because it would break the undo/redo snapshot model.

This redesign makes both guards unnecessary: relations cannot occupy a value
slot, and non-exclusive value relations are read the only correct way
(`getRelationValue`), so they are fully and safely supported.

## Validation (when implemented)

- `dotnet test` (mock) and `npm run test:koota` (real Koota via Fable) — the
  portable conformance suite must stay green on both.
- `npm run build` (Fable → tsc → vite) — Fable can emit invalid TS from F# that
  compiles under `dotnet test`; the npm build is the real gate.
- `npm run coverage:check` — no regression below baseline.
- Headless drag smoke: spawn the scene, drive `handlePointerDown` →
  `handleDragStart` → `handleDrag` → `handleDragEnd` with `runSystems` per frame;
  confirm the dragged node's `Position` actually changes.
