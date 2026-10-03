# Wilnaatahl

A tool for visualizing the genealogical relationships of Gitxsan huwilp members.

## Using Wilnaatahl

When you open Wilnaatahl, it boots straight into a visualization of the bundled
sample dataset, so you can see what the tool does without any setup. The toolbar
gives you two file actions:

- **Open file…** — opens your operating system's file picker immediately. Pick a
  JSON file from your device to replace the current visualization with your own
  data. A file that can't be parsed leaves the current visualization in place and
  shows a dismissible error; a file that loads with minor issues shows a
  dismissible warning summary (with per-record details in the browser's dev
  console).
- **Save…** — exports the currently displayed graph to a JSON file, downloaded to
  your browser's default download location.

Everything happens locally in your browser — files are never uploaded to a
server. There is no persistence between visits: the app always starts on the
sample data.

For the supported file format, see [`specs/json-parser.md`](specs/json-parser.md).

## Development Instructions

These instructions assume Windows 11 and PowerShell, but should be easily adaptable to other environments.

### 🛠️ Dev Environment Setup (Windows 11, PowerShell)

These instructions assume **Git** and **Visual Studio Code** are already installed.

---

#### ✅ 1. Install Node Version Manager for Windows (nvm-windows)

> Allows you to install and switch between Node.js versions easily on Windows.

1. Download and install the latest `nvm-setup.exe` from:  
   👉 https://github.com/coreybutler/nvm-windows/releases

2. Open a new PowerShell window, then install and use the Node.js version
   recorded in `.nvmrc` (currently Node 24 LTS):

```powershell
$nodeVersion = (Get-Content .nvmrc).Trim()
nvm install $nodeVersion
nvm use $nodeVersion
```

3. Verify the installation:

```powershell
node -v    # Should match .nvmrc
npm -v
```

#### ✅ 2. Install the .NET SDK

This project targets .NET 10. To make setup easier we pin the SDK via `global.json` so the correct SDK is used automatically by the `dotnet` CLI.

If you don't have the pinned SDK installed, install any 10.0.x SDK from:

👉 https://dotnet.microsoft.com/download

Verify installation:

```powershell
dotnet --version   # Should return 10.0.x
```

#### ✅ 3. Restore Local Tools and Dependencies

From the project root, run:

```powershell
npm run init
```

This installs npm dependencies and restores the .NET solution and local tools.
For CI-style clean initialization, use `npm.cmd run init -- --ci`; that selects
`npm ci` before the same .NET restores.

### Commands for Dev Inner Loop

The npm scripts are a thin facade over the FAKE target graph:

| Command                             | Behaviour                                                                                 |
| ----------------------------------- | ----------------------------------------------------------------------------------------- |
| `npm run init`                      | Install npm packages and restore .NET packages and tools.                                 |
| `npm run fake -- --target <Target>` | Run a focused target or list targets with `--help`.                                       |
| `npm run build`                     | Format locally, validate agent definitions, type-check, lint and bundle for deployment.   |
| `npm run dev`                       | Format locally, generate/patch Fable output and start Vite.                               |
| `npm test`                          | Run both complete test suites, including Koota conformance.                               |
| `npm run validate`                  | Run build checks, infrastructure self-tests, instrumented suites and coverage gates once. |
| `npm run format`                    | Format authored files with Fantomas and Prettier.                                         |
| `npm run report`                    | Generate fresh coverage and open both HTML reports locally.                               |

Use `npm run validate` as the complete build/test/coverage gate; it avoids
re-running suites just to collect coverage. For local diagnosis, choose a target
such as `TestKoota`, `CoverageCheck`, `Lint`, or `FormatCheck` through the `fake`
command. CI-style validation is explicit:

```powershell
npm.cmd run validate -- --ci
npm.cmd run validate -- --ci --parallel 1
```

In Windows PowerShell, use `npm.cmd` rather than the `npm` PowerShell shim when
forwarding FAKE options, for example `npm.cmd run validate -- --ci`. The shim
can consume flags such as `--ci` or `--target` before they reach FAKE. On
Unix-like shells, use `npm run validate -- --ci` and
`npm run fake -- --target TestKoota`.

In `--ci` mode formatting is check-only, coverage improvements do not update
`coverage-baseline.json`, and report/browser and development-server targets are
rejected. Use `npm.cmd run fake -- --help` in PowerShell or
`npm run fake -- --help` in Unix-like shells to list targets and options.
`--parallel` sets FAKE's worker limit, not a guarantee of parallel execution:
FAKE may serialize sibling targets that share a prerequisite. The same direct
entry point works before npm dependencies are installed:
`dotnet fsi --warnaserror --warnon:3886 scripts/Build.fsx -- --target Init --ci`.

Former focused npm commands map to FAKE targets as follows:

| Former command                                           | Replacement target                      |
| -------------------------------------------------------- | --------------------------------------- |
| `npm run fantomas` / `npm run prettier`                  | `Format` or `FormatCheck`               |
| `npm run fable`                                          | `Fable`                                 |
| `npm run lint`                                           | `Lint`                                  |
| `npm run validate:agents`                                | `ValidateAgents`                        |
| `npm run test:ts` / `npm run test:koota`                 | `TestTypeScript` / `TestKoota`          |
| `npm run coverage`                                       | `Coverage`                              |
| `npm run coverage:fsharp` / `npm run coverage:ts`        | `CoverageFSharp` / `CoverageTypeScript` |
| `npm run coverage:check`                                 | `CoverageCheck`                         |
| `npm run report:fsharp` / `npm run report:ts`            | `ReportFSharp` / `ReportTypeScript`     |
| `npm run test:ts:prepare` / `npm run test:koota:prepare` | `Fable` and `PrepareEcs`                |

Use `npm.cmd run fake -- --target <Target>` in PowerShell or
`npm run fake -- --target <Target>` in Unix-like shells to select the named
target. `preview` was unused and has been removed without replacement.

For future GitHub Actions setup, read the Node version from `.nvmrc`, then run
the direct `Init --ci` and `Validate --ci` targets; this repository does not yet
include an Actions workflow.

To host the deployment-ready build locally for testing: `npx serve dist`.
