namespace Wilnaatahl.Persistence

/// The record shapes of the JSON persistence format, shared by the reader
/// (which decodes into them) and the writer (which encodes from them). These
/// are a faithful mirror of the persisted JSON, not the domain model: ids are
/// bare integers and affiliations are id references, exactly as they appear on
/// disk. Semantic interpretation lives in Transform.
module internal JsonContracts =

    /// One person in the JSON persistence format. Fields the format carries but
    /// the domain model has no representation for (`dateOfBirth`, `dateOfDeath`,
    /// `birthWilp`, `deceased`) are not captured here.
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

    /// The full contents of a JSON persistence file: the three top-level
    /// arrays.
    type RawFile = {
        People: RawPerson list
        Couples: RawCouple list
        Huwilp: RawWilp list
    }
