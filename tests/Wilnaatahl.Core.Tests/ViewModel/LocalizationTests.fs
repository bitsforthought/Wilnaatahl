module Wilnaatahl.Tests.ViewModel.LocalizationTests

open Xunit
open Swensen.Unquote
open FSharp.Reflection
open Wilnaatahl.ViewModel

/// Every BCP-47 language tag maps to English while En is the only locale.
[<Theory>]
[<InlineData("en-US")>]
[<InlineData("fr-FR")>]
[<InlineData("zz-ZZ")>]
[<InlineData("")>]
let ``Locale parse maps every language tag to En for now`` (languageTag: string) = Locale.parse languageTag =! En

/// Pins the exact English chrome string for every catalog item. The reflection
/// guard equates the rows below with every UiText case, so adding a case without
/// a row here fails the test — a new label can never silently lose coverage. The
/// Gitxsan Pdeeḵ labels carry a combining macron-below on the k, the same
/// decomposed byte form used in Pdeek.displayName.
[<Fact>]
let ``UiText text En renders the exact English chrome for every catalog item`` () =
    let expected = [
        BirthAbbreviation, "B"
        DeathAbbreviation, "D"
        BornLabel, "Born:"
        DiedLabel, "Died:"
        WilpLabel, "Wilp:"
        PdeekLabel, "Pdeeḵ:"
        BirthWilpLabel, "Birth Wilp:"
        BirthPdeekLabel, "Birth Pdeeḵ:"
        KinshipLabel, "Kinship:"
        KinshipNotProvided, "Not provided"
        OtherNamesHeldHeading, "Other names held:"
    ]

    let allCases =
        FSharpType.GetUnionCases typeof<UiText>
        |> Array.map (fun case -> FSharpValue.MakeUnion(case, [||]) :?> UiText)
        |> Set.ofArray

    expected |> List.map fst |> Set.ofList =! allCases

    expected |> List.map (fun (item, _) -> UiText.text En item)
    =! (expected |> List.map snd)

/// Pins the Pdeek labels to the decomposed byte form — `k` (U+006B) followed by a
/// combining macron-below (U+0331), matching Pdeek.displayName, never the
/// precomposed U+1E35. The `\u0331` escapes are normalization-immune, so if a rogue
/// editor rewrites the literal combining marks in Localization.fs to U+1E35 this
/// turns RED — whereas the catalog test above (literal marks on both sides) would
/// silently keep agreeing.
[<Fact>]
let ``UiText Pdeek labels use k followed by a combining macron-below U+0331`` () =
    UiText.text En PdeekLabel =! "Pdeek\u0331:"
    UiText.text En BirthPdeekLabel =! "Birth Pdeek\u0331:"
