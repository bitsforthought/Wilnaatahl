---
name: fsharp-doc-comments
description: >-
  How to write `///` doc comments on F# types and functions (and JSDoc on TS
  exports) in Wilnaatahl. Use before adding or editing any doc comment. These
  are the rules past sessions most often slipped on — read before touching F#.
---

# Doc comment style

Doc comments (`///` on F# types and functions, JSDoc on TS exports) appear in IDE
hover, language-server output, and generated docs. They should describe **what
the value is in isolation**, not how any particular caller uses it. The bullets
below are the ones past agent sessions most often slipped on; each has a worked
good/bad pair.

**Why this matters (the through-line for every rule below):** decoupling. A
comment that reaches _outward_ — to a caller, a downstream consumer, an
illustrative example naming another module's type, or a test — turns
documentation into a hidden dependency that silently rots the moment the far end
is renamed, moved, or deleted, and nothing fails the build to tell you. A comment
that describes only the thing it sits on stays true exactly as long as that thing
does. When you catch yourself naming something defined elsewhere, that is the
smell.

- **Describe the contract, not the consumer.** Say what the value/function _is_
  on its own terms. Don't reference downstream callers, name the function that
  will operate on the result later, or justify a field's shape by what some other
  module needs. The callee must stay readable without knowing who calls it.
  - ✅ `/// Member1 and Member2 are JSON person ids in source order; no ordering invariant is imposed.`
  - ❌ `/// Member1 and Member2 are JSON person ids; canonicalization to (min, max) happens later via Couple.create.`
  - **An "illustrative example" is not an exception.** A trailing `(e.g. Dragging)`
    that names a concrete downstream trait, relation, system, or caller is still a
    consumer reference — it re-couples the callee to one of its callers and rots
    when that example is renamed or deleted. State the property abstractly
    ("intended for exclusive relations") without naming who happens to use it.
- **Keep test knowledge out of non-test comments.** A comment in production or
  mock code must not encode which tests exist or what they assert ("Tests assert
  `StartsWith`, so…", "verified in TrackingTests M14/M15"). That couples the unit
  to the shape of its test suite and rots the moment a test is renamed, moved, or
  restructured — and "what we assert" is precisely what the test file is _for_.
  State the behaviour or contract the code guarantees; leave the assertions to the
  tests. A provenance note that a behaviour is pinned by the portable conformance
  tests is fine; naming individual cases is not.
  - ✅ `// The message prefix (up to the entity id) is the cross-backend contract, mirrored in kootaWrapper.ts.`
  - ❌ `// Tests assert StartsWith, so the trailing id is not part of the contract. See TrackingTests M14/M15.`
- **Don't justify field absences by pointing at a consumer.** State what the
  type captures and what source-format fields it doesn't capture. The reason for
  the omission can be implied by absence; it doesn't need a "because the transform
  doesn't read X" tail.
  - ✅ `/// Source fields with no representation here (dateOfBirth, birthWilp, deceased) are silently dropped at decode time.`
  - ❌ `/// Raw display-only date strings and birthWilp are not captured because the transform never consumes them.`
- **No version numbers in doc comments.** Package versions belong in the manifest
  (`.fsproj`, `package.json`) and decay quickly in prose. If you find yourself
  writing "Thoth.Json.Core 0.8.0 …" in a doc comment, drop the version; the
  manifest is the source of truth.
- **Don't restate the F# type signature.** A doc comment that says "Takes a string
  and returns a Result of RawFile or ImportError" adds nothing the signature
  doesn't. Use the doc comment to capture invariants, error shapes, edge cases, or
  semantic nuances the signature can't carry.
- **Direction matters.** It is fine for a comment in a _consumer_ module to
  reference the dependency it uses (`Import.fs` mentioning `Couple.create` is
  natural — `Couple` is what it's calling). It is **not** fine for a comment in a
  _dependency_ module to reference the consumer (`JsonTypes.fs` mentioning
  `Couple.create` reverses the dependency arrow in doc form).

## Related comment hygiene

- **Preserve existing comments.** Don't delete comments from code being
  refactored unless they're factually wrong.
- **When behavior changes, update comments to match.** Dead code paths should
  `failwith`, not silently return defaults.
- **A comment or spec is a claim the code must back up.** When writing a comment
  that asserts a property of the surrounding code ("single-pass", "uses tiny IDs",
  "matches spec rule X"), verify the property actually holds before committing.
  When the spec and implementation disagree, treat that as a sign one of them is
  wrong — pick the right behaviour and update both. Comments and specs that drift
  out of sync with the code are worse than no comment.
