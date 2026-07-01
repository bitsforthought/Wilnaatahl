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
new case. The four options and the gap analysis are preserved as a record of
the exploration; the **Decision** section below records the option that was
chosen and the answers to each design question, with reasoning.

## Decision

**Chosen option: Option B (modified) — first-class `Couple` type, parents
reference a couple by id.**

The "modifications" relative to the original Option B sketch are:

- The new type is named **`Couple`**, not `Marriage`, to be maximally
  inclusive (the tool is not in a position to assert which kinds of unions
  count as marriages).
- The two members of a `Couple` live in a single tuple-typed field named
  **`Members : PersonId * PersonId`**, rather than two named fields, to
  reflect the fact that the rest of the code already treats the pair
  symmetrically (see _How `Mother`/`Father` are used today_ below). A smart
  constructor canonicalizes the pair so `Couple.create a b` and
  `Couple.create b a` produce the same record.
- A single optional metadata field, `DateOfUnion`, is added on day one because
  it's needed for the layout sort order (see Q5 below). All other metadata
  (status, dissolution date, notes) is deferred.

### Planned shapes

```fsharp
#if FABLE_COMPILER
[<Erase>]
#endif
type CoupleId =
    | CoupleId of int
    member this.AsInt = let (CoupleId id) = this in id

type Couple = private {
    Id: CoupleId
    Members: PersonId * PersonId   // canonicalized: lower PersonId first
    DateOfUnion: DateOnly option
}

module Couple =
    /// Smart constructor: canonicalizes Members so (a,b) and (b,a) produce
    /// the same Couple value. Throws if a = b.
    val create : CoupleId -> PersonId -> PersonId -> DateOnly option -> Couple

// New createFamilyGraph signature
val createFamilyGraph :
    people:  seq<Person * CoupleId option> ->
    couples: seq<Couple> ->
    FamilyGraph

// Renamed accessor (was `coparents graph : Set<CoParentRelationship>`).
// Returns a seq, not a Set: uniqueness-by-CoupleId is already enforced at
// graph construction (see validation rule below), so callers don't need
// Set semantics for dedup. As Couple grows additional metadata fields, the
// structural comparison required by Set<Couple> would also become
// progressively more expensive for no benefit.
val couples : FamilyGraph -> Couple seq
```

Inside `WilpTree.Family`, the field
`CoParentsAndDescendants : Map<PersonId, WilpTree seq>` is **renamed** to
`PartnersAndDescendants` to reflect that the keys are now "the Wilp member's
partner in this Couple" — they may or may not also be co-parents. Childless
partners are represented by an entry whose value is the empty sequence.

### How `Mother`/`Father` are used today (background for the rename)

A code walk of every read site (`Model.fs:123-124,141,151`,
`Scene.fs:178-185`) confirms that `Mother` and `Father` are treated
symmetrically by every algorithm. Nothing branches on which of the two fields
a `PersonId` lives in; layout decisions about which spouse is the "Wilp
parent" come from `Person.Wilp` lookups, not from the field name. The
matrilineal invariant ("`Mother` is the Wilp member, `Father` is the
outsider") is asserted only in a comment on `module Initial` and in the
shape of the seed data — the type system does not enforce it. Renaming the
pair to a neutral `Members` tuple is therefore mechanical and removes a
documentation lie that would only get worse once the same record can
represent a non-procreative pair.

### Resolved design questions

These map one-for-one onto the _Cross-cutting design questions_ and _Open
questions_ sections below. The full motivating discussion lives there; the
answers are summarized here.

1. **Naming.** `Couple` (Q1, Q-OQ-1). Chosen for inclusiveness — covers
   marriages, common-law partnerships, and other unions without privileging
   any one cultural framing.
2. **Member naming and gender semantics.** `Members : PersonId * PersonId`
   in canonical order, no gender field on the relationship (Q2, Q-OQ-2).
   Sex / gender stays on `Person.Shape` (which is itself a stand-in pending
   a proper gender model).
3. **Cardinality.** Always exactly two members. `Members : PersonId * PersonId`
   makes this a type-level guarantee. The smart constructor rejects `a = b`.

   **Single-parent and polyamorous arrangements still fit this model
   without expanding the representation:**
   - _Single parents._ When the identity of a child's other parent is
     genuinely unknown, the import / authoring layer synthesizes a `Person`
     with `Label = Some "Unknown"` (and minimal other identifying
     information) and uses that PersonId as the second `Member`. The
     genealogical tree always shows a `Couple`; the "missing" parent is
     simply a Person whose only knowable property is that they exist.
   - _Polyamorous arrangements._ What we care about is each partner's
     relationship to **the Wilp member**, since that's the relationship the
     visualization renders. A poly group containing one Wilp member and
     several partners is therefore expressed as several concurrent `Couple`
     records, each pairing the Wilp member with one partner. The fact that
     the Couples are concurrent (rather than sequential, as with successive
     marriages) is not modeled in this iteration.

     This relies on the simplifying assumption that a poly group contains
     **at most one Wilp member**. We are deliberately ignoring _ḵ'aats_
     relationships (in which both partners belong to the same clan,
     including same-Wilp pairings) for this iteration; once those are
     in scope, the assumption may need to be revisited.

   Because both cases map cleanly onto the two-member `Couple`, neither
   needs special test fixtures or seed data — the tests that exercise
   ordinary two-member Couples already cover the rendering and graph
   behavior they exhibit.

4. **Multiple Couples per Person.** No constraint — a Person can be in any
   number of Couples, procreative or childless or any mix. This matches the
   existing seed (Margaret has three procreative co-parents) and avoids
   a behavior change unrelated to the feature being added.
5. **Layout slot ordering for Couples under one Wilp parent.** Sort by an
   _effective date of union_, which is defined per Couple as:
   - Procreative Couple → DoB of the eldest child if known; otherwise
     undefined (`None`).
   - Childless Couple → its `DateOfUnion`, which may itself be `Some` or
     `None`.

   When _both_ Couples being compared have an effective date, sort by
   that date (with `CoupleId` as a tie-break on equal dates). When _either
   or both_ Couples lack an effective date, fall back immediately to
   comparing by `CoupleId`. The "missing" case is intentionally handled as
   "no defined position relative to dated Couples" rather than as
   "always sorts before/after dated Couples" — incomplete data should not
   pretend to imply an ordering it doesn't actually carry.

   Procreative and childless Couples are interleaved using this single
   comparator rather than being grouped into separate "with children" and
   "without children" buckets.

6. **Visual treatment of childless Couples.** Same as procreative: the
   "spouse bar" (two parallel parent-to-coparent lines) is rendered, and
   nothing hangs below. No new glyph is introduced. This minimizes new
   ECS / React surface area and lets the absence of descendants speak for
   itself.
7. **Marriage metadata scope.** Only `DateOfUnion : DateOnly option` in this
   iteration (needed for Q5). `DateOfDissolution`, `Status`, and free-form
   notes are out of scope; the `Couple` record can grow them later without a
   second migration.
8. **Conflict authority (was Q-OQ-6: "if both an explicit marriage and a
   `CoParentRelationship` between the same pair exist, what wins?").**
   Moot under Option B. There is only one type — `Couple` — and a child's
   parents are referenced by `CoupleId`. The "two sources of truth" problem
   this question was guarding against does not exist in the chosen design.
9. **Validation behavior.** `createFamilyGraph` throws (with a descriptive
   exception) on an unknown `CoupleId` referenced by a Person, on an
   unknown `PersonId` referenced by a Couple's `Members`, and on duplicate
   `CoupleId`s. This matches the existing convention in the file
   (`buildWilpTree` already uses `failwith` for similar lookup failures) and
   avoids spreading a `Result` type through every consumer for what is
   essentially a programming error.
10. **`WilpTree` shape for married Wilp leaves.** No new case on
    `WilpTree`. A married-but-childless Wilp member is represented as
    `Family { WilpParent = id; PartnersAndDescendants = Map [partnerId, []] }`
    with the empty sequence acting as "this partner brought no descendants
    to the tree." This keeps tree traversal uniform and only requires the
    descendant-handling code paths to tolerate empty sequences.
11. **Seed data scope.** `module Initial` will be updated to include at
    least one childless Couple between existing Gen-1 or Gen-2 people, plus
    one "married Wilp leaf with no descendants" case so the demo exercises
    both the empty-descendants `Family` rendering and the new sort
    comparator. Test fixtures (`TestData.fs`) gain analogous coverage.
12. **`createFamilyGraph` input shape.** Two separate sequences:
    `createFamilyGraph (people: seq<Person * CoupleId option>) (couples: seq<Couple>)`.
    Minimal disruption, mirrors the current tuple-based call shape, and
    keeps Couples and people independently constructible.
13. **Accessor naming on `FamilyGraph`.** The `coparents graph` accessor is
    renamed to `couples graph`. Its return type changes from
    `Set<CoParentRelationship>` to `Couple seq` (note: not `Set<Couple>`).
    Uniqueness-by-`CoupleId` is enforced at graph construction time (see
    validation rule #9), so callers don't need a `Set` for dedup; and as
    `Couple` grows additional metadata, structural comparison required by
    `Set<Couple>` would become progressively more expensive for no benefit.
    Callers (`Scene.extractFamilies`, `ModelTests`) update accordingly.

### What changes from the original gap analysis

Most of the gap analysis below still applies as written, with these
substitutions:

- Wherever it says "`CoParentRelationship`", read "`Couple`".
- Wherever it says "`CoParentsAndDescendants`", read "`PartnersAndDescendants`".
- `extractFamilies`'s `Set.intersect childrenOfMother childrenOfFather`
  step is replaced with a direct lookup of "children whose parent
  `CoupleId` is this Couple's id." Faster and less error-prone.
- `Scene.comparePeople` is unchanged; a new comparator
  (`compareCouplesByEffectiveDate`) is added per Q5 and used by
  `visitWilpForest`'s group sort.

The rest of the gap analysis (connector spawn changes, ECS-system
no-touches, generated TS / React no-touches, import-feature adjacency,
multi-Wilp adjacency) is unaffected.

### Out of scope (deferred)

These were considered and explicitly deferred:

- Couple metadata beyond `DateOfUnion` (status, dissolution, notes).
- _ḵ'aats_ (same-clan / same-Wilp) marriages, including the cardinality
  consequences for the polyamorous mapping discussed under Q3.
- Distinguishing concurrent Couples (as in polyamory) from sequential
  Couples (as in successive marriages). Both currently render the same way.
- A first-class "missing parent" representation. Single parents are
  modeled by synthesizing a `Person` with `Label = Some "Unknown"` and
  using that `PersonId` as the second `Member` of an ordinary `Couple`.
- Distinguishing birth parents from genetic parents — for example, a child
  whose birth mother is a surrogate and whose genetic mother is someone
  else. The current model collapses both into a single "parent"
  relationship; teasing them apart will require a separate design pass.
- Any change to the `Person.Shape` stand-in for gender.
- Validation expressed as a `Result` type rather than exceptions.

### Next step

Re-enter plan mode to break this decision into TDD-ordered implementation
todos covering, in dependency order: `Couple` type and smart constructor →
new `createFamilyGraph` signature with validation → renamed
`PartnersAndDescendants` field and updated `buildWilpTree` → renamed
`couples` accessor → updated `Scene.extractFamilies` to look up by
`CoupleId` → new effective-date comparator and updated `visitWilpForest`
group sort → relaxed `Scene.attachParentsToDescendants` for empty
descendants → conditional connector spawn in `Connectors.spawnAllConnectors`
→ updated `Initial` seed and `TestData` fixtures → portable ECS test for
the spouse-bar-only spawn path.

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

## Open questions (historical — see _Decision_ above for resolutions)

These are the questions the original draft listed as needing answers before
picking an option. They are kept here verbatim so the decision history is
auditable; each is now resolved in the _Decision_ section above.

1. Naming: `Marriage` vs `Union` vs `Partnership`?
   _Resolved: `Couple`._
2. Should the spouse pair stay `Mother`/`Father`, or move to a neutral pair?
   \_Resolved: neutral `Members : PersonId * PersonId`, canonicalized.
3. What's the layout slot for a childless co-parent (question 5 above)?
   _Resolved: interleaved with procreative co-parents by effective date of
   union._
4. Is a distinct visual glyph wanted, or just absence of descendants
   (question 4 above)?
   _Resolved: absence of descendants only — same spouse bar as procreative._
5. Are multiple childless marriages per person allowed?
   _Resolved: yes, no constraint._
6. If a marriage and a co-parent record exist for the same pair, what
   wins?
   _Resolved: moot — Option B has only one type for the pair._
7. How important is forward compatibility with marriage metadata
   (status/dates) — important enough to pay the Option B/D refactor cost?
   _Resolved: yes, important enough; Option B chosen partly for this
   reason. Day-one metadata is just `DateOfUnion`._

## Next step

Per the _Decision_ section: re-enter plan mode to break the chosen design
into TDD-ordered implementation todos. The implementation order is sketched
at the end of the _Decision_ section.
