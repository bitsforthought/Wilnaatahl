namespace Wilnaatahl.Import

open Thoth.Json.Core

#if !FABLE_COMPILER
open Thoth.Json.Newtonsoft
#else
open Thoth.Json.JavaScript
#endif

module internal JsonParser =

    /// What Thoth.Json decodes into for one person. Source fields with no
    /// representation here (`dateOfBirth`, `dateOfDeath`, `birthWilp`,
    /// `deceased`) are silently dropped at decode time.
    type RawPerson = {
        Id: int
        Name: string
        Parents: int option
        Wilp: int option
        BirthOrder: int option
        NormalizedDateOfBirth: string option
        NormalizedDateOfDeath: string option
        Gender: string
    }

    /// What Thoth.Json decodes into for one couple. `Member1` and `Member2`
    /// are JSON person ids in source order; no ordering invariant is imposed.
    type RawCouple = {
        CoupleId: int
        Member1: int
        Member2: int
        DateOfUnion: string option
    }

    /// What Thoth.Json decodes into for one entry in the top-level `huwilp`
    /// array. Both `Name` and `Pdeek` are optional at the decoder level.
    type RawWilp = { Id: int; Name: string option; Pdeek: string option }

    /// Top-level decoded contents of an import file.
    type RawFile = {
        People: RawPerson list
        Couples: RawCouple list
        Huwilp: RawWilp list
    }

    /// Decodes a single person object. `get.Optional.Field` returns `None` for
    /// both absent and null fields (Thoth.Json.Core `decodeMaybeNull`
    /// semantics), giving a single uniform representation for the two source
    /// variants.
    let private rawPersonDecoder: Decoder<RawPerson> =
        Decode.object (fun get -> {
            Id = get.Required.Field "id" Decode.int
            Name = get.Required.Field "name" Decode.string
            Parents = get.Optional.Field "parents" Decode.int
            Wilp = get.Optional.Field "wilp" Decode.int
            BirthOrder = get.Optional.Field "birthOrder" Decode.int
            NormalizedDateOfBirth = get.Optional.Field "normalizedDateOfBirth" Decode.string
            NormalizedDateOfDeath = get.Optional.Field "normalizedDateOfDeath" Decode.string
            Gender = get.Required.Field "gender" Decode.string
        })

    /// Decodes a single couple object.
    let private rawCoupleDecoder: Decoder<RawCouple> =
        Decode.object (fun get -> {
            CoupleId = get.Required.Field "coupleId" Decode.int
            Member1 = get.Required.Field "member1" Decode.int
            Member2 = get.Required.Field "member2" Decode.int
            DateOfUnion = get.Optional.Field "dateOfUnion" Decode.string
        })

    /// Decodes a single huwilp entry.
    let private rawWilpDecoder: Decoder<RawWilp> =
        Decode.object (fun get -> {
            Id = get.Required.Field "id" Decode.int
            Name = get.Optional.Field "name" Decode.string
            Pdeek = get.Optional.Field "pdeek" Decode.string
        })

    /// Decodes the top-level file object. The `couples` and `huwilp` keys are
    /// optional; absent → empty list.
    let private rawFileDecoder: Decoder<RawFile> =
        Decode.object (fun get -> {
            People = get.Required.Field "people" (Decode.list rawPersonDecoder)
            Couples =
                get.Optional.Field "couples" (Decode.list rawCoupleDecoder)
                |> Option.defaultValue []
            Huwilp =
                get.Optional.Field "huwilp" (Decode.list rawWilpDecoder)
                |> Option.defaultValue []
        })

    /// Parses a JSON string into a RawFile. The Error branch carries the
    /// underlying decoder message verbatim. Reports syntactic problems only;
    /// semantic validity (empty arrays, duplicate ids, etc.) is downstream.
    let parseJson (json: string) : Result<RawFile, string> = Decode.fromString rawFileDecoder json
