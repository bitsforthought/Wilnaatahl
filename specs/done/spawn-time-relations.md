# Spawn-time relations

> Status: **implemented**. Originally a proposal for two audiences — the repo owner
> (review/tweak the design) and a future agent session (implement it) — proposed as
> a follow-up within the current relations PR, now delivered.

## Motivation

Relations can currently only be attached to an entity **after** it is spawned. A
relation is not expressible as a `TraitSpec`, so every caller that wants a
newly-spawned entity to start life related to something must spawn first and
`addRelation` second:

```fsharp
// src/Wilnaatahl.Core/Entities/People.fs
let nodeId =
    world.Spawn(PersonRef.Val person, Position.Val zeroPosition, Size.Val nodeSize)
nodeId |> addRelation RenderedIn wilp

// src/Wilnaatahl.Core/Systems/Dragging.fs
let dragEntity = world.Spawn()
dragEntity |> addRelationWith Dragging nodeEntity origin
```

The two-step shape is a small papercut, but it is repeated at every relation-at-
birth site, it leaves the entity momentarily in a half-initialised state, and — for
the `Dragging` case — it spawns a bare entity whose _only_ purpose is to carry a
relation, which reads awkwardly. We want:

```fsharp
let nodeId =
    world.Spawn(PersonRef.Val person, Position.Val zeroPosition, Size.Val nodeSize, RenderedIn.ToTarget wilp)

let dragEntity = world.Spawn(Dragging.ToTargetWith(nodeEntity, origin))
```

**This is not a Koota limitation.** Koota's `world.spawn(...traits:
ConfigurableTrait[])` already accepts relation **pairs** — `rel(target)` (tag) and
`rel(target, value)` (value) are `ConfigurableTrait`s (see
`node_modules/koota/dist/index.d.ts` `spawn` and the `OrderedChildren` example, and
`types-*.d.ts` `spawn(...traits: ConfigurableTrait[])`). The Koota wrapper already
knows how to build those pairs at the query/entity-op boundary
(`toKootaRelation(rel)(target)`), and the .NET mock already implements
`addRelation`/`addRelationWith`. The only thing missing is a way to **express** a
relation initializer in the F# type that `Spawn` consumes: `TraitSpec` models a
trait, and nothing else.

## Current shape (what this expands)

`TraitSpec` (`src/Wilnaatahl.Core/ECS/Types.fs`) is a private two-case union with a
`Map` so it can be pattern-matched from the wrapper and mock without exposing its
cases:

```fsharp
type TraitSpec = // NOTE: TypeScriptTaggedUnion breaks the generated TS for functions returning a TraitSpec.
    private
    | Tag of ITagTrait
    | Val of (ITrait * obj) // types erased for Spawn's signature.

    static member Map fTag fValue config =
        match config with
        | Tag t -> fTag t
        | Val v -> fValue v
```

Construction is via extension members (`ECS.fs`): `ITagTrait.Tag()` → `Tag this`,
`IValueTrait<'T>.Val(value)` → `Val(this, value :> obj)`. `Spawn`'s signature is
`abstract Spawn: [<ParamArray>] traits: TraitSpec[] -> EntityId`.

## Proposed F# API

### Rename `TraitSpec` → `SpawnSpec`

Once it can carry a relation, "trait spec" is a misnomer — the type is "the
initializers you hand to `Spawn`". Rename to **`SpawnSpec`** (reads as
`world.Spawn(PersonRef.Val person, RenderedIn.ToTarget wilp)`). (`EntitySpec` / `InitSpec`
are reasonable alternatives; `SpawnSpec` ties the name to its single consumer.)
Keep it a **plain** DU with a `Map` — the existing `TypeScriptTaggedUnion` caveat
still applies.

### Add relation cases

```fsharp
type SpawnSpec =
    private
    | Tag of ITagTrait
    | Val of (ITrait * obj)
    // A relation the new entity acts as the SUBJECT of. Types are erased for
    // Spawn's signature, exactly as Val does.
    | TagRel of (IRelation * EntityId)          // relate to target, no value
    | ValRel of (IRelation * EntityId * obj)    // relate to target, carrying a value

    static member Map fTag fValue fTagRel fValRel config =
        match config with
        | Tag t -> fTag t
        | Val v -> fValue v
        | TagRel r -> fTagRel r
        | ValRel r -> fValRel r
```

Erasing the relation to the non-generic `IRelation` (and the value to `obj`)
mirrors the existing `Val` case and keeps `Spawn : SpawnSpec[] -> EntityId`
monomorphic. The wrapper/mock recover the concrete relation via the same downcast
they already use for entity ops (`relation :?> ITestRelation` in the mock;
`toKootaRelation` in the wrapper).

### Construction members

Mirror the existing `addRelation` / `addRelationWith` split (default value vs.
explicit value), so the spec constructors read the same way the imperative ops do:

```fsharp
type IRelation<'T, 'TMutable> with
    /// Spawn spec: the new entity relates to `target` via this relation, taking the
    /// relation's schema default value (for a tag relation, no value).
    member this.ToTarget(target: EntityId) : SpawnSpec = ...        // Tag/TagRel or default-valued ValRel
    /// Spawn spec: the new entity relates to `target` carrying `value`.
    member this.ToTargetWith(target: EntityId, value: 'T) : SpawnSpec = ValRel(this, target, value :> obj)
```

Naming is open for review. `.ToTarget` / `.ToTargetWith` mirrors `addRelation` / `addRelationWith`
and reads naturally at the call site (`RenderedIn.ToTarget wilp`, `Dragging.ToTargetWith(node,
origin)`). The relation redesign deliberately dropped the old `=>`-produces-a-trait
operator (see `koota-relation-model.md`), so this proposal does **not** reintroduce
`=>`; a relation initializer is a distinct concept from a relation filter.

Whether `.ToTarget` on a **tag** relation emits `TagRel` and on a **value** relation emits
a default-valued `ValRel` is an implementation detail of the members; both map to
"relate, no explicit value". Keep two `.ToTarget`/`.ToTargetWith` names rather than an
arity-overloaded `.ToTarget` — overloads are discouraged in this codebase (`fsharp-style`).

## Migration sketch (call sites)

- `People.fs` `spawnTreeNode`: fold `addRelation RenderedIn wilp` into the `Spawn`
  call as `RenderedIn.ToTarget wilp`.
- `Dragging.fs` `handleDragStart`: replace `world.Spawn()` + `addRelationWith
Dragging nodeEntity origin` with `world.Spawn(Dragging.ToTargetWith(nodeEntity, origin))`.
- No other call sites spawn-then-relate today; `addRelation`/`addRelationWith`
  remain for relating an entity that already exists (the common case), so they are
  **not** removed.

## Wrapper / mock impact

- **`kootaWrapper.ts`**: the `Spawn` unwrap (`unwrapTraitSpec` → rename to
  `unwrapSpawnSpec`) gains two arms passed to `SpawnSpec_Map`:
  - `TagRel(rel, target)` → `toKootaRelation(rel)(target as Entity)`
  - `ValRel(rel, target, value)` → `toKootaRelation(rel)(target as Entity, value)`
    (the params form Koota requires for a value pair at spawn/add).
    Both are `ConfigurableTrait`s that flow straight into `this.world.spawn(...)`.
    Update the `TraitSpec_$union as TraitSpec` / `TraitSpec_Map` imports to the
    `SpawnSpec_*` names.
- **`TestECS.fs`**: `spawn` (which folds `TraitSpec.Map addTagTrait addValueTrait`
  over each spec) gains `addTagRel (rel, target) = world |> addRelation rel target
entity` and `addValRel (rel, target, value) = world |> addRelationWith rel target
(unbox value) entity`, reusing the relation ops that already exist. The value
  unbox mirrors `addValueTrait`'s existing `obj`-erasure handling.

## New tests (TDD, portable across both backends)

Add to `RelationTests.fs` (so they run on the .NET mock and real Koota via Fable):

- Spawn with a **tag** relation pair → the entity is a subject of the relation
  (`hasRelation` / `targetFor` / a `Related` query all see it).
- Spawn with a **value** relation pair → the relation is present **and** carries the
  supplied value (`getRelationValue` returns it), distinguishing `.ToTargetWith` from a
  default-valued `.ToTarget`.
- Spawn with a value relation via `.ToTarget` (no value) → present with the schema
  default value.
- Spawn with an **exclusive** relation pair → single target, exclusivity honoured.
- Spawn **mixing** traits and relation specs in one call → all applied.

## Validation (when implemented)

- `dotnet test` (mock) and `npm run test:koota` (real Koota via Fable) — the
  portable conformance suite must stay green on both; the new spawn tests must pass
  identically on each backend.
- `npm run build` (Fable → tsc → vite) — the real gate; Fable can emit invalid TS
  from F# that `dotnet test` accepts. Confirm the renamed `SpawnSpec_*` exports
  resolve in `kootaWrapper.ts`.
- `npm run coverage:check` — no regression below baseline.
