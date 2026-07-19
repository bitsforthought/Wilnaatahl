namespace Wilnaatahl.ViewModel

open System
open Wilnaatahl.Model

#if FABLE_COMPILER
open Fable.Core
#endif

/// A date for display. `FormattedDate` carries a parseable calendar date, to be
/// formatted for presentation; `RawText` carries the original unparseable string,
/// shown verbatim.
#if FABLE_COMPILER
[<TypeScriptTaggedUnion("kind")>]
#endif
type DisplayDate =
    | FormattedDate of date: DateOnly
    | RawText of text: string

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module DisplayDate =
    /// Chooses the display date from a normalized date and its raw-text fallback:
    /// a parseable date wins, else the raw text, else nothing.
    let ofDateAndText (normalized: DateOnly option) (raw: string option) : DisplayDate option =
        match normalized with
        | Some date -> Some(FormattedDate date)
        | None -> raw |> Option.map RawText

/// The presentation-neutral label content for a tree node: each field has already had
/// its show/hide decision made and is in final order, carrying domain values and
/// `DisplayDate`s rather than formatted strings or chrome, so no date formatting or
/// translation lives here.
type NodeLabelView = {
    ColonialName: string option
    MostRecentName: string option
    /// Parenthesized-line inner text (a Wilp name or a Pdeek's Gitxsan spelling),
    /// already de-duplicated by the builder; `None` when no line shows.
    KinshipParen: string option
    Born: DisplayDate option
    Died: DisplayDate option
} with

    static member Empty = {
        ColonialName = None
        MostRecentName = None
        KinshipParen = None
        Born = None
        Died = None
    }

/// Builds the presentation-neutral label content shown on a tree node.
module NodeLabel =

    /// The parenthesized Kinship line's inner text, drawn purely from the person's
    /// Kinship: the bare Wilp name, the Pdeek's Gitxsan display spelling, or nothing
    /// when no structured Wilp/Pdeek is known. The bare Wilp name is also omitted
    /// when it would merely repeat the most-recent Name already shown above it (e.g.
    /// an outside spouse whose most-recent Name *is* their Wilp's name).
    let private kinshipParen (mostRecentName: string option) kinship =
        match kinship with
        | Wilp w when mostRecentName = Some w.Name.AsString -> None
        | Wilp w -> Some w.Name.AsString
        | UnknownWilp pdeek -> Some(Pdeek.displayName pdeek)
        | NoneProvided _ -> None

    /// Composes the label content from the person's own data plus
    /// `currentWilpDiffersFromRendered`. `ColonialName` and `MostRecentName` (the head
    /// of `namesHeld`, which is supplied most-recent-first) surface verbatim; the
    /// parenthesized Kinship text shows only when `currentWilpDiffersFromRendered` is
    /// true (and, for a bare Wilp name, only when it does not merely repeat the
    /// most-recent Name); `Born` and `Died` are chosen by `DisplayDate.ofDateAndText`.
    let build (person: Person) (namesHeld: NameHeld list) (currentWilpDiffersFromRendered: bool) : NodeLabelView =
        let mostRecentName =
            match namesHeld with
            | name :: _ -> Some name.Name.AsString
            | [] -> None

        {
            ColonialName = person.ColonialName
            MostRecentName = mostRecentName
            KinshipParen =
                if currentWilpDiffersFromRendered then
                    kinshipParen mostRecentName person.Kinship
                else
                    None
            Born = DisplayDate.ofDateAndText person.DateOfBirth person.DateOfBirthText
            Died = DisplayDate.ofDateAndText person.DateOfDeath person.DateOfDeathText
        }

/// One row of the detail overlay's Kinship section: the row's kind plus its domain
/// value (a Wilp name, a Pdeek's Gitxsan spelling, or a free-form note). The
/// per-kind chrome label ("Wilp:", "Pdeeḵ:", "Kinship:", ...) is a catalog entry,
/// not carried here; `KinshipUnknown` carries no value.
#if FABLE_COMPILER
[<TypeScriptTaggedUnion("kind")>]
#endif
type KinshipRow =
    | CurrentWilp of wilpName: string
    | CurrentPdeek of pdeekDisplay: string
    | BirthWilp of wilpName: string
    | BirthPdeek of pdeekDisplay: string
    | KinshipNote of note: string
    | KinshipUnknown

/// The presentation-neutral data backing a person's detail overlay. `Title` is the
/// card header (domain data only), `Kinship` the structured Kinship rows (1–4: the
/// current Kinship rows plus an optional Birth Wilp/Pdeeḵ pair), `Born`/`Died` the
/// displayable dates when known, and `OtherNames` the held Names minus the
/// most-recent one, most-recent-first.
type NodeDetail = {
    Title: string
    Kinship: KinshipRow list
    Born: DisplayDate option
    Died: DisplayDate option
    OtherNames: string list
}

/// Builds the detail-overlay content for a single person.
module NodeDetail =

    /// The header text: the most-recent Name and the colonial name where present.
    /// Both → `<Name> (<colonial>)`; one → that one; neither → empty.
    let private title (colonialName: string option) (mostRecentName: string option) =
        match mostRecentName, colonialName with
        | Some name, Some colonial -> $"{name} ({colonial})"
        | Some name, None -> name
        | None, Some colonial -> colonial
        | None, None -> ""

    /// The current-Kinship rows: a Wilp and Pdeek pair, a lone Pdeek row, a single
    /// note row, or a single unknown row.
    let private currentKinshipRows kinship =
        match kinship with
        | Wilp w -> [ CurrentWilp w.Name.AsString; CurrentPdeek(Pdeek.displayName w.Pdeek) ]
        | UnknownWilp pdeek -> [ CurrentPdeek(Pdeek.displayName pdeek) ]
        | NoneProvided None -> [ KinshipUnknown ]
        | NoneProvided(Some note) -> [ KinshipNote note ]

    /// The Birth Wilp/Pdeek rows, appended when a birth Wilp is recorded that differs
    /// from the current Kinship Wilp (structural equality). An `UnknownWilp`/
    /// `NoneProvided` current Kinship has no Wilp to match, so the rows always show
    /// when a birth Wilp is present.
    let private birthWilpRows kinship (birthWilp: Wilp option) =
        match birthWilp with
        | Some bw ->
            let matchesCurrent =
                match kinship with
                | Wilp w -> w = bw
                | UnknownWilp _
                | NoneProvided _ -> false

            if matchesCurrent then
                []
            else
                [ BirthWilp bw.Name.AsString; BirthPdeek(Pdeek.displayName bw.Pdeek) ]
        | None -> []

    /// Composes the overlay content from the person's own data and their held Names
    /// (supplied most-recent-first). Unlike the label, the Kinship section is built
    /// unconditionally, and the birth-Wilp rows depend only on `Person.BirthWilp`.
    let build (person: Person) (namesHeld: NameHeld list) : NodeDetail =
        let mostRecentName, otherNames =
            match namesHeld with
            | name :: rest -> Some name.Name.AsString, rest |> List.map _.Name.AsString
            | [] -> None, []

        {
            Title = title person.ColonialName mostRecentName
            Kinship =
                currentKinshipRows person.Kinship
                @ birthWilpRows person.Kinship person.BirthWilp
            Born = DisplayDate.ofDateAndText person.DateOfBirth person.DateOfBirthText
            Died = DisplayDate.ofDateAndText person.DateOfDeath person.DateOfDeathText
            OtherNames = otherNames
        }
