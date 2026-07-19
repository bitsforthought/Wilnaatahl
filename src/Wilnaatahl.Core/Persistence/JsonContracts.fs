namespace Wilnaatahl.Persistence

/// The record shapes of the JSON persistence format, shared by the reader
/// (which decodes into them) and the writer (which encodes from them). These
/// are a faithful mirror of the persisted JSON, not the domain model: ids are
/// bare integers and affiliations are id references, exactly as they appear on
/// disk. Semantic interpretation lives in Transform.
module internal JsonContracts =

    /// One person in the JSON persistence format. `deceased` is the only source
    /// field the domain model has no representation for and is not captured here.
    type RawPerson = {
        Id: int
        Name: string option
        Parents: int option
        Wilp: int option
        BirthWilp: int option
        KinshipNote: string option
        BirthOrder: int option
        RawDateOfBirth: string option
        RawDateOfDeath: string option
        NormalizedDateOfBirth: string option
        NormalizedDateOfDeath: string option
        Gender: string
    }

    /// One couple in the JSON persistence format. `Member1` and `Member2` are
    /// person ids in source order; no ordering invariant is imposed.
    type RawCouple = {
        CoupleId: int
        Member1: int
        Member2: int
        DateOfUnion: string option
    }

    /// One entry in the top-level `huwilp` array of the JSON persistence
    /// format. Both `Name` and `Pdeek` are optional.
    type RawWilp = { Id: int; Name: string option; Pdeek: string option }

    /// One entry of the top-level `names` array: a Gitxsan Name with a
    /// file-local id. The id only links `namesHeld` rows to this entry on disk;
    /// a Name's identity is its text.
    type RawName = { Id: int; Text: string }

    /// One entry of the top-level `namesHeld` array: a person (`PersonId`) holds
    /// a Name (`NameId`), with the optional recency keys `NameDate` and
    /// `NameOrder`.
    type RawNameHeld = {
        NameId: int
        PersonId: int
        NameDate: string option
        NameOrder: int option
    }

    /// The full contents of a JSON persistence file: the five top-level arrays.
    /// `Couples`, `Huwilp`, `Names`, and `NamesHeld` default to empty when their
    /// top-level key is absent.
    type RawFile = {
        People: RawPerson list
        Couples: RawCouple list
        Huwilp: RawWilp list
        Names: RawName list
        NamesHeld: RawNameHeld list
    }
