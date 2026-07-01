# Zero-allocation `reframe` via measure-retag

## Goal

Replace the factor-multiplication in `LayoutVector.reframe` and
`LayoutBox.reframe` (`src/Wilnaatahl.Core/ViewModel/LayoutUtils.fs`) with an
O(1), zero-allocation measure relabel.

Units of measure are erased at runtime, so `LayoutBox<w>` and `LayoutBox<l>` are
the _same_ CLR type. Reframing therefore only needs to retag the value, not
rebuild it. The conversion stays type-checked, and scaling becomes
unrepresentable. (Measured: deep-tree rebuild ≈ 786 KB vs retag 0 bytes; retag
returns the same object.)

All production conversion factors are already `1.0` (pure relabels), so this is
behaviour-preserving.

## Design

### 1. Conversion witness (replaces the `float<'v/'u>` factor parameter)

The factor's _type_ gave the safety; its _value_ was the only thing that allowed
scaling. Replace it with a phantom witness that carries the frames in its type
but has no magnitude.

Declare the type **before `module LayoutVector`** (it is generic, so it needs no
concrete measures). Use a name distinct from the constants module to avoid a
type/module name clash:

```fsharp
/// Type-level evidence of a unit relabel from 'from to 'into. Carries the
/// frames in its type but no magnitude, so it can only relabel, never scale.
type ReframeWitness<[<Measure>] 'from, [<Measure>] 'into> = private | Witness
```

Declare the blessed witnesses **after the `w`/`u`/`l` measure declarations**
(currently ~L47–58), e.g. next to the numeric conversion constants:

```fsharp
[<RequireQualifiedAccess>]
module Reframe =
    let l2w: ReframeWitness<l, w> = Witness
    let u2w: ReframeWitness<u, w> = Witness
    let w2l: ReframeWitness<w, l> = Witness
    let w2u: ReframeWitness<w, u> = Witness
```

Only the four conversions actually passed to `reframe` are needed. **Do not
remove** the numeric `l2w`/`u2l`/`u2w`/`w2l`/`w2u` constants — they are still
used for scalar conversions (`x * l2w`). The numeric `LayoutBox.l2w` and the
witness `Reframe.l2w` coexist by namespacing.

Accessibility: keep `reframe`'s current visibility; `ReframeWitness` must be at
least as accessible (public type + `private` union case is correct).

### 2. New `reframe` bodies

```fsharp
// in module LayoutVector
let reframe (_: ReframeWitness<'from, 'into>) (v: LayoutVector<'from>) : LayoutVector<'into> =
    unbox (box v)

// in module LayoutBox  (note: no longer `rec`)
let reframe (_: ReframeWitness<'from, 'into>) (b: LayoutBox<'from>) : LayoutBox<'into> =
    unbox (box b)
```

- `LayoutBox.reframe` no longer recurses and no longer calls
  `LayoutVector.reframe` — the single retag relabels the whole tree at once.
- Comment the cast: `unbox (box _)` is statically unchecked but always succeeds
  because UoM erases (`LayoutBox<w>` and `LayoutBox<l>` are the same runtime
  type); it only relabels and allocates nothing.
- Rewrite both doc comments: O(1), zero-alloc, relabel-only (no scaling). Remove
  the existing "allocate a new box/vector" wording.

### 3. Update call sites (factor → witness)

- `Scene.fs:136,152`: `reframe w2l` → `reframe Reframe.w2l`
- `Scene.fs:182`: `LayoutVector.reframe w2u` → `LayoutVector.reframe Reframe.w2u`
- `LayoutUtils.fs` `attachAbove` (~L292–293): `reframe l2w` / `reframe u2w` →
  `reframe Reframe.l2w` / `reframe Reframe.u2w`

## Tests (`tests/Wilnaatahl.Core.Tests/ViewModel/LayoutBoxTests.fs`)

1. **Rewrite** `LayoutVector.reframe changes units correctly` (~L26): use
   `reframe Reframe.w2u`; assert values preserved, measure changed
   (`{2.0<w>;3.0<w>;4.0<w>}` → `{2.0<u>;3.0<u>;4.0<u>}`).
2. **Rewrite** `reframe changes LayoutBox units recursively` (~L60): use
   `reframe Reframe.w2u`; assert the whole tree relabels with values preserved.
3. **Retarget** the two `attachAbove` tests (~L296, ~L326):
   `reframe 1.0<w/l>` → `reframe Reframe.l2w`, `reframe 1.0<w/u>` →
   `reframe Reframe.u2w`. Assertions unchanged.
4. **Replace** the two inverse PBT properties (~L493, ~L500) with exact relabel
   round-trip facts that assert **physical (reference) equality**, proving the
   retag is zero-copy. Use `LanguagePrimitives.PhysicalEquality` (already used
   ~L291); round-trip keeps the types aligned:
   - LayoutVector: `PhysicalEquality v (v |> LayoutVector.reframe Reframe.w2u |> LayoutVector.reframe Reframe.u2w) =! true`
   - LayoutBox: same shape over a representative nested box, as a `[<Fact>]`.
5. **Remove** now-dead helpers: `factorGen`, `layoutVectorsApproxEqual`,
   `boxesApproxEqual`, and the recursive box generators `boxGen` /
   `boxGenSized` / `compositeGenSized` / `maxBoxDepth` (only the removed box
   inverse property used them).
   **Keep**: `approxEqual`, `leafGen`, `coordinateGen`, `sizeComponentGen`,
   `layoutVectorGen`, `sizeVectorGen` — the `attachHorizontally` properties use
   them.

## Notes

- `LayoutVector.reframe` is **not** deletable: besides the (now-removed)
  recursive use inside `LayoutBox.reframe`, it has one standalone caller
  (`Scene.fs:182`). It stays, as a witness retag.
- Records are reference types, so `box` is a no-op upcast (no allocation) and
  `unbox` is a cast that always succeeds under erasure.

## Validate

Run all three; all must pass:

- `npm run build` (confirms Fable accepts `unbox (box _)` for these types)
- `npm test`
- `npm run coverage:check` (baseline 97.5%)
