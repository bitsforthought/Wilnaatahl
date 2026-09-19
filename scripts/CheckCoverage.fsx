open System.IO
open System.Text.Json

type ParseError =
    | FileNotFound of path: string
    | PropertyNotFound of property: string * path: string

type CheckResult =
    | Pass
    | Improved of current: float
    | Regressed of current: float * baseline: float

/// A coverage metric tracked against a watermark: its display name, its key in
/// coverage-baseline.json, and its key in the ReportGenerator JsonSummary.
type Metric =
    { Label: string
      BaselineKey: string
      SummaryKey: string }

let lineMetric =
    { Label = "Line coverage"
      BaselineKey = "lineCoverage"
      SummaryKey = "linecoverage" }

let branchMetric =
    { Label = "Branch coverage"
      BaselineKey = "branchCoverage"
      SummaryKey = "branchcoverage" }

let metrics = [ lineMetric; branchMetric ]

let tryGetProperty (property: string) (element: JsonElement) =
    match element.TryGetProperty(property) with
    | true, prop -> Ok prop
    | false, _ -> Error property

let tryGetDouble (property: string) (element: JsonElement) =
    tryGetProperty property element |> Result.map (fun prop -> prop.GetDouble())

let parseBaseline (metric: Metric) (doc: JsonDocument) path =
    tryGetDouble metric.BaselineKey doc.RootElement
    |> Result.mapError (fun prop -> PropertyNotFound(prop, path))

let parseSummary (metric: Metric) (doc: JsonDocument) path =
    tryGetProperty "summary" doc.RootElement
    |> Result.mapError (fun prop -> PropertyNotFound(prop, path))
    |> Result.bind (fun summary ->
        tryGetDouble metric.SummaryKey summary
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

let unwrap result =
    match result with
    | Ok value -> value
    | Error e -> exitWithError e

let baselineDoc = readFile baselinePath |> Result.map JsonDocument.Parse |> unwrap

let summaryDoc = readFile summaryPath |> Result.map JsonDocument.Parse |> unwrap

let results =
    metrics
    |> List.map (fun metric ->
        let baseline = parseBaseline metric baselineDoc baselinePath |> unwrap
        let current = parseSummary metric summaryDoc summaryPath |> unwrap
        printfn "%s: %.1f%% (baseline: %.1f%%)" metric.Label current baseline
        metric, current, checkCoverage current baseline)

let anyRegressed =
    results
    |> List.exists (fun (_, _, result) ->
        match result with
        | Regressed _ -> true
        | _ -> false)

for metric, current, result in results do
    match result with
    | Regressed(current, baseline) -> eprintfn "FAIL: %s regressed from %.1f%% to %.1f%%." metric.Label baseline current
    | Improved current -> printfn "PASS: %s improved to %.1f%%." metric.Label current
    | Pass -> printfn "PASS: %s meets or exceeds baseline." metric.Label

if anyRegressed then
    exit 1
else
    let anyImproved =
        results
        |> List.exists (fun (_, _, result) ->
            match result with
            | Improved _ -> true
            | _ -> false)

    if anyImproved then
        let properties =
            results
            |> List.map (fun (metric, current, _) -> sprintf "  \"%s\": %.1f" metric.BaselineKey current)
            |> String.concat ",\n"

        let newBaseline = sprintf "{\n%s\n}\n" properties
        File.WriteAllText(baselinePath, newBaseline)
        printfn "Updated %s with new baseline." baselinePath
