module Wilnaatahl.Tests.Persistence.ImportServiceTests

open Xunit
open Swensen.Unquote
open Wilnaatahl.Model
open Wilnaatahl.Model.FamilyGraph
open Wilnaatahl.Persistence
open Wilnaatahl.Persistence.ImportService

// ---------------------------------------------------------------------------
// loadSampleGraph — builds a valid graph from the hardcoded Initial sample data
// ---------------------------------------------------------------------------

[<Fact>]
let ``loadSampleGraph returns graph with expected people count`` () =
    loadSampleGraph () |> allPeople |> Seq.length =! 35

[<Fact>]
let ``loadSampleGraph returns graph with expected huwilp`` () =
    loadSampleGraph () |> huwilp
    =! Set.ofList [ WilpName "A"; WilpName "B"; WilpName "C"; WilpName "D" ]

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
    match importJsonText validJson with
    | Ok result ->
        result.Graph |> allPeople |> Seq.length =! 3
        result.Warnings =! []
    | Error e -> failwithf "Unexpected error: %A" e

[<Fact>]
let ``importJsonText populates the graph's huwilp set from the JSON huwilp array`` () =
    match importJsonText validJson with
    | Ok result -> result.Graph |> huwilp =! Set.ofList [ WilpName "House" ]
    | Error e -> failwithf "Unexpected error: %A" e

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

    match importJsonText json with
    | Ok result -> result.Warnings =! [ UnresolvedCoupleId("Carol", 999) ]
    | Error e -> failwithf "Unexpected error: %A" e

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
    match importJsonText """{ "people": [] }""" with
    | Error EmptyPeopleArray -> ()
    | other -> failwithf "Expected EmptyPeopleArray, got %A" other
