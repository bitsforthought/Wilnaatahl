open System
open System.Globalization
open System.IO
open System.Text.Json

type ParseError =
    | FileNotFound of path: string
    | InvalidJson of path: string * message: string
    | PropertyNotFound of property: string * path: string
    | InvalidNumber of property: string * path: string

type MetricPropertyError =
    | MissingMetricProperty of property: string
    | NonNumericMetricProperty of property: string

type CheckResult =
    | Pass
    | Improved of current: float
    | Regressed of current: float * baseline: float

/// A coverage metric tracked against a watermark in a summary document.
type Metric =
    { Label: string
      BaselineKey: string
      SummaryKey: string }

type CoverageSuite =
    { Label: string
      SummaryPath: string
      Metrics: Metric list }

let lineMetric =
    { Label = "Line coverage"
      BaselineKey = "lineCoverage"
      SummaryKey = "linecoverage" }

let branchMetric =
    { Label = "Branch coverage"
      BaselineKey = "branchCoverage"
      SummaryKey = "branchcoverage" }

let typeScriptLineMetric =
    { Label = "Line coverage"
      BaselineKey = "tsLineCoverage"
      SummaryKey = "linecoverage" }

let typeScriptBranchMetric =
    { Label = "Branch coverage"
      BaselineKey = "tsBranchCoverage"
      SummaryKey = "branchcoverage" }

let fSharpMetrics = [ lineMetric; branchMetric ]
let typeScriptMetrics = [ typeScriptLineMetric; typeScriptBranchMetric ]

let tryGetProperty (property: string) (element: JsonElement) =
    match element.TryGetProperty(property) with
    | true, prop -> Ok prop
    | false, _ -> Error property

let tryGetDouble (property: string) (element: JsonElement) =
    tryGetProperty property element
    |> Result.mapError MissingMetricProperty
    |> Result.bind (fun prop ->
        if prop.ValueKind = JsonValueKind.Number then
            Ok(prop.GetDouble())
        else
            Error(NonNumericMetricProperty property))

let parseJson (path: string) (text: string) =
    try
        Ok(JsonDocument.Parse(text))
    with :? JsonException as ex ->
        Error(InvalidJson(path, ex.Message))

let parseBaseline (metric: Metric) (doc: JsonDocument) path =
    tryGetDouble metric.BaselineKey doc.RootElement
    |> Result.mapError (fun error ->
        match error with
        | MissingMetricProperty property -> PropertyNotFound(property, path)
        | NonNumericMetricProperty property -> InvalidNumber(property, path))

let parseSummary (metric: Metric) (doc: JsonDocument) path =
    tryGetProperty "summary" doc.RootElement
    |> Result.mapError (fun prop -> PropertyNotFound(prop, path))
    |> Result.bind (fun summary ->
        tryGetDouble metric.SummaryKey summary
        |> Result.mapError (fun error ->
            match error with
            | MissingMetricProperty property -> PropertyNotFound(property, path)
            | NonNumericMetricProperty property -> InvalidNumber(property, path)))

let checkCoverage current baseline =
    if current < baseline then Regressed(current, baseline)
    elif current > baseline then Improved current
    else Pass

let sequenceResults results =
    List.fold
        (fun state parsed ->
            state
            |> Result.bind (fun values -> parsed |> Result.map (fun value -> value :: values)))
        (Ok [])
        results
    |> Result.map List.rev

let selectTypeScriptSuite path (root: JsonElement) =
    match tryGetProperty "tsLineCoverage" root, tryGetProperty "tsBranchCoverage" root with
    | Ok _, Ok _ -> Ok true
    | Error _, Error _ -> Ok false
    | Ok _, Error property
    | Error property, Ok _ -> Error(PropertyNotFound(property, path))

let evaluateCoverage baselineText suites summaries =
    let parseSuiteMetrics baselineDocument suite =
        suite.Metrics
        |> List.map (fun metric ->
            parseBaseline metric baselineDocument "coverage-baseline.json"
            |> Result.map (fun baseline -> suite, metric, baseline))
        |> sequenceResults

    let evaluateMetric (suite, metric, baseline) =
        match Map.tryFind suite.SummaryPath summaries with
        | None -> Error(FileNotFound suite.SummaryPath)
        | Some summaryText ->
            parseJson suite.SummaryPath summaryText
            |> Result.bind (fun summaryDocument ->
                parseSummary metric summaryDocument suite.SummaryPath
                |> Result.map (fun current -> suite, metric, current, baseline, checkCoverage current baseline))

    parseJson "coverage-baseline.json" baselineText
    |> Result.bind (fun baselineDocument ->
        suites
        |> List.map (parseSuiteMetrics baselineDocument)
        |> sequenceResults
        |> Result.map List.concat
        |> Result.bind (List.map evaluateMetric >> sequenceResults))

let errorMessage error =
    match error with
    | FileNotFound path -> $"ERROR: {path} not found."
    | InvalidJson(path, message) -> $"ERROR: Invalid JSON in {path}: {message}"
    | PropertyNotFound(prop, path) -> $"ERROR: Property '{prop}' not found in {path}."
    | InvalidNumber(prop, path) -> $"ERROR: Property '{prop}' in {path} must be a number."

let failWithError error =
    eprintfn "%s" (errorMessage error)
    exit 1

let unwrapResult result =
    match result with
    | Ok value -> value
    | Error error -> failWithError error

let writeBaseline path results =
    let properties =
        results
        |> List.map (fun (_, metric, (current: float), _, _) ->
            let formattedCurrent = current.ToString("F1", CultureInfo.InvariantCulture)
            $"  \"{metric.BaselineKey}\": {formattedCurrent}")
        |> String.concat ",\n"

    File.WriteAllText(path, $"{{\n{properties}\n}}\n")

let runSelfTest () =
    let assertEqual name expected actual =
        if expected <> actual then
            failwith (sprintf "Self-test failed: %s. Expected %A, got %A." name expected actual)

    let fSharpSuite =
        { Label = "F#"
          SummaryPath = "fsharp/Summary.json"
          Metrics = fSharpMetrics }

    let typeScriptSuite =
        { Label = "TypeScript"
          SummaryPath = "typescript/Summary.json"
          Metrics = typeScriptMetrics }

    let baseline =
        """{"lineCoverage":98.6,"branchCoverage":93.2,"tsLineCoverage":87.5,"tsBranchCoverage":76.0}"""

    let summaries =
        Map
            [ ("fsharp/Summary.json", """{"summary":{"linecoverage":99.0,"branchcoverage":92.0}}""")
              ("typescript/Summary.json", """{"summary":{"linecoverage":88.0,"branchcoverage":76.0}}""") ]

    let mixedResults =
        evaluateCoverage baseline [ fSharpSuite; typeScriptSuite ] summaries

    assertEqual
        "mixed improvement and regression"
        (Ok
            [ (fSharpSuite, lineMetric, 99.0, 98.6, Improved 99.0)
              (fSharpSuite, branchMetric, 92.0, 93.2, Regressed(92.0, 93.2))
              (typeScriptSuite, typeScriptLineMetric, 88.0, 87.5, Improved 88.0)
              (typeScriptSuite, typeScriptBranchMetric, 76.0, 76.0, Pass) ])
        mixedResults

    assertEqual
        "JSON parsing"
        (Ok 98.6)
        (parseJson "baseline" baseline
         |> Result.bind (fun document -> parseBaseline lineMetric document "baseline"))

    assertEqual
        "missing baseline key"
        (Error(PropertyNotFound("tsLineCoverage", "coverage-baseline.json")))
        (evaluateCoverage
            """{"lineCoverage":98.6,"branchCoverage":93.2,"tsBranchCoverage":76.0}"""
            [ fSharpSuite; typeScriptSuite ]
            summaries)

    assertEqual
        "non-numeric baseline metric"
        (Error(InvalidNumber("lineCoverage", "coverage-baseline.json")))
        (evaluateCoverage
            """{"lineCoverage":"98.6","branchCoverage":93.2,"tsLineCoverage":87.5,"tsBranchCoverage":76.0}"""
            [ fSharpSuite; typeScriptSuite ]
            summaries)

    let invalidJsonResult = parseJson "baseline" "{invalid"

    let isInvalidJson =
        match invalidJsonResult with
        | Error(InvalidJson("baseline", _)) -> true
        | _ -> false

    assertEqual "invalid JSON" true isInvalidJson

    let oldBaselineRoot =
        parseJson "old-baseline" """{"lineCoverage":98.6,"branchCoverage":93.2}"""
        |> Result.map (fun document -> document.RootElement)

    let newBaselineRoot =
        parseJson "new-baseline" baseline
        |> Result.map (fun document -> document.RootElement)

    assertEqual
        "two-key baseline keeps the legacy suite"
        (Ok false)
        (oldBaselineRoot |> Result.bind (selectTypeScriptSuite "old-baseline"))

    assertEqual
        "four-key baseline enables both suites"
        (Ok true)
        (newBaselineRoot |> Result.bind (selectTypeScriptSuite "new-baseline"))

    let partialBaselineRoot =
        parseJson "partial-baseline" """{"lineCoverage":98.6,"branchCoverage":93.2,"tsLineCoverage":87.5}"""
        |> Result.map (fun document -> document.RootElement)

    assertEqual
        "partial baseline is rejected"
        (Error(PropertyNotFound("tsBranchCoverage", "partial-baseline")))
        (partialBaselineRoot |> Result.bind (selectTypeScriptSuite "partial-baseline"))

    assertEqual
        "missing summary file"
        (Error(FileNotFound "typescript/Summary.json"))
        (evaluateCoverage baseline [ fSharpSuite; typeScriptSuite ] (summaries |> Map.remove "typescript/Summary.json"))

    assertEqual
        "first missing summary is reported first"
        (Error(FileNotFound "fsharp/Summary.json"))
        (evaluateCoverage baseline [ fSharpSuite; typeScriptSuite ] Map.empty)

    assertEqual
        "missing summary property"
        (Error(PropertyNotFound("summary", "typescript/Summary.json")))
        (evaluateCoverage
            baseline
            [ fSharpSuite; typeScriptSuite ]
            (summaries |> Map.add "typescript/Summary.json" """{"details":{}}"""))

    assertEqual
        "missing summary metric"
        (Error(PropertyNotFound("linecoverage", "typescript/Summary.json")))
        (evaluateCoverage
            baseline
            [ fSharpSuite; typeScriptSuite ]
            (summaries |> Map.add "typescript/Summary.json" """{"summary":{"details":{}}}"""))

    let invalidSummary =
        evaluateCoverage
            baseline
            [ fSharpSuite; typeScriptSuite ]
            (summaries |> Map.add "typescript/Summary.json" "{invalid")

    let isInvalidSummary =
        match invalidSummary with
        | Error(InvalidJson("typescript/Summary.json", _)) -> true
        | _ -> false

    assertEqual "invalid summary JSON" true isInvalidSummary

    let rewritten =
        mixedResults
        |> Result.map (fun results ->
            let temporaryPath = Path.GetTempFileName()
            writeBaseline temporaryPath results
            let text = File.ReadAllText temporaryPath
            File.Delete temporaryPath
            text)

    let rewrittenKeys =
        rewritten
        |> Result.bind (parseJson "rewritten")
        |> Result.map (fun document ->
            [ "lineCoverage"; "branchCoverage"; "tsLineCoverage"; "tsBranchCoverage" ]
            |> List.map (fun key ->
                tryGetDouble key document.RootElement
                |> Result.mapError (fun error ->
                    match error with
                    | MissingMetricProperty property -> PropertyNotFound(property, "rewritten")
                    | NonNumericMetricProperty property -> InvalidNumber(property, "rewritten"))
                |> Result.map (fun value -> key, value)))

    assertEqual
        "rewriting retains all four keys"
        (Ok
            [ ("lineCoverage", 99.0)
              ("branchCoverage", 92.0)
              ("tsLineCoverage", 88.0)
              ("tsBranchCoverage", 76.0) ])
        (rewrittenKeys |> Result.bind sequenceResults)

    printfn "Self-test: passed."

// --- Top-level I/O and control flow ---

let repoRoot = Path.GetDirectoryName(Path.GetFullPath(__SOURCE_DIRECTORY__))
let baselinePath = Path.Combine(repoRoot, "coverage-baseline.json")
let fSharpSummaryPath = Path.Combine(repoRoot, "coveragereport", "Summary.json")

let typeScriptSummaryPath =
    Path.Combine(repoRoot, "coveragereport-ts", "Summary.json")

let scriptArgs = fsi.CommandLineArgs |> Array.skip 1

if scriptArgs |> Array.contains "--self-test" then
    runSelfTest ()
else
    let baselineTextResult =
        if File.Exists baselinePath then
            Ok(File.ReadAllText baselinePath)
        else
            Error(FileNotFound baselinePath)

    let baselineDocument =
        baselineTextResult
        |> Result.bind (parseJson baselinePath)
        |> Result.map (fun document -> document.RootElement)

    // An existing two-key baseline keeps the legacy F# workflow working; a
    // four-key baseline is the explicit switch that makes the TS summary required.
    let useTypeScriptSuite =
        baselineDocument
        |> Result.bind (selectTypeScriptSuite baselinePath)
        |> unwrapResult

    let suites =
        if useTypeScriptSuite then
            [ { Label = "F#"
                SummaryPath = fSharpSummaryPath
                Metrics = fSharpMetrics }
              { Label = "TypeScript"
                SummaryPath = typeScriptSummaryPath
                Metrics = typeScriptMetrics } ]
        else
            [ { Label = "F#"
                SummaryPath = fSharpSummaryPath
                Metrics = fSharpMetrics } ]

    let summaryTexts =
        suites
        |> List.choose (fun suite ->
            if File.Exists suite.SummaryPath then
                Some(suite.SummaryPath, File.ReadAllText suite.SummaryPath)
            else
                None)
        |> Map.ofList

    let baselineText = baselineTextResult |> unwrapResult

    let results = evaluateCoverage baselineText suites summaryTexts |> unwrapResult

    for suite, metric, current, baseline, result in results do
        printfn "%s %s: %.1f%% (baseline: %.1f%%)" suite.Label metric.Label current baseline

        match result with
        | Regressed(current, baseline) ->
            eprintfn "FAIL: %s %s regressed from %.1f%% to %.1f%%." suite.Label metric.Label baseline current
        | Improved current -> printfn "PASS: %s %s improved to %.1f%%." suite.Label metric.Label current
        | Pass -> printfn "PASS: %s %s meets or exceeds baseline." suite.Label metric.Label

    let anyRegressed =
        results
        |> List.exists (fun (_, _, _, _, result) ->
            match result with
            | Regressed _ -> true
            | _ -> false)

    if anyRegressed then
        exit 1
    elif
        results
        |> List.exists (fun (_, _, _, _, result) ->
            match result with
            | Improved _ -> true
            | _ -> false)
    then
        writeBaseline baselinePath results
        printfn "Updated %s with new baseline." baselinePath
