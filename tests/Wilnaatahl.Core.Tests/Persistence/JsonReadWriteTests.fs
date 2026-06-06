namespace Wilnaatahl.Tests.Persistence

open System
open Xunit
open FsCheck
open FsCheck.FSharp
open FsCheck.Xunit
open Newtonsoft.Json.Linq
open Swensen.Unquote
open Wilnaatahl.Persistence
open Wilnaatahl.Persistence.JsonContracts

/// Generators for arbitrary RawFile values. Strings are drawn from a character
/// set that includes JSON-significant characters (quote, backslash, slash,
/// control whitespace) and Sim Algyax orthography (the precomposed underlined k
/// U+1E35, a combining macron below U+0331, a non-breaking space) so the
/// round-trip exercises escaping and Unicode. The generator never produces a
/// null string, which a required-string decode would reject.
module private RawFileGen =
    let private safeChars =
        [ 'a' .. 'z' ]
        @ [ 'A' .. 'Z' ]
        @ [ '0' .. '9' ]
        @ [ ' '; '\t'; '\n'; '"'; '\\'; '/'; '\''; 'é'; '\u1E35'; '\u00A0'; '\u0331' ]

    let private charGen = Gen.elements safeChars

    let private stringGen = Gen.arrayOf charGen |> Gen.map String

    let private intGen = Gen.choose (-1000, 1000)
    let private optInt = Gen.optionOf intGen
    let private optString = Gen.optionOf stringGen

    let private personGen =
        gen {
            let! id = intGen
            let! name = stringGen
            let! parents = optInt
            let! wilp = optInt
            let! birthOrder = optInt
            let! normalizedDateOfBirth = optString
            let! normalizedDateOfDeath = optString
            let! gender = stringGen

            return {
                Id = id
                Name = name
                Parents = parents
                Wilp = wilp
                BirthOrder = birthOrder
                NormalizedDateOfBirth = normalizedDateOfBirth
                NormalizedDateOfDeath = normalizedDateOfDeath
                Gender = gender
            }
        }

    let private coupleGen =
        gen {
            let! coupleId = intGen
            let! member1 = intGen
            let! member2 = intGen
            let! dateOfUnion = optString

            return {
                CoupleId = coupleId
                Member1 = member1
                Member2 = member2
                DateOfUnion = dateOfUnion
            }
        }

    let private wilpGen =
        gen {
            let! id = intGen
            let! name = optString
            let! pdeek = optString
            return { Id = id; Name = name; Pdeek = pdeek }
        }

    let fileGen =
        gen {
            let! people = Gen.listOf personGen
            let! couples = Gen.listOf coupleGen
            let! huwilp = Gen.listOf wilpGen
            return { People = people; Couples = couples; Huwilp = huwilp }
        }

    let arb = Arb.fromGen fileGen

module JsonReadWriteTests =

    /// Structural JSON equality: a JSON object is an unordered collection of
    /// members (RFC 8259), so this ignores object key order and whitespace,
    /// while still respecting array order and every key and value. Lets the
    /// expected literals below pin the exact schema without being brittle to
    /// formatting.
    let private jsonStructurallyEquals (expected: string) (actual: string) =
        JToken.DeepEquals(JToken.Parse expected, JToken.Parse actual)

    // ---- read: behaviours the round-trip property can't express ----
    //
    // Per-field decode preservation is covered generically by the read/write
    // round-trip property below. The facts here pin behaviours it can't reach:
    // the documented minimal shape, error reporting on malformed input, and
    // tolerance of unknown keys.

    /// Builds a RawPerson with all optional fields absent — the minimum a JSON
    /// object needs to decode successfully.
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
    let ``read decodes absent optional fields to None and absent arrays to empty`` () =
        let json = """{"people": [{"id":0,"name":"Alice","gender":"F"}]}"""

        JsonReader.read json
        =! Ok {
            People = [ minimalPerson 0 "Alice" "F" ]
            Couples = []
            Huwilp = []
        }

    [<Fact>]
    let ``read returns Error with a non-empty message for malformed JSON`` () =
        match JsonReader.read "not valid json {{ at all" with
        | Error message -> message.Length >! 0
        | Ok _ -> failwith "Expected Error"

    [<Fact>]
    let ``read ignores extra unknown fields`` () =
        let json =
            """{"people":[{"id":0,"name":"Dave","gender":"M","deceased":true,
               "birthWilp":5,"dateOfBirth":"circa 1850","dateOfDeath":"1920-ish"}]}"""

        JsonReader.read json
        =! Ok { People = [ minimalPerson 0 "Dave" "M" ]; Couples = []; Huwilp = [] }

    // ---- read/write round-trip ----

    /// The central "there and back again" property: the writer is the inverse
    /// of the reader. Catches one-directional field renames that the forward-
    /// only decode facts cannot. RawFile is internal, so it stays out of the
    /// (public) property signature by going through Prop.forAll.
    [<Property>]
    let ``writing then reading round-trips any RawFile`` () =
        Prop.forAll RawFileGen.arb (fun raw -> JsonReader.read (JsonWriter.write raw) = Ok raw)

    // ---- write: exact persistence-format output ----
    //
    // These pin the writer's output to the documented persistence format.
    // Combined with the round-trip property (reader keys = writer keys), they
    // anchor both directions to the schema, closing the two-directional-rename
    // blind spot of round-trip alone.

    [<Fact>]
    let ``write emits the full persistence format with every field populated`` () =
        let raw = {
            People = [
                {
                    Id = 0
                    Name = "A"
                    Parents = Some 1
                    Wilp = Some 2
                    BirthOrder = Some 3
                    NormalizedDateOfBirth = Some "1900-01-01"
                    NormalizedDateOfDeath = Some "1980-01-01"
                    Gender = "F"
                }
            ]
            Couples = [
                {
                    CoupleId = 1
                    Member1 = 0
                    Member2 = 5
                    DateOfUnion = Some "1950-01-01"
                }
            ]
            Huwilp = [ { Id = 2; Name = Some "House"; Pdeek = Some "Giskaast" } ]
        }

        let expected =
            """{
                "people": [
                    {
                        "id": 0,
                        "name": "A",
                        "parents": 1,
                        "wilp": 2,
                        "birthOrder": 3,
                        "normalizedDateOfBirth": "1900-01-01",
                        "normalizedDateOfDeath": "1980-01-01",
                        "gender": "F"
                    }
                ],
                "couples": [
                    { "coupleId": 1, "member1": 0, "member2": 5, "dateOfUnion": "1950-01-01" }
                ],
                "huwilp": [ { "id": 2, "name": "House", "pdeek": "Giskaast" } ]
            }"""

        jsonStructurallyEquals expected (JsonWriter.write raw) =! true

    [<Fact>]
    let ``write omits optional fields whose value is None`` () =
        let raw = {
            People = [
                {
                    Id = 0
                    Name = "A"
                    Parents = None
                    Wilp = None
                    BirthOrder = None
                    NormalizedDateOfBirth = None
                    NormalizedDateOfDeath = None
                    Gender = "M"
                }
            ]
            Couples = []
            Huwilp = []
        }

        let expected =
            """{ "people": [ { "id": 0, "name": "A", "gender": "M" } ], "couples": [], "huwilp": [] }"""

        jsonStructurallyEquals expected (JsonWriter.write raw) =! true
