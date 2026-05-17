module Wilnaatahl.Tests.Import.JsonParserTests

open Xunit
open Swensen.Unquote
open Wilnaatahl.Import.JsonParser

/// Builds a RawPerson with all optional fields absent — the minimum a JSON object needs
/// to decode successfully.
let private minimalPerson id name gender = {
    Id = id
    Name = name
    Parents = None
    Wilp = None
    BirthOrder = None
    NormalizedDateOfBirth = None
    NormalizedDateOfDeath = None
    Gender = gender
}

[<Fact>]
let ``parseJson minimal valid JSON decodes absent fields to None and absent arrays to empty`` () =
    let json = """{"people": [{"id":0,"name":"Alice","gender":"F"}]}"""

    parseJson json
    =! Ok {
        People = [ minimalPerson 0 "Alice" "F" ]
        Couples = []
        Huwilp = []
    }

[<Fact>]
let ``parseJson person with all optional fields populated returns all fields set`` () =
    let json =
        """{"people": [{"id":7,"name":"Bob","gender":"M","parents":42,
           "wilp":3,"birthOrder":2,"normalizedDateOfBirth":"1900-01-01",
           "normalizedDateOfDeath":"1980-12-31"}]}"""

    parseJson json
    =! Ok {
        People = [
            {
                Id = 7
                Name = "Bob"
                Parents = Some 42
                Wilp = Some 3
                BirthOrder = Some 2
                NormalizedDateOfBirth = Some "1900-01-01"
                NormalizedDateOfDeath = Some "1980-12-31"
                Gender = "M"
            }
        ]
        Couples = []
        Huwilp = []
    }

[<Fact>]
let ``parseJson couple with all fields returns correct RawCouple`` () =
    let json =
        """{"people":[{"id":0,"name":"A","gender":"F"}],
           "couples":[{"coupleId":50,"member1":0,"member2":1,"dateOfUnion":"1955-06-15"}]}"""

    match parseJson json with
    | Ok rawFile ->
        rawFile.Couples
        =! [
            {
                CoupleId = 50
                Member1 = 0
                Member2 = 1
                DateOfUnion = Some "1955-06-15"
            }
        ]
    | Error e -> failwithf "Unexpected error: %A" e

[<Fact>]
let ``parseJson huwilp populated round-trips with name and pdeek`` () =
    let json =
        """{"people":[{"id":0,"name":"A","gender":"F"}],
           "huwilp":[{"id":0,"name":"First","pdeek":"Giskaast"},
                     {"id":1,"name":"Second","pdeek":null},
                     {"id":2,"name":null,"pdeek":"Lax Skiik"}]}"""

    match parseJson json with
    | Ok rawFile ->
        rawFile.Huwilp
        =! [
            { Id = 0; Name = Some "First"; Pdeek = Some "Giskaast" }
            { Id = 1; Name = Some "Second"; Pdeek = None }
            { Id = 2; Name = None; Pdeek = Some "Lax Skiik" }
        ]
    | Error e -> failwithf "Unexpected error: %A" e

[<Fact>]
let ``parseJson huwilp entry with all keys absent decodes with None fields`` () =
    let json =
        """{"people":[{"id":0,"name":"A","gender":"F"}],
           "huwilp":[{"id":9}]}"""

    match parseJson json with
    | Ok rawFile -> rawFile.Huwilp =! [ { Id = 9; Name = None; Pdeek = None } ]
    | Error e -> failwithf "Unexpected error: %A" e

[<Fact>]
let ``parseJson malformed JSON returns Error with non-empty message`` () =
    match parseJson "not valid json {{ at all" with
    | Error msg -> msg.Length >! 0
    | Ok _ -> failwith "Expected Error"

[<Fact>]
let ``parseJson empty people array decodes to empty People list`` () =
    parseJson """{"people": []}""" =! Ok { People = []; Couples = []; Huwilp = [] }

[<Fact>]
let ``parseJson ignores extra unknown fields`` () =
    let json =
        """{"people":[{"id":0,"name":"Dave","gender":"M","deceased":true,
           "birthWilp":5,"dateOfBirth":"circa 1850","dateOfDeath":"1920-ish"}]}"""

    parseJson json
    =! Ok { People = [ minimalPerson 0 "Dave" "M" ]; Couples = []; Huwilp = [] }
