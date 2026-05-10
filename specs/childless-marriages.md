# Modeling marriages without children — design exploration

## Problem

Today the family tree only knows about **co-parent** pairs, and only because
each `Person` carries an optional `CoParentRelationship` pointing at their two
parents. A marriage is therefore visible in the model **only as a side effect
of having produced a recorded child**. We want the model to be able to
represent marriages that have produced no children (whether by choice,
infertility, early death, or simply because the children aren't recorded).

This document proposes several options for extending the data model and walks
the codebase to identify every system that would have to change to support the
new case. It deliberately stops short of recommending an option or scheduling
implementation work — that is the next step.

## Current state (relevant code)

- `src/Wilnaatahl.Core/Model.fs`
  - `type CoParentRelationship = { Mother: PersonId; Father: PersonId }` —
    the only "pair of adults" type in the model.
  - `createFamilyGraph (peopleAndParents: seq<Person * CoParentRelationship option>)`
    derives the set of co-parents purely from the children's parent pointers
    (`peopleAndParents |> Seq.choose (fun (_, parents) -> parents) |> Set.ofSeq`).
  - `WilpTree.Family.CoParentsAndDescendants : Map<PersonId, WilpTree seq>` —
    a Wilp parent's co-parents are only discovered by walking down to a child
    that lists them as a parent.
  - `buildWilpTree` only attaches a co-parent if at least one child references
    the pair via `CoParentRelationship`.
  - `visitWilpForest` calls `visitFamily` only for `Family` nodes (i.e. nodes
    that have at least one descendant subtree). A childless marriage cannot be
    surfaced here today.
  - `module Initial` — hardcoded seed data, also only links spouses through
    children (`parents mother father` only attached to children).
- `src/Wilnaatahl.Core/ViewModel/Scene.fs`
  - `extractFamilies` iterates `coparents familyGraph`, intersects each pair's
    children, and **silently drops** any pair where `Set.intersect` is empty
    (`(_ :: _ as children) -> yield ...; | _ -> ()`).
  - `layoutGraph` calls `visitWilpForest` and relies on the `visitFamily`
    callback to position co-parents — i.e. it only ever lays out a co-parent
    if a child anchors them.
  - `attachParentsToDescendants` assumes a non-empty
    `unattachedChildBoxes` and divides families into a "left half" / "right
    half" around the descendants box.
- `src/Wilnaatahl.Core/Entities/Connectors.fs`
  - `spawnAllConnectors` consumes `RenderedFamily` records (which always have
    a non-empty `Children` list today) and unconditionally builds: hidden
    parent line, two parallel parent lines, bisecting node, child bounding
    box, branch elbow, branch line, and per-child elbows + lines.
- `src/Wilnaatahl.Core/Entities/People.fs`
  - `spawnTreeNode` is per-person and is invoked for every Person in the
    graph, so spouses are already spawned as nodes regardless of children.
- `src/Wilnaatahl.Core/Systems/Layout.fs`
  - Reads target positions from `Scene.layoutGraph` and writes them onto tree
    nodes; no per-relationship logic.
- `src/Wilnaatahl.Core/Systems/{Selection,Dragging,Movement,UndoRedo,Animation}.fs`
  - Operate on entities by trait (`PersonRef`, `Position`, `Selected`,
    `TargetPosition`, etc.) and have no knowledge of marriage/co-parent
    relationships.
- `src/ecs/*` and `src/react-components/*`
  - Drive entirely off ECS traits. No direct dependency on
    `CoParentRelationship`. They will pick up the new behaviour
    transparently provided that connectors and tree nodes are spawned with
    the right traits.

## Cross-cutting design questions (need answers regardless of option)

These questions are the ones that actually drive the look and feel of the
feature; the data-model options below are mostly mechanical once they are
answered.

1. **Naming and gender semantics.** `CoParentRelationship` uses
   `Mother`/`Father` because the dataset's matrilineal invariant uses those
   roles. A "marriage" relationship is a more general concept (and the Gitxsan
   matrilineal invariant doesn't say anything about who can marry whom); do
   we keep `Mother`/`Father` (Spouse roles inferred by Wilp/Shape) or
   introduce neutral spouse labels (`Spouse1`/`Spouse2`, or
   `WilpSpouse`/`OutsideSpouse`)? This decision propagates into types,
   layout (which spouse goes on the left), and any future import format.
2. **Cardinality.** Should a person be allowed to be in **multiple childless
   marriages** (e.g. successive marriages, none of which produced recorded
   children)? Today the model already supports a person having multiple
   _procreative_ coparents (e.g. Margaret has three in `Initial`); childless
   marriages should presumably follow the same rule.
3. **Marriage state.** Just "married" today, or do we need
   `MarriageStatus` (current / divorced / widowed / dissolved) and dates
   (`Married`, `Ended`)? Even if not modeled now, we should pick a name
   that doesn't paint us into a corner (`Marriage` vs `Union` vs
   `Partnership`).
4. **Visual treatment.** A childless marriage has the two parallel
   parent-to-coparent lines but no bisecting elbow / branch / children. Do
   we render:
   - exactly the same horizontal "spouse bar" with nothing hanging below
     (cleanest), or
   - a distinct symbol (e.g. an open elbow) so the user can tell at a glance
     "married, no children", or
   - a layout-only treatment (no extra glyph; the absence of descendants
     speaks for itself)?
5. **Layout placement.** Where does a childless co-parent sit relative to a
   Wilp parent's other (procreative) co-parents? Options:
   - left/right by some deterministic order (e.g. marriage date, then
     birth date, then spouse name);
   - always grouped after procreative co-parents (descendants take priority);
   - same comparator as procreative co-parents, with empty descendants treated
     as a zero-width child group.
6. **Authority.** If both an explicit marriage and a `CoParentRelationship`
   between the same pair exist, do we treat the marriage record as redundant,
   reject the data, or merge silently? (This question only applies to options
   that keep both representations alive — A and C below.)

## Data-model options

In all options, every Wilp tree node continues to be a `Person`; the change is
_only_ in how we describe pairs of adults. Code samples are illustrative and
intentionally minimal.

### Option A — Additive: explicit `Marriages` set alongside the existing parent links

Keep `peopleAndParents` exactly as it is, but add a second input sequence to
`createFamilyGraph` for marriages that aren't otherwise discoverable:

```fsharp
let createFamilyGraph
    (peopleAndParents: seq<Person * CoParentRelationship option>)
    (marriages:        seq<CoParentRelationship>)
    : FamilyGraph
```

`coparents graph` returns `derivedFromChildren ∪ explicitMarriages`. Internal
storage gains a `Marriages : Set<CoParentRelationship>` field (or simply
expands `CoParentRelationships`).

**Pros**

- Minimal diff: every consumer of `coparents graph` automatically sees childless
  marriages, and the type stays the same.
- Backward-compatible with `Initial.peopleAndParents`; only the
  `GraphViewFactory.LoadGraph()` call site needs updating.
- No renames; existing tests need only additive coverage.

**Cons**

- Reuses `Mother`/`Father` for relationships that have nothing to do with
  parenthood — naming becomes misleading.
- Two sources of truth for the same pair (the question 6 ambiguity).
- Doesn't gracefully extend to richer marriage metadata (status, dates) — any
  attempt would force `CoParentRelationship` to carry irrelevant fields.

### Option B — First-class `Marriage`, parents reference a marriage

Introduce a `Marriage` record with its own identity and have a child's parent
pointer reference a marriage rather than carrying the spouse pair inline:

```fsharp
type MarriageId = MarriageId of int

type Marriage = {
    Id: MarriageId
    WilpSpouse: PersonId      // or Spouse1
    OutsideSpouse: PersonId   // or Spouse2
    // future: Status, Dates
}

let createFamilyGraph
    (people:    seq<Person * MarriageId option>)   // child -> marriage
    (marriages: seq<Marriage>)
    : FamilyGraph
```

A childless marriage is just a `Marriage` that no one in `people` references.

**Pros**

- Single source of truth: a marriage exists iff there's a `Marriage` record.
- Naturally extensible (status, dates, certificates, notes).
- Removes the awkward `Mother`/`Father` overload for non-procreative pairs.
- Aligns with how genealogy formats (e.g. GEDCOM) model unions.

**Cons**

- Largest refactor of the four. Touches `Model.fs`, `Scene.fs`, every test
  fixture in `TestData.fs`/`SceneTests.fs`/`ModelTests.fs`, and the
  hardcoded `Initial.peopleAndParents` seed.
- Forces a stable id scheme for marriages (PersonId is already an int; we'd
  add another int namespace).
- The convenient `Mother`/`Father` matrilineal invariant has to be re-stated
  on `Marriage` (or inferred from the `Person.Wilp`/`Shape` pairing).

### Option C — Two parallel relationships: keep `CoParentRelationship`, add `MarriageRelationship`

Treat parenthood and marriage as distinct relations. Children still point at a
`CoParentRelationship`. A separate `MarriageRelationship` (structurally similar
but semantically independent) carries marriages that don't necessarily have
recorded children. Procreative marriages can either:

- (C1) appear in **both** sets, or
- (C2) appear only in `CoParentRelationship`s, with `MarriageRelationship`
  reserved for the childless case.

```fsharp
type MarriageRelationship = { Spouse1: PersonId; Spouse2: PersonId }

let createFamilyGraph
    (peopleAndParents: seq<Person * CoParentRelationship option>)
    (marriages:        seq<MarriageRelationship>)
    : FamilyGraph
```

**Pros**

- Clean conceptual split between "shared a child" and "married".
- Marriage type can grow (status, dates) without affecting `CoParentRelationship`.
- C2 in particular is the smallest possible new surface area.

**Cons**

- C1 has the same "two sources of truth" problem as Option A.
- C2 makes "is this pair married?" a function of two sets, which is easy to
  get wrong at call sites.
- Either variant duplicates the spouse-pair shape — every consumer that wants
  to draw a "spouse bar" must union over both sets.

### Option D — Restructure input around marriages

Flip the input shape from "person → parents" to "marriage → children":

```fsharp
type Marriage = {
    WilpSpouse: PersonId
    OutsideSpouse: PersonId
    Children: PersonId list   // possibly empty
}

let createFamilyGraph
    (people:    seq<Person>)
    (marriages: seq<Marriage>)
    : FamilyGraph
```

`Marriage.Children = []` is the childless case; everything else is a
procreative marriage.

**Pros**

- Single source of truth, and the data shape mirrors the visual: each
  marriage _is_ a horizontal spouse bar with zero or more children below.
- Eliminates the awkward `Person * CoParentRelationship option` tuple where
  the parents are stored on the child.

**Cons**

- Largest semantic change: every test fixture, the `Initial` seed, the
  whole `buildWilpTree` recursion, and any future import format have to be
  re-thought around marriage records.
- A `Person` can now be referenced from multiple marriages as a `Child`,
  but enforcing "exactly one marriage of origin per person" becomes a
  validation step rather than a type-level guarantee.

### Quick comparison

| Aspect                               | A (additive set) | B (Marriage id)            | C (parallel sets)        | D (marriage-centric)   |
| ------------------------------------ | ---------------- | -------------------------- | ------------------------ | ---------------------- |
| Extra concept introduced             | None             | `MarriageId` + `Marriage`  | `MarriageRelationship`   | `Marriage` w/ children |
| Single source of truth for marriages | No               | Yes                        | C1: no / C2: yes         | Yes                    |
| Future-proof for marriage metadata   | No               | Yes                        | Yes (on the new type)    | Yes                    |
| Diff size                            | Small            | Large                      | Small (C2) / Medium (C1) | Large                  |
| Renames needed                       | None             | Likely (`Mother`/`Father`) | Maybe (`Spouse1/2`)      | Likely                 |
| Input-format implication             | Add a section    | Add a section + ids        | Add a section            | Restructure entirely   |

## Gap analysis

This is what changes — or has to be re-examined — _no matter which option we
pick_. Per-option footprints differ in size; per-system footprints don't.

### Domain model (`src/Wilnaatahl.Core/Model.fs`)

- **`createFamilyGraph` input shape changes.** Every caller (`ViewModel.fs`,
  the test fixtures in `TestData.fs`, `ModelTests.fs`, `SceneTests.fs`) has to
  pass the new argument(s).
- **`coparents graph`** must return childless marriages too (or callers must
  switch to a new accessor like `marriages graph`). Current callers:
  `Scene.extractFamilies`, `ModelTests`.
- **`buildWilpTree`** currently discovers co-parents only by walking children.
  It must learn to attach a co-parent to a Wilp member when the only evidence
  for that pair is a marriage record — i.e. the marriage of a `Leaf` person
  must be detectable.
- **`Family.CoParentsAndDescendants : Map<PersonId, WilpTree seq>`** can hold
  empty seqs for childless co-parents, but several downstream code paths
  assume non-empty (see Layout / visitWilpForest below).
- **`WilpTree`** itself probably doesn't need a new case — a childless
  marriage just becomes a `Family` whose `CoParentsAndDescendants` value for
  that spouse is an empty sequence — but we should consider whether `Leaf`
  needs to grow to include "Leaf with a spouse".
- **`huwilpForests` / root discovery.** A Wilp member who has no parents and
  no children but _is_ in a recorded marriage is currently a `Leaf` root with
  no spouse rendered. Their spouse must now be reachable from the tree.

### Scene + layout (`src/Wilnaatahl.Core/ViewModel/Scene.fs`,

`LayoutUtils.fs`, `Systems/Layout.fs`)

- **`Scene.extractFamilies`** intersects the two parents' children and
  drops the pair when the intersection is empty. This filter has to relax —
  a `RenderedFamily` with an empty `Children` list must be allowed (and
  `RenderedFamily.Children` typed accordingly).
- **`Scene.layoutGraph` / `attachParentsToDescendants`** currently relies on
  every `Family` having at least one child, and divides co-parents into a
  "left half" / "right half" around a non-empty descendants box. This needs:
  - a code path that produces a parent + co-parent pair box with no
    descendants attached,
  - a defined slot for that pair within the parent's row of co-parents
    (see cross-cutting question 5).
- **`visitWilpForest`'s `sortAndProcessSortedChildGroups`** sorts groups by
  `childGroup1 |> Seq.head |> fst`, which fails on empty descendant
  sequences. The comparator (or input filtering) needs to handle empty
  groups, presumably by sorting on the co-parent itself.
- **`comparePeople`** is fine as is, but we need to decide what to compare on
  for the childless co-parent's "implied" position (probably the co-parent's
  own DoB/birth-order).

### ECS spawn (`src/Wilnaatahl.Core/Entities/{Connectors,People}.fs`)

- **`spawnAllConnectors`** unconditionally builds the elbow / branch / child
  scaffolding. It has to branch on `family.Children` being empty:
  - always spawn the hidden line + two parallel parent lines (the
    "spouse bar"),
  - skip the bisecting node, branch elbow, branch line, child bounding box,
    and per-child elbows/lines when there are no children.
- **`spawnTreeNode`** doesn't need to change — every Person is already
  spawned via `enumerateHuwilpToRender` returning all people. We should
  verify, though, that a non-Wilp spouse with no children still ends up in
  some Wilp's render set (currently `enumerateHuwilpToRender` returns
  `(wilp, allPeople)` for the single Wilp, so this works _only_ under that
  TODO; once multi-Wilp lands, the question recurs).

### ECS systems (`src/Wilnaatahl.Core/Systems/*.fs`)

- **Selection, Dragging, Movement, Animation, UndoRedo** are all
  trait-driven and have no relationship awareness. They should "just work"
  for childless marriages provided the connector entities are spawned
  correctly.

### Hardcoded seed and tests

- **`Initial.peopleAndParents`** is hardcoded sample data. To exercise the
  new path we need to add at least one childless marriage (and probably one
  Wilp-member-only-married-with-no-children case to stress the leaf-with-
  spouse situation). This is also a documentation surface: the comment
  block at the top of `module Initial` describes the matrilineal invariants
  and should be updated.
- **`tests/Wilnaatahl.Core.Tests/TestData.fs`** has `testPeopleAndParents`
  and `extendedFamily`. Both will need new fixtures (a childless marriage
  among existing people, and a Wilp-member leaf with a spouse).
- **Tests to add** (TDD, pre-implementation):
  - `ModelTests`: `coparents` includes childless marriages; `findChildren`
    is unaffected; `visitWilpForest` exposes childless co-parents under
    their Wilp parent in the order dictated by question 5.
  - `SceneTests`: `extractFamilies` yields a `RenderedFamily` with empty
    `Children` for a childless marriage; `layoutGraph` positions the
    childless co-parent at the specified slot and emits no child positions
    for that subtree.
  - New ECS test (in `tests/Wilnaatahl.ECS.Tests`) that
    `spawnAllConnectors` produces the spouse-bar Lines but no Elbow /
    branch / child Lines for a childless `RenderedFamily`.
- **Stable identity spec** (`specs/stable-tree-node-identity.md`) doesn't
  mention marriages directly but should be re-read once an option is
  picked, in case spouse identity affects the proposed key scheme.

### Generated code and React/Three layer

- **`src/generated/`** is regenerated by Fable; nothing to hand-edit.
- **React components** (`HuwilpGroup`, `TreeNodeMesh`, `LineMesh`,
  `ElbowSphereMesh`, `TreeScene`) read entities by trait
  (`PersonRef`, `Line`, `Elbow`, `Position`, `Selected`, `Hidden`, `Size`)
  and require no changes. The new "spouse bar" lines will appear simply by
  virtue of being spawned with the `Line` trait.
- **Palette** (`Palette.fs`) is per-person; unaffected.

### Future / out-of-scope adjacencies (worth flagging)

- **Import feature** (`specs/import-feature.md`): the eventual file format
  must encode marriages — including childless ones. Whichever option we
  pick here pins the shape of that part of the format.
- **Multi-Wilp rendering** (TODO in `Scene.enumerateHuwilpToRender` and
  `Connectors.spawnAllConnectors`): a childless cross-Wilp marriage is one
  of the simpler test cases for that feature; the design should keep that
  in mind.
- **Marriage metadata** (status, dates, divorces): not in scope, but
  options B, C, and D are the only ones that gracefully accommodate it
  later without a second migration.

## Open questions (must be answered before picking an option)

1. Naming: `Marriage` vs `Union` vs `Partnership`?
2. Should the spouse pair stay `Mother`/`Father`, or move to a neutral pair?
3. What's the layout slot for a childless co-parent (question 5 above)?
4. Is a distinct visual glyph wanted, or just absence of descendants
   (question 4 above)?
5. Are multiple childless marriages per person allowed?
6. If a marriage and a co-parent record exist for the same pair, what
   wins?
7. How important is forward compatibility with marriage metadata
   (status/dates) — important enough to pay the Option B/D refactor cost?

## Next step

Pick an option (and answer the open questions), then this document gets
re-opened in plan mode to break the chosen direction into TDD-ordered
implementation todos.
