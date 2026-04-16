open System.IO
open System.Text.Json

type ParseError =
    | FileNotFound of path: string
    | PropertyNotFound of property: string * path: string

type CheckResult =
    | Pass
    | Improved of current: float
    | Regressed of current: float * baseline: float

let tryGetProperty (property: string) (element: JsonElement) =
    match element.TryGetProperty(property) with
    | true, prop -> Ok prop
    | false, _ -> Error property

let tryGetDouble (property: string) (element: JsonElement) =
    tryGetProperty property element |> Result.map (fun prop -> prop.GetDouble())

let parseBaseline (json: string) path =
    let doc = JsonDocument.Parse(json)

    tryGetDouble "lineCoverage" doc.RootElement
    |> Result.mapError (fun prop -> PropertyNotFound(prop, path))

let parseSummary (json: string) path =
    let doc = JsonDocument.Parse(json)

    tryGetProperty "summary" doc.RootElement
    |> Result.mapError (fun prop -> PropertyNotFound(prop, path))
    |> Result.bind (fun summary ->
        tryGetDouble "linecoverage" summary
        |> Result.mapError (fun prop -> PropertyNotFound(prop, path)))

let checkCoverage current baseline =
    if current < baseline then Regressed(current, baseline)
    elif current > baseline then Improved current
    else Pass

// --- Top-level I/O and control flow ---

let repoRoot = Path.GetDirectoryName(Path.GetFullPath(__SOURCE_DIRECTORY__))
let baselinePath = Path.Combine(repoRoot, "coverage-baseline.json")
let summaryPath = Path.Combine(repoRoot, "coveragereport", "Summary.json")

let readFile path =
    if File.Exists(path) then
        Ok(File.ReadAllText(path))
    else
        Error(FileNotFound path)

let exitWithError error =
    match error with
    | FileNotFound path -> eprintfn "ERROR: %s not found." path
    | PropertyNotFound(prop, path) -> eprintfn "ERROR: Property '%s' not found in %s." prop path

    exit 1

let baseline =
    readFile baselinePath
    |> Result.bind (fun json -> parseBaseline json baselinePath)
    |> function
        | Ok value -> value
        | Error e -> exitWithError e

let current =
    readFile summaryPath
    |> Result.bind (fun json -> parseSummary json summaryPath)
    |> function
        | Ok value -> value
        | Error e -> exitWithError e

printfn "Line coverage: %.1f%% (baseline: %.1f%%)" current baseline

match checkCoverage current baseline with
| Regressed(current, baseline) ->
    eprintfn "FAIL: Line coverage regressed from %.1f%% to %.1f%%." baseline current
    exit 1
| Improved current ->
    printfn "PASS: Line coverage improved from %.1f%% to %.1f%%." baseline current
    let newBaseline = sprintf "{\n  \"lineCoverage\": %.1f\n}\n" current
    File.WriteAllText(baselinePath, newBaseline)
    printfn "Updated %s with new baseline." baselinePath
| Pass -> printfn "PASS: Line coverage meets or exceeds baseline."
