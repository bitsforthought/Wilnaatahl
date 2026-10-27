# Build and Validation

The npm scripts are a thin facade over the FAKE target graph in
`scripts/Build.fsx`.

## Commands

| Command            | Behaviour                                                                                                                                         |
| ------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------- |
| `npm run init`     | Install npm packages and restore .NET packages and tools.                                                                                         |
| `npm run build`    | Apply enabled F# refactorings, format locally, validate agent definitions, type-check, lint, and bundle for deployment.                           |
| `npm run dev`      | Apply enabled F# refactorings, format locally, generate and bridge Fable output, and start Vite.                                                  |
| `npm test`         | Apply enabled F# refactorings and format locally, then run both complete test suites, including Koota conformance.                                |
| `npm run validate` | Apply enabled F# refactorings and format locally, then run build checks, infrastructure self-tests, instrumented suites, and coverage gates once. |
| `npm run selftest` | Apply enabled F# refactorings and format locally, then run infrastructure self-tests without the full validation gate.                            |
| `npm run format`   | Format authored files with Fantomas and Prettier without applying F# refactorings.                                                                |
| `npm run refactor` | Apply enabled F# refactorings, then format authored files with Fantomas and Prettier.                                                             |
| `npm run report`   | Apply enabled F# refactorings and format, then generate fresh coverage and open both HTML reports locally.                                        |

Run a focused target with:

```text
npm run fake "--" --target <Target>
```

Quoting the npm argument separator keeps this form consistent across PowerShell,
`cmd.exe`, and Unix-like shells. Run `npm run fake "--" --help` to list the
available targets and options.

Useful focused targets include `Format`, `FormatCheck`, `RewriteSource`,
`Refactor`, `RefactorCheck`, `FableCompileSrc`, `FableCompileTests`, `TestFSharp`, `TestTypeScript`,
`TestKoota`, `BuildE2E`, `TestE2E`, `UpdateE2ESnapshots`, `SelfTest`,
`TestWithCoverage`, `TestWithCoverageCheck`, `Lint`, `ReportFSharp`, and
`ReportTypeScript`.

`SelfTest` runs the infrastructure self-tests as one aggregate; `Validate`
depends on that same target. Use `npm run selftest` for focused feedback when
changing build, CI, or code-generation scripts. Its component targets are
internal leaves and are not selectable directly.

`Format` runs Fantomas and Prettier without applying refactorings.
`Refactor` applies enabled mechanical fixes, and `RewriteSource` applies
those fixes before formatting. `RefactorCheck` previews findings without
changing source. The refactor targets print findings to the console and scan F#
projects and scripts, including new files not yet tracked in Git.

Rule settings live in `fsharprefactor.json`. Performance rules run only on
browser production code, using the allowlist in
`.config/fsharp-refactor-performance.json`; tests, infrastructure and CLR mocks
are excluded from that pass.

Both modes use `--fail-on-findings`: any finding that survives the configured
filters, including an advisory note with no automatic fix, fails the target.
A local run that applies fixes also fails for the findings it encountered;
inspect those edits, address remaining advisories, and rerun until clean.
Tool execution failures and malformed configuration also fail the target,
because the tool silently uses its default rules when `fsharprefactor.json`
is malformed.

## Generated TypeScript

`FableCompileSrc` compiles Core and its referenced ECS project into `src/generated`,
then generates the stable Fable runtime bridge. `FableCompileTests` compiles the
portable ECS tests and their referenced ECS project into `tests/generated`.
The compilation targets are independent: each emits its own dependency copies,
and compiling tests does not populate the source output or its runtime bridge.
TypeScript tests also import application code, so test execution and the
monolithic `TypeCheck` target require both generated trees.

TypeScript root-file discovery excludes the vendored Fable runtime and its
generated bridge, configured in `tsconfig.json`. Fable includes runtime modules
for optional features the application does not use, and the bridge re-exports
the whole runtime, so both trees must be excluded from root discovery.
First-party generated files remain roots, and runtime modules reached through
imports are still resolved and type-checked.

## F# Test Platform

The F# test projects run on Microsoft Testing Platform: `global.json` selects
it for `dotnet test`, and each test project builds as an executable. Coverage is collected by coverlet's Microsoft Testing Platform
extension, configured by the shared `tests/testconfig.json`. Running the tests
from an IDE requires a test explorer that supports Microsoft Testing Platform.

## CI Mode

Pass `--ci` through the quoted separator:

```text
npm run init "--" --ci
npm run validate "--" --ci
```

CI mode uses `npm ci`, runs F# refactoring report-only, checks formatting
without writing, prevents coverage baseline updates, and rejects targets that
launch a development server or open a report. The report-only `RefactorCheck`
target can also be run directly in CI mode. Local build, test, dev, validate,
self-test, and focused F# targets apply enabled fixes before Fantomas. Local development should
normally use `npm run init` and `npm run validate` without this option.

## Browser E2E tests

The browser E2E workflow uses the real built application in Chromium. The host
must have the .NET SDK and Node version already used by the build, plus a
running Linux-container engine; setup details are in the
[README prerequisites](../README.md#optional-browser-e2e-testing). Fable
compilation and the isolated E2E bundle run on the host. The Playwright process
uses the built static bundle, test sources, and lockfile from a read-only
repository mount; it does not require or invoke .NET or F# projects.

Run the focused targets from PowerShell:

```powershell
npm run fake "--" --target BuildE2E
npm run fake "--" --target TestE2E
```

`TestE2E` builds the isolated `dist-e2e` output, then starts the pinned
Playwright image. Docker caches the image after its first pull, but each
operation creates a fresh temporary Linux `node_modules` volume and installs
packages from the lockfile again. The host's `node_modules` is never mounted
over it. The Playwright process uses the host UID/GID on Unix so reports and
locally updated references remain writable by the developer. The app preview
server and browser run inside the same container. Playwright reports and
diagnostics remain under `.e2e-output/`.

Ordinary `TestE2E` runs explicitly disable screenshot updates and mount project
files read-only. Updating references is a separate local-only action:

```powershell
npm run fake "--" --target UpdateE2ESnapshots
```

Inspect any generated references before committing them with their test
changes. The update target is rejected in CI mode; verification never writes
missing or changed references. Neither Docker nor browser execution is part of
`npm run validate`.

The visual-regression references cover the desktop Move-mode toolbar, the
desktop selected-person detail card, and import-warning toasts on desktop and
narrow mobile emulation. The detail-card reference uses the imported-family
fixture and captures only Ada North's dialog; it does not baseline the scene
behind it. The warning references also capture only the toast, not the imported
scene.

The sample graph is not screenshot-baselined: at the canonical viewports, the
scene extends beyond the visible area and some labels are clipped or obscured.
Behavioural tests still exercise the scene and camera with real browser input.
The narrow mobile Move-mode toolbar is functionally tested but has no screenshot
reference because the toolbar overflows the viewport and clips controls. These
visual states are not accepted as baselines until their existing behaviour is
addressed separately.

No reference image is stored for known-defect states. The imported-family
overview is excluded because its first node label is missing ([#116](https://github.com/bitsforthought/Wilnaatahl/issues/116));
multi-selection and moved-node connectors cannot be reached while Move-mode
projected selection is blocked ([#117](https://github.com/bitsforthought/Wilnaatahl/issues/117));
and a narrow-screen selected-detail image is blocked by View-mode touch
interception ([#118](https://github.com/bitsforthought/Wilnaatahl/issues/118)).
Edge-positioned detail placement is also not captured: the current synthetic
fixture has no node at an edge, and this layer does not alter graph layout to
manufacture that state. These remain documented gaps rather than approved
visual behaviour.

`tests/e2e/desktop.spec.ts` contains three explicitly named `[known defect pin]`
tests. They currently pass by documenting two out-of-scope application defects:
the first imported node label is absent from the DOM ([#116](https://github.com/bitsforthought/Wilnaatahl/issues/116)), and projected
Move-mode clicks leave multi-selection empty ([#117](https://github.com/bitsforthought/Wilnaatahl/issues/117)). These assertions are diagnostic
pins, not desired behaviour; remove or replace them when the application
defects are fixed. A third pin records that selected-node drag and its
undo/redo path cannot be exercised while projected Move-mode selection is
empty. The remaining intentionally uncovered matrix items are edge-specific
detail placement (the fixture does not place a node at an edge) and redo
invalidation after a successful drag (blocked by the same selection defect).

The mobile Playwright project uses Chromium's Pixel 7 emulation at a narrow
390 × 844 CSS-pixel viewport; it is not physical-device coverage. Use
`page.touchscreen.tap` for taps and the typed CDP touch helper for continuous or
multi-contact gestures. CDP requires `touchEnd` and `touchCancel` requests to have
no touch points, so it cannot target an arbitrary finger for partial release.
The suite avoids asserting how an in-progress gesture continues after one finger
lifts. Tests confirm trusted touch pointer events and observe camera and logical
node state separately.

The touch suite pins two current behaviours: in narrow View mode, a node-label
HTML wrapper intercepts a projected touch instead of opening the detail card
([#118](https://github.com/bitsforthought/Wilnaatahl/issues/118)); in Move mode,
projected touch leaves the node unselected. These are diagnostic
pins, not desired contracts. One-finger background drag rotates the camera;
two-finger spread/contract changes camera distance; parallel two-finger movement
changes the camera target; a subsequent gesture works after contact release or
cancellation. Cancellation verifies pointer cleanup and input recovery, not rollback
of camera movement already applied. The suite does not define how an in-progress
gesture behaves when a second contact arrives or one contact is released.

## Continuous Integration

`.github/workflows/validate.yml` runs the CI-mode commands above on Ubuntu and
Windows for every pull request, every push to `main`, and on manual dispatch. It
installs the Node.js version in `.nvmrc` and the .NET SDK selected by
`global.json`, then delegates everything else to the FAKE target graph; build,
test, lint, and coverage steps are not duplicated in the workflow. Each OS
reports a `Validate (<os>)` check on the pull request. To reproduce a failure
locally, run the two CI-mode commands from the repository root.

Third-party actions are pinned to full commit SHAs, with the release tag in a
trailing comment. Dependabot (.github/dependabot.yml) checks for new action
releases weekly and opens one grouped pull request that updates both the SHA and
the comment.

## Parallelism

FAKE uses up to five workers by default. Override the worker limit when
diagnosing scheduling behaviour:

```text
npm run validate "--" --parallel 1
```

The option sets the maximum worker count. Actual concurrency is constrained by
the target graph, so targets that share an unfinished prerequisite may still run
serially.
