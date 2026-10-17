# fsharp-refactor: integration feedback and suggestions

This report shares feedback from integrating fsharp-refactor into
[Wilnaatahl](https://github.com/bitsforthought/Wilnaatahl), an open source
codebase that leans heavily on agentic engineering. Deterministic refactoring
and analysis help us keep agent-authored changes consistent and reviewable.

The automatic refactorings have been valuable enough to adopt the tool in our
build/CI pipeline. These suggestions come from integrating versions 0.8.42 and
0.8.47 into an F# application compiled through Fable's TypeScript backend to
JavaScript for the browser, with .NET tests and F# build scripts.
They describe those versions, not a fresh assessment of the latest release.

## 1. Reject invalid configuration before editing

During integration, we found that malformed configuration could fall back to
defaults, and unrecognized rule IDs could be ignored. A typo in an opt-out can
therefore leave an unwanted automatic rewrite enabled.

**Suggestion:** fail before editing on malformed configuration, unknown rule IDs,
invalid option values, and duplicate entries. Include the configuration path and
offending setting in the error. Consumers should not need to implement their own
validator for the tool's configuration.

## 2. Support scoped profiles and explain effective policy

We need general quality rules across production, tests, and .fsx scripts, but
selected performance rules only in browser-targeted JavaScript production code.
The CLR allocation benefits of struct representation and value options do not
carry over to that runtime. As your README explains, Fable also has other
backends: `[<Struct>]` can still matter for Rust. Categories alone are
insufficient: applicability depends on the backend and runtime, not just whether
a project uses Fable.

The evaluated CLI had no named profiles or explicit `--config` option, requiring
separate invocations and an external performance rule allowlist. Explicit `--codes`
selection could also run a rule disabled in the discovered configuration.

**Suggestion:** support path-scoped profiles, explicit configuration selection,
and documented override precedence. An effective-policy command should show
which rules will run, on which inputs, and why.

## 3. Explain how analysis selects browser versus .NET code

The same F# project can use different code and packages in a browser and in .NET
tests. In our project, `#if FABLE_COMPILER` selects the implementation used for
Fable compilation, while an MSBuild property selects a JavaScript-specific JSON
package. The symbol alone does not identify a Fable backend; our configuration
associates it with the browser build. We had to supply both the compiler define
and the MSBuild property when asking fsharp-refactor to analyze that version.

Analysis ran a build that replaced the shared package-resolution file,
`obj/project.assets.json`, with the browser version. Our next .NET test build
used `--no-restore` and failed because it needed the .NET packages instead.
Giving the browser build separate intermediate and output folders fixed this
for all tools, not just fsharp-refactor.

**Suggestion:** provide a small Fable-to-JavaScript integration example showing
how to select the intended code and packages. Explain which build/restore steps
analysis runs and which files it may change even without editing source. Where browser and
.NET builds use different packages, recommend separate build folders configured
by the project; the tool need not choose those folders itself.

## 4. Make API-scope controls easier to understand

The two settings sound similar, but `true` has opposite effects:

- `publicApi: true` means "protect public declarations from changes."
- `apiChanges: true` means "allow broader changes, including changes to public
  declarations and their callers"; it also implies `publicApi: false`.

For example, FR0022 suggested naming the fields in a public union case such as
`Point of float * float`. With `publicApi: true`, it reported the suggestion as
"held back by scope" rather than applying it. The command still succeeded with
`--fail-on-findings`. It took investigation to understand which setting prevented
the edit and why the suggestion did not fail the build.

**Suggestion:** use names that distinguish protection from permission, and make
each held message name the setting that prevented the edit. Explain how users
can inspect or authorize the change, while making clear that leaving it
protected does not prevent validation from passing. Consider using a single
tri-state setting instead of two Booleans with one invalid combination ("make
illegal states unrepresentable").

## 5. Clarify finding states and exit semantics

We needed experiments to distinguish automatic edits, advisory-only notes, and
scope-held suggestions. Advisory details required `--notes`. With
`--fail-on-findings`, a successful automatic fix could still fail the invocation
and require a clean rerun, while a scope-held suggestion alone did not fail it.

**Suggestion:** summarize applied edits, remaining actionable findings,
advisory-only findings, and scope-held suggestions separately, and state which
caused the exit status. Document whether `--fail-on-findings`, when automatic
fixes are enabled, checks findings encountered or findings remaining after fixes.

## 6. Explain advisory assumptions and performance trade-offs

FR0169 flagged repeated enumeration where an adapter already materialized the
collection. For the resulting JavaScript array, the Fable JavaScript runtime
library's implementation of `Seq.isEmpty` already checked array length. The
warning did not establish repeated underlying computation in that case.

FR0058 identified recursive-sequence enumerator overhead. Our agent-generated fix
replaced recursive sequences with recursive list concatenation, but introduced
copying, allocations, and lost laziness in the generated JavaScript; it was not
an assured improvement. That replacement was our implementation, not an
automatic tool rewrite.

We also excluded these representation rules from our JavaScript-targeted
performance checks:

| Rules          | Suggested change                                                                               | Why we excluded it                                                                                |
| -------------- | ---------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------- |
| FR0016, FR0070 | Add `[<Struct>]` to small unions or records                                                    | JavaScript does not gain CLR value-type storage from the attribute.                               |
| FR0059, FR0069 | Replace private option results or optional record fields with `ValueOption`                    | The CLR allocation-saving rationale does not carry over to the JavaScript runtime representation. |
| FR0011, FR0093 | Use struct partial active patterns with `ValueSome`/`ValueNone`, or struct tuple record fields | These likewise rely on value-type representation benefits that do not transfer to JavaScript.     |

These are backend-specific choices, not objections to the rules in general:
struct representation can be useful when Fable targets Rust.

**Suggestion:** distinguish potential risks from proven defects, state runtime
and collection assumptions, and explain trade-offs and unsuitable remedies.
Build on the tool's existing Fable/backend awareness by explaining which
performance claims apply to which backends and runtimes, and where applicability
is unknown. A Fable compiler target and the runtime library used by its output
are distinct considerations; neither implies CLR performance characteristics.

## 7. Make FR0182's automatic rename optional

FR0182 usefully detected shadowing of `id`, but automatically replaced it with
`pid`. This avoided shadowing without conveying meaning; names such as
`entityId` or `personId` were appropriate in different contexts for our application.

**Suggestion:** improve replacement-name selection and allow FR0182 to be
configured as either an automatic rewrite or advisory-only. When the tool cannot
infer a meaningful name, users should be able to retain the diagnostic without
accepting a low-quality rename or disabling the rule entirely.
