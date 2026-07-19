module Wilnaatahl.Tests.Persistence.ImportServiceTests

open System
open System.IO
open Xunit
open Swensen.Unquote
open Wilnaatahl.Model
open Wilnaatahl.Model.FamilyGraph
open Wilnaatahl.Persistence
open Wilnaatahl.Persistence.ImportService
open Wilnaatahl.Tests.TestData

// ---------------------------------------------------------------------------
// loadSampleGraph — builds a valid graph from the hardcoded Initial seed data.
// Its specific contents are asserted by the domain and transform unit tests, so
// here we only pin that the boot seed builds a non-empty graph.
// ---------------------------------------------------------------------------

[<Fact>]
let ``loadSampleGraph returns a populated graph`` () =
    loadSampleGraph () |> allPeople |> Seq.isEmpty =! false

/// Builds a NameHeld from text and optional date/order — used by the
/// name-holding import test below.
let private held text nameDate nameOrder : NameHeld = { Name = Name text; NameDate = nameDate; NameOrder = nameOrder }

// ---------------------------------------------------------------------------
// samples/sample.json — the demo showcase must import warning-clean.
// ---------------------------------------------------------------------------

// Read lazily inside each test rather than in a module initializer, so a missing
// or unreadable file fails only the sample.json tests (not the whole module) with
// a legible error.
let private readSampleJson () =
    File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sample.json"))

/// Imports JSON expected to succeed, returning the `ImportResult` (failing the
/// test with a legible message on an unexpected `Error`). Lifts the success value
/// to the top level so tests assert on it directly rather than nesting assertions
/// inside an `Ok`/`Error` match.
let private importOrFail json =
    match importJsonText json with
    | Ok result -> result
    | Error e -> failwithf "Expected a successful import, got %A" e

[<Fact>]
let ``the demo sample.json imports warning-clean`` () =
    (importOrFail (readSampleJson ())).Warnings =! []

// ---------------------------------------------------------------------------
// importJsonText — happy path
// ---------------------------------------------------------------------------

let private validJson =
    """{
        "people": [
            {"id":0,"name":"Alice","gender":"F","wilp":0},
            {"id":1,"name":"Bob","gender":"M"},
            {"id":2,"name":"Carol","gender":"F","parents":100,"wilp":0}
        ],
        "couples": [
            {"coupleId":100,"member1":0,"member2":1}
        ],
        "huwilp": [
            {"id":0,"name":"House","pdeek":"Giskaast"}
        ]
    }"""

[<Fact>]
let ``importJsonText returns Ok with a FamilyGraph for valid input`` () =
    let result = importOrFail validJson
    result.Graph |> allPeople |> Seq.length =! 3
    result.Warnings =! []

[<Fact>]
let ``importJsonText populates the graph's huwilp set from the JSON huwilp array`` () =
    (importOrFail validJson).Graph |> huwilp =! Set.ofList [ WilpName "House" ]

[<Fact>]
let ``importJsonText surfaces parser warnings`` () =
    // Carol references couple 999 which doesn't exist.
    let json =
        """{
            "people": [
                {"id":0,"name":"Alice","gender":"M"},
                {"id":1,"name":"Carol","gender":"F","parents":999}
            ]
        }"""

    (importOrFail json).Warnings =! [ UnresolvedCoupleId("Carol", 999) ]

[<Fact>]
let ``importJsonText threads name holdings through into the graph`` () =
    // Guards that ImportService passes the transform's NameHoldings into
    // createFamilyGraph rather than dropping them.
    let json =
        """{
            "people": [
                {"id":0,"name":"Alice","gender":"F"}
            ],
            "names": [
                {"id":10,"text":"Tinker"}
            ],
            "namesHeld": [
                {"nameId":10,"personId":0,"nameDate":"1900-01-01","nameOrder":2}
            ]
        }"""

    let result = importOrFail json
    result.Warnings =! []

    result.Graph |> namesHeldBy (PersonId 0)
    =! [ held "Tinker" (on 1900 1 1) (Some 2) ]

// ---------------------------------------------------------------------------
// importJsonText — error path
// ---------------------------------------------------------------------------

[<Fact>]
let ``importJsonText returns Error InvalidJson for malformed JSON`` () =
    match importJsonText "this is not json" with
    | Error(InvalidJson _) -> ()
    | other -> failwithf "Expected InvalidJson, got %A" other

[<Fact>]
let ``importJsonText returns Error EmptyPeopleArray for empty people`` () =
    importJsonText """{ "people": [] }""" =! Error EmptyPeopleArray
