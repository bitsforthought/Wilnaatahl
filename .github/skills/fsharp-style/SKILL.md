---
name: fsharp-style
description: >-
  F# code-style conventions for the Wilnaatahl Core project — how to write
  idiomatic, functional-first F# and avoid C#/OOP-style code. Use whenever
  writing or modifying any `.fs` source file (Model, ViewModel, Traits,
  Entities, Systems, ECS) or reviewing F# for idiom violations.
---

# F# Code Style

F# code is functional-first, minimal, and avoids mutation. The rules below steer
away from C#-in-F# habits. Each is a hard convention for this codebase.

- **Think in declarative expressions, not step-by-step construction.** The most
  common way F# degrades into "C# with different syntax" is building a result
  procedurally — unwrap, branch, mutate, append, assert inside a branch — where a
  single declarative expression would do. Before writing a `match`/`if`/fold that
  assembles a value or an assertion piece by piece, ask whether the whole thing can
  be one expression compared against, or built from, a literal. Reach for
  combinators (`Result.map`, `Option.map`, `List.map`, structural equality on whole
  values) over manual unwrap-and-reassemble. This is the mindset behind many of the
  specific rules below.

- **Separate pure and impure code.** Keep I/O (file reads, console output,
  `exit`) at the top level or in a thin shell. Functions called by the top level
  should be pure — no side effects, no `exit`, no file I/O. Communicate errors
  via discriminated union return types (e.g., `Result<'T, Error>`) rather than
  exceptions or early exits.
- **Use F# idioms, not workarounds.** Prefer bare `_` discards over `_prefixed`
  names where the language allows. Use tuple-style `TryRemove` returns instead of
  `&` out-params. Don't over-qualify record constructors when the type can be
  inferred.
- **Prefer recursion and folds over `mutable`/`while`.** Reach for `List`/`Seq`/
  `Array` combinators (`fold`, `takeWhile`, `skipWhile`, `choose`) or a tail-
  recursive function before introducing `let mutable` and `while`. Mutation is
  occasionally justified (tight interop, measured hot loops) but should be rare
  and tightly localized — not the default way to accumulate or iterate.
  - ✅ `let rec loop acc xs = match xs with [] -> List.rev acc | x :: rest -> loop (f x :: acc) rest`
  - ❌ `let mutable acc = []` … `while i < xs.Length do acc <- acc @ [ f xs[i] ]; i <- i + 1`
- **Use modern indexing syntax.** Write `expr[i]` and `expr[i..j]`, not the legacy
  `expr.[i]` / `expr.[i..j]`. Fantomas does not rewrite this and no standard F#
  analyzer flags it, so keep to the convention by hand — the adversarial review
  checks for it.
  - ✅ `lines[0]`, `m.Groups[1].Value`, `lines[1..offset]`
  - ❌ `lines.[0]`, `m.Groups.[1].Value`, `lines.[1..offset]`
- **Layering: helpers go in their consumer module, not the dependency.** If a
  function exists solely to assist one specific caller (e.g. a comparator the
  layout uses to sort), it belongs in the caller's module even though it operates
  on the dependency's types. The dependency module should expose primitives
  (lookups, queries) that any consumer can build on. (See the `fsharp-doc-comments`
  skill for the related rule about how this affects the dependency's doc comments.)
- **Smart constructors for types with invariants.** When direct record
  construction could produce an invalid value (canonical field ordering,
  mutually-exclusive cases, validation rules), declare the record `private` and
  force construction through a `Module.create` smart constructor. Expose member
  properties on the record so dot-notation reads still work at call sites.
- **Make illegal states unrepresentable, then check invariants in one place.**
  Prefer a type that can't hold a bad value over re-checking the bad value at
  every use site. When the type genuinely must admit a value you later reject
  (e.g. a parsed scalar that may be blank), funnel the check through **one** shared
  predicate or active pattern rather than repeating `when s.Trim() <> ""` (or
  similar) in every match. A repeated guard is a smell that a helper, active
  pattern, or tighter type is missing.
  - ✅ `let (|NonEmptyScalar|_|) = function Scalar(s, _) when s.Trim() <> "" -> Some(s.Trim()) | _ -> None` — then match `NonEmptyScalar v` everywhere.
  - ❌ the same `Scalar(s, _) when s.Trim() <> ""` guard copy-pasted across three functions.
- **Don't materialize sequences unnecessarily.** `Seq.toArray` / `List.ofSeq`
  are appropriate when callers need random access or when a sorted sequence will
  be enumerated more than once (the second enumeration of `Seq.sortWith`'s result
  would re-sort). They are not a default "make this concrete" reflex —
  round-tripping `seq → array → seq` allocates for no gain. Materialize once at
  the point where multiple enumerations actually happen.
  - ❌ `let keys = src |> Seq.filter p |> List.ofSeq in for k in keys do f k` —
    the list is built only to be walked once; pipe straight to `src |> Seq.filter p |> Seq.iter f`.
  - The usual excuse — "I must materialize first to avoid mutating the collection
    while I iterate it" — often doesn't even apply: `ConcurrentDictionary.Keys`
    already returns a **snapshot**, so filtering `store.Keys` and calling
    `store.TryRemove` in the same `Seq.iter` is safe with no intermediate list.
    Check whether the source is already a snapshot before reaching for `List.ofSeq`.
- **Split a collection in one pass, not two.** When you need both the elements
  that match a predicate and those that don't, use `List.partition` (or a single
  `fold`/`groupBy`) once — don't run two `List.filter`/`List.choose` passes over
  the same list. For a `Result`/`Choice` list where you want "first error, else
  all the successes", sequence/traverse it with a single `fold` rather than
  `choose`-ing the errors and then `choose`-ing the successes separately.
  - ✅ `let kept, dropped = items |> List.partition predicate`
  - ❌ `let kept = items |> List.filter predicate` then `let dropped = items |> List.filter (predicate >> not)`
- **Pipe the "subject" argument into a call.** When a function's last argument is
  the thing being operated on, pipe it in so the call reads subject-verb-object.
  Applies to stdlib (`Map.add`/`Map.find`/`Set.contains`/`List.map`) **and our own
  functions** — e.g. every `FamilyGraph` accessor takes the graph last, so pipe
  the graph in. Skip it only for symmetric operations (`Set.union`,
  `Set.intersect`) where neither argument is the subject.
  - ✅ `nameById |> Map.add n.Id (Name n.Text)`, `seenTexts |> Set.contains n.Text`
  - ✅ `graph |> namesHeldBy (PersonId 0)`, `graph |> findPerson pid`
  - ❌ `Map.add n.Id (Name n.Text) nameById`, `Set.contains n.Text seenTexts`
  - ❌ `namesHeldBy (PersonId 0) graph`, `findPerson pid graph`
  - **This is a rule about the whole expression's data flow, not just one call
    site — it applies everywhere a subject appears, not only at the top of a `let`
    binding.** The two shapes that keep slipping past author and reviewer both hide
    the subject, so train your eye on them specifically:
    - **Nested / inside-out application → a left-to-right pipeline.** Don't write
      `g (f subject)`; the subject is buried in the innermost parens. Flow it
      forward: `subject |> f |> g`. This is the C#/OO "transliteration" smell —
      building a value inside-out instead of reading it as a data pipeline.
      - ✅ `expected |> List.map fst |> Set.ofList`
      - ❌ `Set.ofList (List.map fst expected)`
    - **Operands of operators are expressions too — pipe the subject there as
      well.** A subject sitting as an argument on either side of `=`, `=!`, `<`,
      `@`, etc. still gets piped; don't leave it in application form just because
      it's "only the expected value" or because a pipe there would need parens.
      - ✅ `actual =! (expected |> List.map snd)`
      - ❌ `actual =! List.map snd expected`
      - **Precedence gotcha (why the paren is needed, and why it's easy to dodge
        the pipe here):** `|>` and comparison/assertion operators like `=!`/`=`
        share the same precedence and are **left-associative**, so
        `a =! xs |> List.map f` parses as `(a =! xs) |> List.map f` — a type error,
        not what you meant. A pipeline on the **right** of such an operator must be
        parenthesized: `a =! (xs |> List.map f)`. The application form needs no
        paren, which is exactly the friction that quietly pushes code back to the
        ❌ shape — resist it; wrap the pipe.
      - **Left-associativity also means a pipeline on the _left_ needs no parens
        — don't add them.** `xs |> List.map f =! expected` already parses as
        `(xs |> List.map f) =! expected`. Wrapping the left side is noise, and it
        is the more common shape in assertions, so watch for it.
        - ✅ `world.Query(With Button) |> Seq.length =! 1`
        - ❌ `(world.Query(With Button) |> Seq.length) =! 1`
- **Use `List.foldBack` instead of `fold` then `List.rev`.** Folding to build a
  list and reversing it to restore forward order is two passes for one result;
  `List.foldBack` accumulates in forward order directly. (Arg order differs:
  `List.foldBack folder list state`.)
  - ✅ `List.foldBack folder items initialState`
  - ❌ `List.fold folder initialState items` then `List.rev`
- **Parse at the boundary into the strongest type; don't carry raw strings.** A
  domain value with a precise type (an ISO-8601 date → `DateOnly option`, a fixed
  set of tags → a DU) is parsed once at the import/decode boundary and stored as
  that type in the model; unparseable input becomes `None` (with a warning) there.
  Keeping the raw `string` in the model and re-parsing it at each use site is the
  "illegal states representable" smell.
  - ✅ model field `NameDate: DateOnly option`, parsed in `Transform.fs`
  - ❌ model field `NameDate: string option`, parsed inside a comparator on every call
- **Parenthesize tuple elements in a single-item list.** A one-element list of a
  tuple written `[ a, b ]` (e.g. `Map [ "k", v ]`) triggers compiler info `FS3886`
  ("This list expression contains a single tuple element. Did you mean to use ';'
  instead of ','...?"), because `,` in a list is a common typo for `;`. Write the
  tuple explicitly to state intent and clear the diagnostic; the ambiguity
  disappears with two or more entries.
  - ✅ `Map [ ("k", v) ]`
  - ❌ `Map [ "k", v ]`
- **Naming: spell things out.** `makeComparatorGraph` over `mkComparatorGraph`,
  `child` over `kid`, `wilpHead` over `mWilp`. Hungarian-style prefixes (`m`, `p`)
  and abbreviation prefixes (`mk`) save almost no characters while obscuring
  meaning; prefer names that describe the role directly.
- **Don't use `emitJsExpr` when F# works.** Default to pure F#; only use JS
  interop when the F# standard library can't express it.
- **No optional parameters.** Optional parameters are an OOP/C#-interop feature,
  not idiomatic F#. Make all parameters required. If F# type inference fails
  without an overload, add type annotations at the point of declaration rather
  than introducing optional parameters or leaving dead overloads.
- **Don't introduce unnecessary abstractions.** Avoid over-generalizing (e.g.,
  predicate callbacks when a concrete parameter suffices).
- **Don't leave dead code.** Remove unused overloads, unreachable branches, and
  orphaned helpers. **Dead-code removal must be deep**: when deleting a function,
  also delete the data structures, derivation logic, and helper types that exist
  solely to serve it. Grep for every reference before declaring the removal done.
  If removing code causes a compile error, fix the root cause (e.g., add type
  annotations) rather than keeping the dead code as a workaround.
- **Avoid unnecessary type annotations.** F# infers types well in most cases.
  Only add annotations when needed for disambiguation or to fix inference failures.
- **Don't erase types with `obj` for convenience — it defeats the type system.**
  `obj` (and `box` / `unbox` / `:?>`) discards static typing: the compiler can no
  longer stop a wrong value from reaching a cast, so what would have been a build
  error becomes a runtime failure (an `InvalidCastException` on the CLR, a Fable
  runtime throw in the browser — exactly where we have no compiler to catch it).
  Reach for `obj` only when there is genuine heterogeneity with **no** common
  supertype (e.g. a store of unrelated boxed values), and even then confine the
  erasure to the narrowest scope and recover the concrete type at a single, obvious
  boundary. It is **not** a tool for dodging upcast friction: if a function must
  accept several subtypes of one base, take **that base** — or a **flexible type**
  `#Base`, which accepts any subtype with no caller `:>` ceremony — instead of
  widening the parameter (or a collection's key/value) to `obj`. "It works for
  every caller" is not a justification; turning off the type checker always
  "works".
  - ✅ `let stores = ConcurrentDictionary<IRelation, _>()` and `let get (r: #IRelation) = …` — accepts every relation, still fully type-checked.
  - ❌ `let stores = ConcurrentDictionary<obj, _>()` and `let get (r: obj) = …` — "accepts everything" by switching the type system off.
  - A downcast (`:?>` / `unbox`) is the tell that a value was erased; each one
    should trace back to a deliberate, justified erasure, not a convenience `obj`.
- **Use proper words for record field names.** Single-letter field names like
  `R/G/B` or `L/C/H` are noisy at call sites and ambiguous in doc strings. Spell
  them out (`Red/Green/Blue`, `Lightness/Chroma/Hue`).
- **Encapsulate as tightly as possible.** Default new declarations to `internal`
  (assembly-scoped — the F# equivalent of C# `internal`) if they need to be usable
  from tests; promote to public only when something genuinely crosses an assembly
  boundary (e.g. consumed by Fable-generated TS or by a downstream project). The
  `Wilnaatahl.Core` project has `InternalsVisibleTo` entries for its test
  assemblies, so `internal` declarations remain test-visible. F# `private` is
  _module_-scoped, not assembly-scoped — use `internal` when you mean "visible to
  tests but not to other projects".
- **No magic numbers in production code.** Algorithmic constants need names. For
  values used in only one function, declare a local `let` binding inside the
  function rather than a module-level constant.
- **Use named top-level functions for hot-path callbacks.** ECS/query callbacks
  (e.g. `updateEach`) called per frame per entity should reference named
  functions, not inline lambdas, so the closure is allocated once.
- **All `open` statements at the top of the file.** No `open` mid-module. Group
  them at the top so the file's dependencies are visible at a glance.
- **Cross-assembly anonymous records.** Anonymous records created in one assembly
  are a different type from those in another. Use helper functions in the source
  assembly (e.g., `Line3.pos`) to create anonymous records that can be used in
  test assertions.

## Runtime reality

Production runs as **Fable-generated JS in a browser**, not on the CLR. Don't
assume a CLR runtime when writing code comments or reasoning about runtime
behaviour.

### Decorate DUs that cross the F#/TS boundary

An undecorated F# discriminated union compiles to a Fable `Union` **class** plus a
`Foo_$union` alias, numeric `tag`s, a positional `fields` array, and a factory
function per case. TypeScript consumers then read `row.tag === 3` and cast
`row.fields[0] as string` — untyped, unreadable, and silently wrong if case order
changes. Pick the attribute that fits the DU's shape (guarded by
`#if FABLE_COMPILER`, so the .NET build and its tests keep a plain DU):

| DU shape                         | Attribute                           | Emitted TypeScript                           |
| -------------------------------- | ----------------------------------- | -------------------------------------------- |
| All cases nullary                | `[<StringEnum>]`                    | `type Pdeek = "laxGibuu" \| "laxSkiik" \| …` |
| Cases carry fields               | `[<TypeScriptTaggedUnion("kind")>]` | `\| { kind: "rawText", text: string }`       |
| Single-case wrapper over a value | `[<Erase>]`                         | the underlying type, no wrapper              |

- **Name the fields of a tagged-union case** (`RawText of text: string`) — the
  case-field name becomes the TS property name, so an unnamed field emits a
  useless `Item`.
- Consumers then `switch` on the tag string and get **compile-time
  exhaustiveness** (via a `never` guard) plus narrowed field access — no casts.
- **`[<StringEnum>]` has a Fable emission bug in lambda-return position:** a
  lambda returning one is typed `string` while its container is typed by the
  union, so e.g. an ECS trait factory (`refTrait (fun () -> En)`) emits TS that
  fails `tsc`. When that bites, leave the DU undecorated and say why in a comment.
- The `_$union`/`tag`/`fields` shape is the smell; if TS consumption code is
  casting out of `fields`, the fix belongs on the F# type, not in the TS.

### ECS systems

The rules for writing an ECS system — one behaviour per system, where traits
live, `Runner.fs` ordering, same-frame input arbitration, and never writing a
trait value that hasn't changed — are **architecture, not style**, so they live in
`AGENTS.md` under _Architecture & Data Flow_. Read them before adding or changing
anything under `Systems/`.

## Build vs buy: reach for a standard library first

Before hand-rolling non-trivial mechanics — parsing, serialization, a data
format, an algorithm, a diff — **stop and look for an established, well-maintained
library**, and make the build-vs-buy decision explicitly rather than defaulting to
bespoke code. The goal is to surface useful dependencies _early_, before sinking
effort into fragile, expensive-to-maintain hand-rolled solutions.

- **Production F# (`src/Wilnaatahl.Core/`) compiles through Fable to JS**, so a
  `.NET`-only library won't work there. The candidate must be Fable-compatible (or
  implemented in JS/TS and bound), which narrows the field — weigh that constraint.
  (Build scripts under `scripts/` run on the CLR and have the full NuGet ecosystem
  available; see the `infra-scripts-fsharp` skill.)
- **Evaluate a candidate on:** license (compatible with this repo's AGPL-3.0 +
  non-commercial terms; MIT/Apache are fine), maintenance health (recent releases,
  not a dead repo), whether it ships or is build-only, and bus-factor.
- **Hand-roll only when** no suitable library exists, the dependency is
  disproportionate to the need, or a hard constraint (Fable/browser) precludes it
  — and say _why_ in a comment.
- **Worked examples from this repo:**
  - ✅ `ValidateAgentDefinitions.fsx` parses frontmatter with **YamlDotNet** (MIT,
    build-only) instead of a hand-rolled YAML parser that accumulated a dozen
    edge-case bugs (duplicate keys, comments, quoting, nested mappings).
  - ⚖️ The modern-indexing rule (`expr[i]` over `expr.[i]`) and the agent/skill
    frontmatter _schema_ have **no** standard tool, so a small bespoke check is
    justified — but that was a conscious finding after looking, not a default.

## Modern F#

This repo pins an explicit F# `<LangVersion>` in `Directory.Build.props` (a
manifest, where the version number belongs) and bumps it deliberately — write for
that pinned version and prefer current idioms over older ones. The catch: the
pinned version post-dates most model training data, so "what's modern" — and even
the exact **syntax** of a feature — is easy to get wrong. Treat the rules below as
the defence.

- **Verify language features; don't trust recall — or a single failed check.**
  Before using a feature you believe is modern, confirm it compiles **against the
  pinned `LangVersion`** — not against whatever `dotnet fsi` happens to default to.
  A plain `dotnet fsi` snippet uses the SDK's default language version, which can
  be _newer_ than the pin and accept syntax a project build rejects; check with
  `dotnet fsi --langversion:<pinned>` or an actual `dotnet build`. A remembered
  feature may have different syntax, need a newer `LangVersion`, or not exist — but
  a _failed_ check can equally be your test's fault, so reduce to a clean minimal
  repro before concluding. (Worked example: nested record copy-and-update
  `{ r with Child.Value = x }` is a real F# 8 feature and **does** compile here; an
  initial check "disproved" it only because the throwaway test made the field name
  collide with a type name — `Inner.Value` then parsed as static member access, not
  a field path. The feature was fine; the test was wrong.) Scripts under `scripts/`
  run on the `dotnet fsi` default, so for those a plain `dotnet fsi` check matches
  their reality.
- **Investigate before assuming.** When empirical results conflict with
  documentation (Fable, Koota, or F# behaviour), check the source code and issue
  tracker before concluding something is a bug or intended behavior.
- **For production `src/`, also confirm Fable support.** Fable can lag the F#
  compiler, so a feature that compiles under `dotnet build` may not survive
  `npm run build`. The cheapest proof is that the idiom is **already used in
  `src/`** with a green Fable build; otherwise verify before relying on it there.
- **Verified, preferred idioms:**
  - **Modern indexing** `expr[i]` / `expr[i..j]`, never `expr.[i]` (see the bullet
    above; not tool-enforced).
  - **Interpolated strings** `$"…{value}…"` for simple interpolation — used in
    `src/` with a green Fable build, so Fable-confirmed. `sprintf` is still fine
    when you need typed format specifiers (`%0.2f`, `%A`). (The triple-quoted
    _interpolated_ form `$"""…"""` is not yet used in `src/` — verify it under
    `npm run build` before using it there.)
  - **Shorthand member lambdas** `_.Property` / `_.Member.Sub` instead of
    `(fun x -> x.Property)` (F# 8) — used in `src/` (e.g. `Seq.map _.RenderedInWilp`
    in `ViewModel/Scene.fs`, `Seq.map _.Size.Y` in `ViewModel/LayoutUtils.fs`), so
    Fable-confirmed.
- **Keep this list living.** When you confirm another modern idiom works here (and
  under Fable, for `src/`), add it with a one-line note on how it was verified.
