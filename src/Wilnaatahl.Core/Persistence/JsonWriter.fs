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
/// absent key to None, so encoding None by omission round-trips. The couples,
/// huwilp, names, and namesHeld arrays are always written (the reader tolerates
/// their absence too).
module internal JsonWriter =

    let private encodeOptionalField name encoder value =
        value |> Option.map (fun v -> name, encoder v)

    let private encodePerson (person: RawPerson) =
        [
            Some("id", Encode.int person.Id)
            person.Name |> encodeOptionalField "name" Encode.string
            person.Parents |> encodeOptionalField "parents" Encode.int
            person.Wilp |> encodeOptionalField "wilp" Encode.int
            person.BirthWilp |> encodeOptionalField "birthWilp" Encode.int
            person.KinshipNote |> encodeOptionalField "kinshipNote" Encode.string
            person.BirthOrder |> encodeOptionalField "birthOrder" Encode.int
            person.RawDateOfBirth |> encodeOptionalField "dateOfBirth" Encode.string
            person.NormalizedDateOfBirth
            |> encodeOptionalField "normalizedDateOfBirth" Encode.string
            person.RawDateOfDeath |> encodeOptionalField "dateOfDeath" Encode.string
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

    let private encodeName (name: RawName) =
        Encode.object [ "id", Encode.int name.Id; "text", Encode.string name.Text ]

    let private encodeNameHeld (nameHeld: RawNameHeld) =
        [
            Some("nameId", Encode.int nameHeld.NameId)
            Some("personId", Encode.int nameHeld.PersonId)
            nameHeld.NameDate |> encodeOptionalField "nameDate" Encode.string
            nameHeld.NameOrder |> encodeOptionalField "nameOrder" Encode.int
        ]
        |> List.choose id
        |> Encode.object

    let private encodeFile (file: RawFile) =
        Encode.object [
            "people", Encode.list (file.People |> List.map encodePerson)
            "couples", Encode.list (file.Couples |> List.map encodeCouple)
            "huwilp", Encode.list (file.Huwilp |> List.map encodeWilp)
            "names", Encode.list (file.Names |> List.map encodeName)
            "namesHeld", Encode.list (file.NamesHeld |> List.map encodeNameHeld)
        ]

    /// Encodes a RawFile to a compact JSON string. Inverse of JsonReader.read
    /// for any RawFile.
    let write (file: RawFile) : string = encodeFile file |> Encode.toString 0
