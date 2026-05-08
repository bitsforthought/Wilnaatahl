# Stable `TreeNodeMesh` Identity Across Selection

`HuwilpGroup` currently renders person entities through two parallel queries —
unselected entities in a static map, selected entities inside a single
`<DragControls>` wrapper. When a node is selected or deselected, its entity
migrates between those two queries, which means React unmounts the old
`<TreeNodeMesh>` and mounts a fresh one in the other parent. `key={entity.id()}`
preserves identity _within a parent's children array_, but not _across
different parents_, so the remount happens despite the matching key.

This spec proposes restructuring so that each entity is rendered by exactly
one `<TreeNodeMesh>` for its lifetime, regardless of selection state, and
drag handling is wired per-node via raw `@use-gesture/react` instead of via
drei's `<DragControls>`.

## Problem

The remount-on-selection pattern is a structural footgun: every selection
toggle is a full unmount + remount of the entire mesh subtree (mesh,
geometry, material, `<Html>` portal, all descendants). Several classes of
bugs follow from this. Some are latent today; others would surface as soon
as new behaviour is added to `TreeNodeMesh`.

### Bugs already observed

- **Label font-size flicker** (fixed in commit `8793ab6`). The original
  `useState(16)` for the label font size reset on every remount, so the
  first paint of the new instance used the default `16px` before
  `useFrame` recomputed the camera-distance-tracked size. Most visible
  when zoomed out, where the correct size is much smaller than the
  default. Fixed by switching to `<Html distanceFactor>`, which mutates
  the wrapping div's CSS transform inside drei's own `useFrame` and holds
  no React state. The fix is sound, but it works _despite_ the remount,
  not by addressing it.

### Bugs latent in the current structure

- **Local state loss.** Any future `useState`, `useReducer`, or `useRef`
  inside `TreeNodeMesh` resets on selection. Concrete examples:
  - A hover-glow intensity state for highlight-on-hover behaviour.
  - An "is the tooltip showing yet" flag for hover-delayed tooltips.
  - A controlled `<input>` for inline rename of a person's label.
  - Any react-spring or framer-motion-3d hook holding interpolation
    state between transitions.

- **Animation interruption.** Any in-progress animation (selection-glow
  pulse, size transition, color fade) restarts from initial state when
  the component remounts mid-transition. Clicking a node mid-animation
  would visibly jump rather than smoothly continue.

- **Effect cleanup/re-run cycles.** Every `useEffect` /
  `useLayoutEffect` cleanup runs and the effect re-runs on selection.
  Idempotent effects survive this fine; effects that:
  - Initialize external resources (sound buffers, network handles)
  - Install long-lived subscriptions (websocket listeners, future
    `useTraitEffect` calls)
  - Have a debounce or "first-call" semantic
    will misbehave or do double-work per selection toggle.

- **Three.js / GPU resource churn (small in absolute terms).** Geometry,
  material, and `Mesh` objects are destroyed and recreated on every
  selection. For one node this is well under a millisecond; even
  rebuilding 100 meshes on a "deselect all" of a large selection is on
  the order of a perceptible single-frame hitch, not a sustained
  problem. Imperative `dispose()` patterns added later could leak if
  anyone forgets the new lifecycle.

- **Stale external references.** Anything outside the component
  holding a reference to the mesh becomes a dangling pointer on
  remount. Today only `MeshRef` does this (and `useMeshRef` correctly
  add/removes it, with a brief one-frame gap where systems iterating
  `Position, MeshRef` skip the entity). Any future custom `userData`,
  raycaster target list, or external listener would have to remember
  to re-attach on every selection.

- **Selection-vs-mount conflation.** A future hook like "play a sound
  when selected" or "log a telemetry event on selection" written as
  `useEffect(..., [isSelected])` would naturally also fire on first
  mount — and because _every selection is a first mount_, it's
  impossible to tell mount from selection from inside the component
  with this structure. Bugs of the form "this fires twice when I click
  the first time after page load" become endemic.

- **DragControls scope is implicit.** Today, "the children of
  `<DragControls>`" _is_ the selection set, by construction of
  `HuwilpGroup`'s two queries. That happy coincidence makes the drag
  plumbing brittle to evolve. A future "lock node — keep selected but
  disable dragging" feature, for instance, would require either a third
  query, a sibling `<DragControls>`, or partial unwiring of selection
  from drag.

## Goals

- Each person entity is rendered by exactly one `TreeNodeMesh` instance
  for its lifetime in the scene. Selection toggles do **not** remount it.
- Local state, refs, effects, and Three.js objects in `TreeNodeMesh`
  survive selection toggles transparently.
- Drag behaviour is identical to today, including multi-select drag
  (grabbing one selected node moves all selected nodes).
- HTML labels remain HTML overlays (preserves Unicode/Gitxsan
  combining-mark shaping and Playwright DOM testability).

## This is not a performance fix

The motivation is architectural, not perf. The remount cost today is
small in absolute terms — sub-millisecond for one node, on the order of
a perceptible single-frame hitch only for bulk-selection-clear actions
(`world.RemoveAll Selected` in `Selection.fs:36, 42, 59`) on large
trees. The dominant contributors per remount are drei's `<Html>` portal
teardown/recreate (DOM node detached from `document.body`, React root
torn down and rebuilt, drei's own `useFrame` resubscribed) and the
Three.js mesh + geometry + material rebuild. None of this is large
enough to motivate the refactor on its own. The case is the _latent
bugs above_: any future React state, ref, effect, or animation in
`TreeNodeMesh` would silently break, and disentangling "selection" from
"mount" makes lifecycle hooks behave the way contributors will expect.

If a perf problem ever does materialize here — e.g. a tree of thousands
of people where bulk selection-clear becomes a sustained hitch — this
refactor will incidentally help. But it shouldn't be sold as the
primary reason for landing it.

## Non-goals

- Changing the visual appearance of selected vs unselected nodes
  (`Palette.fs` decides that; out of scope).
- Migrating labels to `<Text>` + `<Billboard>` (separately ruled out for
  Unicode reasons).
- Lifting label-scale calculation into a derived F# trait (worth doing
  separately if/when label sizing logic becomes more complex).

## Why not the alternatives we already considered

- **Per-node `<DragControls>` instances.** Tried in this session and
  reverted in `8793ab6`'s history. drei's `<DragControls>` is designed
  as a single instance wrapping the draggable subtree; per-node
  instances mis-attribute the drag transform — labels move, meshes
  don't. Reading drei's source confirms `<DragControls>` itself is a
  thin wrapper around `@use-gesture/react`'s `useDrag`, so we can use
  the underlying primitive directly without inheriting the
  one-instance-per-subtree assumption.

- **Keep two queries, use `useTraitEffect` to bridge selection-driven
  side effects.** Possible, but doesn't address the root cause. The
  component still remounts; we'd just be working harder to compensate
  inside it. Each new piece of `TreeNodeMesh` logic would need its own
  workaround. Better to fix the structure once.

- **Forward refs and a manual key-based slot system to "move" the same
  Three.js mesh between two parents.** React doesn't support
  re-parenting a component instance. Workarounds exist (portals,
  imperative `appendChild`) but they fight the framework and erase the
  declarative model.

## Approach

### High-level structure

```tsx
// HuwilpGroup.tsx (after)
const personEntities = useQuery(Size, PersonRef, Not(Hidden));
// ...
{
  personEntities.map((entity) => <TreeNodeMesh entity={entity} key={entity.id()} />);
}
```

```tsx
// TreeNodeMesh.tsx (sketch)
export function TreeNodeMesh({ entity }: { entity: Entity }) {
  const isSelected = useTrait(entity, Selected) !== undefined;
  const ref = useMeshRef(entity);
  const bind = useDrag(
    ({ delta: [dx, dy], first, last }) => {
      if (first) handleDragStart();
      handleDrag(/* matrix from delta */);
      if (last) handleDragEnd();
    },
    { enabled: isSelected }
  );

  return (
    <mesh {...bind()} ref={ref} onClick={...} onPointerDown={...}>
      {/* unchanged */}
    </mesh>
  );
}
```

The single map keyed by entity id gives every entity stable React identity
across its lifetime in the scene. `useDrag` attaches gesture handlers
directly to the mesh's pointer events; `enabled: isSelected` makes the
gestures inert on unselected nodes without any structural change.

### Multi-select drag

The F# `Events.handleDrag` system already applies the drag delta to **all**
entities with the `Selected` trait (`Dragging.fs:59`). Whichever selected
node receives the gesture forwards the delta into `handleDrag`, and the
F# side moves the entire selection. Behaviour is preserved.

### Coordinate-space conversion

drei's `<DragControls>` provides `onDrag(localMatrix, deltaLocalMatrix,
worldMatrix, deltaWorldMatrix)`. The current `eventActions.handleDrag`
decomposes `localMatrix` into a `Vector3` translation:

```ts
handleDrag: (localMatrix: Matrix4) => {
  const local = new Vector3();
  localMatrix.decompose(local, new Quaternion(), new Vector3());
  Events.handleDrag(wrappedWorld, local.x, local.y, local.z);
};
```

`useDrag` from `@use-gesture/react` reports gesture data in screen pixels
(`movement: [px, py]`, `delta: [dx, dy]`). The implementation needs to
project that into the same world-space coordinates the F# side expects.
Two options:

- **Project pixel delta to world delta via the camera.** Use the
  perspective camera's projection at the mesh's depth to convert
  pixels → world units. This is the standard Three.js pattern and
  matches what `<DragControls>` does internally (its source uses
  `raycaster` + `intersectPlane` for a similar effect).

- **Use the gesture's `xy` against `useThree().raycaster`.** Cast a ray
  from the cursor position onto the `axisLock="z"` plane through the
  initial node position, take the world-space intersection point, and
  emit deltas in those coordinates. Closer to a 1:1 replacement for
  what `<DragControls axisLock="z">` does today.

The second option is the safer port — it preserves the "drag locks to
the z = node-z plane" semantics of the current `axisLock="z"` setting.
Implementation will need to verify that the F# `handleDrag` system's
expectations of the input units are met (it expects world-space
displacement based on the delta calculation in `Dragging.fs:54-57`).

### Selection rendering changes (none)

The `paintTreeNodes` system in `src/ecs/rendering.ts` already queries
`MeshRef, PersonRef, Selected` vs `MeshRef, PersonRef, Not(Selected)`
and applies the selected-vs-unselected paint each frame. That continues
to work unchanged — it operates on the mesh, not on which React parent
holds it.

## Files affected

- `src/react-components/HuwilpGroup.tsx` — collapse `staticEntities` and
  `draggableEntities` into one query; remove the outer `<DragControls>`
  and the `useActions(eventActions)` call.
- `src/react-components/TreeNodeMesh.tsx` — read `Selected` via
  `useTrait`; replace `<mesh>`'s pointer-event props with `useDrag`
  bindings (`...bind()`), gated on `enabled: isSelected`. Keep
  `handleMeshClick`/`handlePointerDown` for selection plumbing if
  `useDrag` doesn't subsume them (verify against the drag-vs-click
  disambiguation already in `Dragging.fs`'s `handleDragEnd`, which
  cleans up the spurious `ClickEvent` after a drag).
- `src/ecs/index.ts` — likely a new `eventActions.handleDrag` signature
  taking world-space delta directly (or remove the `Matrix4` decomposition
  step), since `useDrag` reports in different units than drei's
  `<DragControls>`.
- `package.json` — add `@use-gesture/react` as a direct dependency
  (currently transitive via drei).

## Risks and open questions

- **Coordinate conversion correctness.** The biggest unknown is making
  `useDrag`'s screen-space gesture data produce the same world-space
  deltas that `Events.handleDrag` currently receives. A regression here
  would manifest as nodes moving the wrong distance or in the wrong
  direction. Mitigated by writing a small interactive smoke test
  (drag a node a known number of CSS pixels, verify the
  `Position` trait moved by the expected world distance) before
  declaring done.

- **Spurious click events after drag.** Today
  `Dragging.fs::handleDragEnd` removes any `ClickEvent` raised in the
  same frame as a `DragEndEvent`, to keep dragging from also toggling
  selection. With raw `useDrag`, click vs drag disambiguation may need
  re-checking — `useDrag` has its own `tap`/`drag` distinction
  (`onClick` vs `onDrag` callbacks), and we should confirm the F# side
  still gets the events it expects.

- **OrbitControls interaction.** `TreeScene.tsx` disables
  `OrbitControls` while a drag is in progress
  (`enabled={!isDragInProgress}`). This is driven by an ECS query for
  `Dragging("*")`, which is updated by F# `handleDragStart`. As long
  as `useDrag` calls our `handleDragStart` on first gesture event,
  this continues to work.

- **`@use-gesture/react` API surface.** Bringing it in as a direct
  dependency is a small commitment. It's already transitively
  present (drei depends on it), and it's the de facto gesture library
  for React + Three.js, so the risk is minimal — but worth noting.

- **Test coverage.** No automated tests exercise the React drag
  plumbing today. A manual smoke test is the practical bar:
  - Drag a single selected node — moves to follow cursor.
  - Multi-select two nodes, drag one — both move together.
  - Click a selected node — deselects (no spurious drag).
  - Click and drag in one motion — drags without leaving selection
    toggled at the end.
  - Drag-end while still over the node — no spurious click event
    leaks through to selection.

## Recommended landing strategy

This refactor is self-contained and, on its own, has zero externally
visible behavioural change — it just makes the component identity
stable. That makes it a poor candidate for a standalone PR, because
without a user-visible payoff it's hard to motivate review effort.

The recommended approach is to **land it alongside the next feature
that would otherwise hit one of the latent bugs above** — for example:

- A hover-glow effect for tree nodes (would lose state on selection
  without this fix).
- A selection-transition animation (would visibly jump on click).
- An inline rename popup (would lose input focus and content).
- Any `useTraitEffect`-based imperative behaviour where the
  initialize-twice/cleanup-twice cycle would be wasteful or
  incorrect.

That way the structural fix gets test coverage from the new feature,
the PR has a user-visible justification, and the latent bug list
above shrinks as features are added.
