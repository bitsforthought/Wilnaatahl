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

- **Describe the contract, not the consumer.** Say what the value/function _is_
  on its own terms. Don't reference downstream callers, name the function that
  will operate on the result later, or justify a field's shape by what some other
  module needs. The callee must stay readable without knowing who calls it.
  - ✅ `/// Member1 and Member2 are JSON person ids in source order; no ordering invariant is imposed.`
  - ❌ `/// Member1 and Member2 are JSON person ids; canonicalization to (min, max) happens later via Couple.create.`
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
