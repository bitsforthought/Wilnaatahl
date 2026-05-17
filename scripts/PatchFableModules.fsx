// Workaround for a Fable TypeScript codegen bug (still present in Fable 5.1.0):
// generic methods on generic interfaces (e.g. Thoth.Json.Core's
// `IRequiredGetter.Field`, `IEncoder<JsonValue>.Encode<T>`) emit an unbound
// `$a` in parameter type annotations. The runtime JS is correct; only tsc
// trips on the bad annotations.
//
// We can't edit the generated files directly (they're regenerated on every
// `npm run fable`), so instead we prepend `// @ts-nocheck` to the specific
// `.fs.ts` files known to contain the offending pattern. These are
// third-party libraries we don't author; their type-checking adds no value
// and the runtime behavior is unaffected.
//
// Match patterns use the package family name (without version) so the script
// keeps working across Thoth/Fable point-release upgrades without needing an
// allow-list update — the same three files trigger the bug regardless of
// version directory naming (`Thoth.Json.Core.0.8.0` vs `0.9.0`, etc.).
//
// This script runs as a post-fable step in package.json.

open System.IO

type PatchOutcome =
    | RootMissing of path: string
    | NoMatches of patterns: string list
    | Patched of files: string list * skipped: string list

let marker =
    "// @ts-nocheck (fable codegen patch — see scripts/PatchFableModules.fsx)\n"

// Each entry pairs a package-directory glob with a list of `.fs.ts` files
// inside that package that are known to emit the `$a` codegen bug. Keep this
// list as narrow as possible — every entry suppresses type-checking on the
// matching file, so additions should be backed by a reproducible `tsc` error.
let bugTriggers =
    [ "Thoth.Json.Core.*", [ "Decode.fs.ts"; "Encode.fs.ts" ]
      "Thoth.Json.JavaScript.*", [ "Encode.fs.ts" ] ]

let resolveTargets (root: string) (triggers: (string * string list) list) =
    if not (Directory.Exists root) then
        []
    else
        triggers
        |> List.collect (fun (packageGlob, files) ->
            Directory.GetDirectories(root, packageGlob)
            |> List.ofArray
            |> List.collect (fun packageDir ->
                files
                |> List.map (fun file -> Path.Combine(packageDir, file))
                |> List.filter File.Exists))

let isAlreadyPatched (path: string) =
    File.ReadAllText(path).StartsWith(marker)

let prependMarker (path: string) =
    let original = File.ReadAllText(path)
    File.WriteAllText(path, marker + original)

let patchAll (paths: string list) =
    let alreadyPatched, toPatch = paths |> List.partition isAlreadyPatched

    for path in toPatch do
        prependMarker path

    toPatch, alreadyPatched

// --- Top-level I/O and control flow ---

let repoRoot = Path.GetDirectoryName(Path.GetFullPath(__SOURCE_DIRECTORY__))
let fableModulesRoot = Path.Combine(repoRoot, "src", "generated", "fable_modules")

let outcome =
    if not (Directory.Exists fableModulesRoot) then
        RootMissing fableModulesRoot
    else
        let targets = resolveTargets fableModulesRoot bugTriggers

        if List.isEmpty targets then
            let patternStrings =
                bugTriggers
                |> List.collect (fun (pkg, files) -> files |> List.map (fun f -> sprintf "%s/%s" pkg f))

            NoMatches patternStrings
        else
            let patched, skipped = patchAll targets
            Patched(patched, skipped)

match outcome with
| RootMissing path ->
    eprintfn $"ERROR: %s{path} not found. Run `npm run fable` first."
    exit 1
| NoMatches patterns ->
    eprintfn $"ERROR: PatchFableModules.fsx found no files matching any of:"

    for p in patterns do
        eprintfn $"  %s{p}"

    eprintfn $"If the upstream codegen bug has been fixed, remove this script."
    exit 1
| Patched(patched, skipped) ->
    printfn
        $"PatchFableModules: prepended @ts-nocheck to %d{patched.Length} file(s), %d{skipped.Length} already patched."
