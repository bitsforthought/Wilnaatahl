namespace Wilnaatahl.Persistence

open Thoth.Json.Core

#if !FABLE_COMPILER
open Thoth.Json.Newtonsoft
#else
open Thoth.Json.JavaScript
#endif

open Wilnaatahl.Persistence.JsonContracts

/// Serializes the Raw* records to the JSON persistence format that JsonReader
/// reads. Optional fields are emitted only when present; the reader maps an
/// absent key to None, so encoding None by omission round-trips. The huwilp and
/// couples arrays are always written (the reader tolerates their absence too).
module internal JsonWriter =

    let private encodeOptionalField name encoder value =
        value |> Option.map (fun v -> name, encoder v)

    let private encodePerson (person: RawPerson) =
        [
            Some("id", Encode.int person.Id)
            Some("name", Encode.string person.Name)
            person.Parents |> encodeOptionalField "parents" Encode.int
            person.Wilp |> encodeOptionalField "wilp" Encode.int
            person.BirthOrder |> encodeOptionalField "birthOrder" Encode.int
            person.NormalizedDateOfBirth
            |> encodeOptionalField "normalizedDateOfBirth" Encode.string
            person.NormalizedDateOfDeath
            |> encodeOptionalField "normalizedDateOfDeath" Encode.string
            Some("gender", Encode.string person.Gender)
        ]
        |> List.choose id
        |> Encode.object

    let private encodeCouple (couple: RawCouple) =
        [
            Some("coupleId", Encode.int couple.CoupleId)
            Some("member1", Encode.int couple.Member1)
            Some("member2", Encode.int couple.Member2)
            couple.DateOfUnion |> encodeOptionalField "dateOfUnion" Encode.string
        ]
        |> List.choose id
        |> Encode.object

    let private encodeWilp (wilp: RawWilp) =
        [
            Some("id", Encode.int wilp.Id)
            wilp.Name |> encodeOptionalField "name" Encode.string
            wilp.Pdeek |> encodeOptionalField "pdeek" Encode.string
        ]
        |> List.choose id
        |> Encode.object

    let private encodeFile (file: RawFile) =
        Encode.object [
            "people", Encode.list (file.People |> List.map encodePerson)
            "couples", Encode.list (file.Couples |> List.map encodeCouple)
            "huwilp", Encode.list (file.Huwilp |> List.map encodeWilp)
        ]

    /// Encodes a RawFile to a compact JSON string. Inverse of JsonReader.read
    /// for any RawFile.
    let write (file: RawFile) : string = encodeFile file |> Encode.toString 0
