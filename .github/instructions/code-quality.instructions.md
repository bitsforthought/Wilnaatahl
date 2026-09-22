---
applyTo: "**/*.fs,**/*.fsx,**/*.ts,**/*.tsx"
---

# Shared code quality

These language-independent rules apply to the repository's F# and TypeScript
production code and tests. Language-specific skills add syntax, idiom, and
ecosystem guidance.

## Keep code moves mechanical

When relocating existing code, preserve its named functions, helper boundaries,
declaration order, and useful explanatory comments, including doc comments directly
attached to moved declarations. A move should be easy to review as the same code in
a new home.

Do not combine relocation with inlining, restructuring, renaming, reordering, or
comment deletion unless the task explicitly requires that separate refactor or the
original text is no longer correct. Compare the old and new forms before declaring
the move complete; checking only that the implementation still behaves correctly
can miss deliberately preserved structure and explanation.

## Name values by their role

Name a literal or positional argument when its role is not clear at the use site.
This matters especially for:

- ordered constructor/function arguments whose parameter names are hidden;
- booleans whose effects cannot be inferred from `true` or `false`;
- units, thresholds, time steps, coordinates, dimensions, and projection values;
- fixture inputs repeated in assertions that verify forwarding or decomposition;
- constants used more than once in a formula.

Use the narrowest useful scope: a local binding beside one call is preferable to a
file-level constant used nowhere else (that said, avoid duplicating the same
constants in multiple places). Name the value's semantic role, not its spelling, and
verify that the name is mathematically and dimensionally true. The same literal
serving two roles needs two bindings. For example, `0.5` can be both an NDC-to-unit
scale and an origin offset; those are distinct concepts even though their values
happen to match.

Add a comment when the name cannot explain why the value was chosen, such as a frame
delta representing 60 FPS or a tolerance derived from an external specification.
Do not extract literals whose meaning is already obvious from the operation or
contract; naming should expose hidden meaning, not add indirection.

**TypeScript**

```ts
// ❌ The constructor gives no clue what each number means.
const camera = new PerspectiveCamera(55, 1, 0.1, 100);

// ✅ Each positional input names its role.
const verticalFieldOfViewDegrees = 55;
const aspectRatio = 1;
const nearClippingPlane = 0.1;
const farClippingPlane = 100;
const camera = new PerspectiveCamera(
  verticalFieldOfViewDegrees,
  aspectRatio,
  nearClippingPlane,
  farClippingPlane
);
```

**F#**

```fsharp
// ❌ The unit and role of this value are hidden.
runSystems world (1.0 / 60.0)

// ✅ The name and expression explain the role and exact value.
let frameDeltaSeconds = 1.0 / 60.0
runSystems world frameDeltaSeconds
```

In tests, preserve independent evidence. Reusing named fixture inputs in an
assertion is appropriate when the contract is exact forwarding, but do not calculate
an expected result with the same formula or traversal as the implementation under
test.

## Write comments that stay true

Comments create dependencies even though the compiler cannot check them. A comment
that names a caller, test, downstream consumer, or unrelated type can silently rot
when that other code changes; describe the local contract so the comment stays true
for as long as the declaration does.

Every comment also creates review work: a human must fact-check it against the code.
Default to no comment, and spend that attention only on information the code cannot
express clearly: constraints, rejected alternatives, non-obvious consequences, or
external contracts.

Before writing a comment, ask:

1. **Does the code already say this?** If it narrates the next few lines, improve
   the names or structure instead.
2. **Does this belong here?** Put a shared mechanism on its declaration and an
   architectural principle in repository instructions, not at each call site.
3. **Would one sentence do?** Length is not thoroughness; every sentence creates
   another claim a reviewer must verify.

- **Describe the contract, not the consumer.**
  - ✅ `Member1 and Member2 are source person ids; no ordering invariant is imposed.`
  - ❌ `Member1 and Member2 are reordered later by Couple.create.`
  - An illustrative example naming a downstream caller is still a consumer
    reference. State the property abstractly.
- **Keep test knowledge out of production comments.** State the behaviour or
  contract, not which test or assertion verifies it.
  - ✅ `The message prefix is the cross-backend contract.`
  - ❌ `TrackingTests M14 asserts StartsWith on this message.`
- **Describe omissions directly.** Say which source fields have no representation;
  do not justify their absence by naming the consumer that ignores them.
- **Do not put package versions in comments.** The dependency manifest is the
  source of truth.
- **Do not restate a type or function signature.** Document invariants, error
  shapes, edge cases, units, or semantic nuances the signature cannot carry.
- **Keep comments concise.** Do not add motivational prose or narrate the history
  of a fixture or implementation.
- **Follow dependency direction.** A consumer may mention the dependency it calls;
  a dependency should not name a downstream consumer.
- **Document a shared contract once.** Put the explanation on the declaration and
  let the name carry it at use sites.
- **Attach single-declaration comments to the declaration.** Use the language's
  doc-comment form rather than a free-floating banner; reserve banners for sections
  containing several related declarations.
  - **F#:** put `///` above the declaration's attributes.
  - **TypeScript:** put `/** ... */` immediately above the declaration.
- **Keep comments synchronized with behaviour.** Update or remove a comment when the
  behaviour changes.
- **Treat comments and specs as claims.** Verify assertions such as "single-pass"
  or "matches rule X" against the implementation before committing.
