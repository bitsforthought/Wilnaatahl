namespace Wilnaatahl.ViewModel

#if FABLE_COMPILER
open Fable.Core
#endif

/// Locales the UI can render. English is the only implemented locale today; add a
/// case here (and a branch in `UiText.text` below) to add a language.
// Deliberately not a [<StringEnum>]: Fable types a lambda returning one as `string`
// while still typing its container by the union, so an emitted factory returning
// this type fails to type-check.
type Locale = En

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Locale =
    /// Maps a BCP-47 language tag (e.g. from the browser) to a supported Locale.
    /// Everything falls back to English for now.
    let parse (languageTag: string) : Locale = En

/// Translatable UI chrome — fixed labels and words. `UiText.text` resolves each
/// item to its string for a given locale.
#if FABLE_COMPILER
[<StringEnum>]
#endif
type UiText =
    | BirthAbbreviation
    | DeathAbbreviation
    | BornLabel
    | DiedLabel
    | WilpLabel
    | PdeekLabel
    | BirthWilpLabel
    | BirthPdeekLabel
    | KinshipLabel
    | KinshipNotProvided
    | OtherNamesHeldHeading

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module UiText =
    /// The chrome string for the given locale. The Gitxsan labels carry a combining
    /// macron-below (U+0331) on the `k` — the same decomposed byte form used in
    /// `Pdeek.displayName`, never the precomposed U+1E35.
    let text (locale: Locale) (item: UiText) : string =
        match locale with
        | En ->
            match item with
            | BirthAbbreviation -> "B"
            | DeathAbbreviation -> "D"
            | BornLabel -> "Born:"
            | DiedLabel -> "Died:"
            | WilpLabel -> "Wilp:"
            | PdeekLabel -> "Pdeeḵ:"
            | BirthWilpLabel -> "Birth Wilp:"
            | BirthPdeekLabel -> "Birth Pdeeḵ:"
            | KinshipLabel -> "Kinship:"
            | KinshipNotProvided -> "Not provided"
            | OtherNamesHeldHeading -> "Other names held:"
