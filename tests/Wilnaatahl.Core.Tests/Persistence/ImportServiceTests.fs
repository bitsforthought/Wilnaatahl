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
// The startup contract is asserted here independently of the Initial bindings.
// ---------------------------------------------------------------------------

/// Builds a NameHeld from text and optional date/order — used by the
/// startup and name-holding import tests.
let private held text nameDate nameOrder : NameHeld = { Name = Name text; NameDate = nameDate; NameOrder = nameOrder }

[<Fact>]
let ``loadSampleGraph contains exactly the startup people and sole Name holdings`` () =
    let graph = loadSampleGraph ()
    let people = graph |> allPeople |> Seq.sortBy _.Id.AsInt |> Seq.toList

    people
    |> List.map (fun person ->
        person.Id,
        person.ColonialName,
        person.Shape,
        person.Kinship,
        person.BirthWilp,
        person.BirthOrder,
        person.DateOfBirth,
        person.DateOfDeath,
        person.DateOfBirthText,
        person.DateOfDeathText)
    =! [
        PersonId 0, None, Sphere, Wilp { Name = WilpName "Wilp"; Pdeek = Giskaast }, None, 0, None, None, None, None
        PersonId 1, None, Cube, UnknownWilp LaxSkiik, None, 0, None, None, None, None
        PersonId 2, None, Sphere, Wilp { Name = WilpName "Wilp"; Pdeek = Giskaast }, None, 0, None, None, None, None
        PersonId 3, None, Sphere, Wilp { Name = WilpName "Wilp"; Pdeek = Giskaast }, None, 1, None, None, None, None
        PersonId 4, None, Cube, Wilp { Name = WilpName "Wilp"; Pdeek = Giskaast }, None, 2, None, None, None, None
        PersonId 5, None, Cube, UnknownWilp Ganeda, None, 0, None, None, None, None
        PersonId 6, None, Sphere, Wilp { Name = WilpName "Wilp"; Pdeek = Giskaast }, None, 0, None, None, None, None
        PersonId 7, None, Cube, Wilp { Name = WilpName "Wilp"; Pdeek = Giskaast }, None, 1, None, None, None, None
        PersonId 8, None, Cube, UnknownWilp LaxGibuu, None, 0, None, None, None, None
        PersonId 9, None, Cube, Wilp { Name = WilpName "Wilp"; Pdeek = Giskaast }, None, 2, None, None, None, None
    ]

    people |> List.map _.Id |> List.distinct |> List.length =! 10

    people
    |> List.collect (fun person -> graph |> namesHeldBy person.Id |> List.map (fun holding -> person.Id, holding))
    =! [
        PersonId 0, held "Ts'iits'" None None
        PersonId 1, held "Ye'e" None None
        PersonId 2, held "Nixdaa" None None
        PersonId 3, held "Nox" None None
        PersonId 4, held "Biip" None None
        PersonId 5, held "Nigwoot" None None
        PersonId 6, held "Gimxdii" None None
        PersonId 7, held "Wek" None None
        PersonId 8, held "Wilaayasxw" None None
        PersonId 9, held "'Nii'y" None None
    ]

[<Fact>]
let ``loadSampleGraph preserves the three couples and ordered parentage`` () =
    let graph = loadSampleGraph ()

    let personById: Map<PersonId, Person> =
        graph |> allPeople |> Seq.map (fun person -> person.Id, person) |> Map.ofSeq

    let nameOf (personId: PersonId) =
        match graph |> namesHeldBy personId with
        | holding :: _ -> holding.Name.AsString
        | [] -> personById[personId].ColonialName |> Option.defaultValue "<unnamed>"

    let coupleNames (couple: Couple) =
        let member1, member2 = couple.Members
        [ nameOf member1; nameOf member2 ]

    graph
    |> couples
    |> Seq.sortBy _.Id.AsInt
    |> Seq.map (fun couple -> coupleNames couple, couple.DateOfUnion)
    |> Seq.toList
    =! [
        [ "Ts'iits'"; "Ye'e" ], None
        [ "Nox"; "Nigwoot" ], None
        [ "Gimxdii"; "Wilaayasxw" ], None
    ]

    graph
    |> couples
    |> Seq.sortBy _.Id.AsInt
    |> Seq.map (fun couple ->
        findChildrenOfCouple couple graph
        |> List.map (fun childId -> nameOf childId, personById[childId].BirthOrder))
    |> Seq.toList
    =! [
        [ "Nixdaa", 0; "Nox", 1; "Biip", 2 ]
        [ "Gimxdii", 0; "Wek", 1; "'Nii'y", 2 ]
        []
    ]

type private RenderedStartupStats = { People: Set<PersonId>; MaxDepth: int; ChildlessCouples: int }

[<Fact>]
let ``loadSampleGraph renders one three-generation forest containing all ten people`` () =
    let graph = loadSampleGraph ()

    let visitLeaf personId = {
        People = Set.singleton personId
        MaxDepth = 1
        ChildlessCouples = 0
    }

    let visitPartner (personId: PersonId) (_: Couple) : RenderedStartupStats = {
        People = Set.singleton personId
        MaxDepth = 1
        ChildlessCouples = 0
    }

    let visitParent (personId: PersonId) : RenderedStartupStats = visitLeaf personId

    let visitFamily (parent: RenderedStartupStats) (groups: (RenderedStartupStats * RenderedStartupStats seq) array) =
        let peopleInGroups =
            groups
            |> Array.toList
            |> List.collect (fun (partnerStats, descendants) ->
                let descendantPeople = descendants |> Seq.map _.People
                [ descendantPeople |> Seq.fold Set.union partnerStats.People ])

        let childlessCouples =
            groups
            |> Array.sumBy (fun (_, descendants) -> if Seq.isEmpty descendants then 1 else 0)

        let descendantChildlessCouples =
            groups
            |> Array.sumBy (fun (_, descendants) -> descendants |> Seq.sumBy _.ChildlessCouples)

        let deepestChildGeneration =
            groups
            |> Array.collect (snd >> Seq.toArray)
            |> Array.map _.MaxDepth
            |> fun depths -> if Array.isEmpty depths then 0 else Array.max depths

        {
            People = peopleInGroups |> List.fold Set.union parent.People
            MaxDepth = max parent.MaxDepth (deepestChildGeneration + 1)
            ChildlessCouples = parent.ChildlessCouples + childlessCouples + descendantChildlessCouples
        }

    let compareTrees _ _ = 0
    let compareGroups _ _ = 0

    let forests =
        visitWilpForest
            (WilpName "Wilp")
            visitLeaf
            visitParent
            visitPartner
            visitFamily
            compareTrees
            compareGroups
            graph
        |> Seq.toList

    forests |> List.length =! 1

    forests |> List.head
    =! {
           People = [ 0..9 ] |> List.map PersonId |> Set.ofList
           MaxDepth = 3
           ChildlessCouples = 1
       }

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
