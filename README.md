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

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for how to propose changes and the
validation gate that pull requests must pass.

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

#### Optional: Browser E2E testing

Browser E2E tests use Docker with Linux containers. Install and start Docker
Desktop manually using the
[Windows installation guide](https://docs.docker.com/desktop/setup/install/windows-install/).
The engine must be configured for Linux containers; Windows 11 may require WSL 2
and hardware virtualization. Do not install or enable these machine-level
features through project scripts.

On macOS, use [Docker Desktop](https://docs.docker.com/desktop/setup/install/mac-install/)
or another Linux-container engine. On Linux, install and start a compatible
[Docker Engine](https://docs.docker.com/engine/install/). The project uses a
Linux/amd64 browser image; ARM machines may rely on emulation and should be
validated in that environment.

Verify that the engine is running:

```powershell
docker version
docker info --format '{{.OSType}}/{{.Architecture}}'
```

The first browser-test run downloads the pinned Playwright image. Docker is
needed only for browser E2E tests and screenshot-reference updates; it is not
required for ordinary setup, application use, unit tests, or `npm run validate`.
The separate browser E2E workflow is non-gating: it runs on pushes to `main`,
nightly, or manual dispatch, not on pull requests. GitHub only offers manual
dispatch after the workflow exists on the default branch; it can then target a
selected branch.
See [`docs/build.md`](docs/build.md#browser-e2e-tests) for the run commands and
operational details.

### Common Development Commands

The npm scripts delegate to the FAKE build:

| Command            | Behaviour                                                                                 |
| ------------------ | ----------------------------------------------------------------------------------------- |
| `npm run dev`      | Generate the Fable output and start the development server.                               |
| `npm run build`    | Run build checks and create the deployment bundle.                                        |
| `npm test`         | Run both complete test suites, including Koota conformance.                               |
| `npm run validate` | Run build checks, infrastructure self-tests, instrumented suites and coverage gates once. |
| `npm run selftest` | Apply enabled F# refactorings and format locally, then run infrastructure self-tests.     |
| `npm run format`   | Format authored files with Fantomas and Prettier without applying F# refactorings.        |
| `npm run refactor` | Apply enabled F# refactorings, then format authored files with Fantomas and Prettier.     |
| `npm run report`   | Generate fresh coverage and open both HTML reports locally.                               |

Local build, test, dev, validate, self-test, and report targets apply enabled F#
refactorings before formatting. `npm run format` runs the formatters only, while
`npm run refactor` applies rewrites and then formats. CI uses report-only analysis;
it does not write fixes, but findings that survive the configured filters still
fail the check.

See [`docs/build.md`](docs/build.md) for focused targets, CI mode, and other
build options.

To host the deployment-ready build locally for testing: `npx serve dist`.
