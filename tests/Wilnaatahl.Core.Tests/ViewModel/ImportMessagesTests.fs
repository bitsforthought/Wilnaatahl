module Wilnaatahl.Tests.ViewModel.ImportMessagesTests

open Xunit
open Swensen.Unquote
open Wilnaatahl.Model
open Wilnaatahl.Persistence
open Wilnaatahl.ViewModel

// ---------------------------------------------------------------------------
// ImportError.toMessage — exact user-facing text (part of the UI contract)
// ---------------------------------------------------------------------------

[<Fact>]
let ``ImportError toMessage InvalidJson includes the parser detail`` () =
    ImportError.toMessage En (InvalidJson "invalid json case")
    =! "Could not parse the file as JSON: invalid json case"

[<Fact>]
let ``ImportError toMessage EmptyPeopleArray`` () =
    ImportError.toMessage En EmptyPeopleArray =! "The file contains no people."

// ---------------------------------------------------------------------------
// ImportWarning.toMessage — exact text. Exact equality (not Contains) so that a
// placeholder transposition — e.g. swapping couple/person id in UnresolvedMember,
// or dropping the InvalidJson prefix — fails rather than passing on substrings.
// ---------------------------------------------------------------------------

[<Fact>]
let ``ImportWarning toMessage UnresolvedCoupleId`` () =
    ImportWarning.toMessage En (UnresolvedCoupleId("Carol", 42))
    =! "Carol references couple #42 which does not exist; treated as a root."

[<Fact>]
let ``ImportWarning toMessage UnresolvedMember`` () =
    ImportWarning.toMessage En (UnresolvedMember(100, 99))
    =! "Couple #100 references person #99 which does not exist; couple dropped."

[<Fact>]
let ``ImportWarning toMessage UnresolvedWilpId`` () =
    ImportWarning.toMessage En (UnresolvedWilpId("Carol", 5))
    =! "Carol references wilp #5 which does not exist or is unusable; Wilp left unset."

[<Fact>]
let ``ImportWarning toMessage UnparseableDate`` () =
    ImportWarning.toMessage En (UnparseableDate("Carol", "normalizedDateOfBirth", "not-iso"))
    =! "Carol: could not parse normalizedDateOfBirth value 'not-iso'."

[<Fact>]
let ``ImportWarning toMessage UnparsableCoupleDate`` () =
    ImportWarning.toMessage En (UnparsableCoupleDate(100, "circa 1900"))
    =! "Couple #100: could not parse dateOfUnion value 'circa 1900'."

[<Fact>]
let ``ImportWarning toMessage DuplicatePersonId`` () =
    ImportWarning.toMessage En (DuplicatePersonId 7)
    =! "Duplicate person id #7; only the first occurrence was kept."

[<Fact>]
let ``ImportWarning toMessage DuplicateCoupleId`` () =
    ImportWarning.toMessage En (DuplicateCoupleId 50)
    =! "Duplicate couple id #50; only the first occurrence was kept."

[<Fact>]
let ``ImportWarning toMessage DuplicateWilpId`` () =
    ImportWarning.toMessage En (DuplicateWilpId 8)
    =! "Duplicate wilp id #8; only the first occurrence was kept."

[<Fact>]
let ``ImportWarning toMessage WilpMissingPdeek`` () =
    ImportWarning.toMessage En (WilpMissingPdeek 4)
    =! "Wilp #4 has no pdeek; dropped."

[<Fact>]
let ``ImportWarning toMessage WilpMissingNameAndPdeek`` () =
    ImportWarning.toMessage En (WilpMissingNameAndPdeek 9)
    =! "Wilp #9 has neither name nor pdeek; dropped."

[<Fact>]
let ``ImportWarning toMessage UnknownPdeek`` () =
    ImportWarning.toMessage En (UnknownPdeek(2, "NotAClan"))
    =! "Wilp #2 has unrecognized pdeek 'NotAClan'; dropped."

[<Fact>]
let ``ImportWarning toMessage ConflictingWilpPdeek names the wilp and its pdeek`` () =
    // Gisḵ'aast is spelled with a decomposed underlined k (k + U+0331), matching
    // Pdeek.displayName; \u0331 pins that exact form rather than a precomposed ḵ.
    ImportWarning.toMessage En (ConflictingWilpPdeek(1, "H", Giskaast))
    =! "Wilp #1 'H' has pdeek Gisk\u0331'aast but another wilp of the same name has a different pdeek; dropped."

[<Fact>]
let ``ImportWarning toMessage UnresolvedBirthWilpId`` () =
    ImportWarning.toMessage En (UnresolvedBirthWilpId("Carol", 5))
    =! "Carol references birth wilp #5 which does not exist or is unusable; birth Wilp left unset."

[<Fact>]
let ``ImportWarning toMessage BirthWilpNotNamed`` () =
    ImportWarning.toMessage En (BirthWilpNotNamed("Carol", 5))
    =! "Carol references birth wilp #5 which has no name; birth Wilp left unset."

[<Fact>]
let ``ImportWarning toMessage IgnoredKinshipNote`` () =
    ImportWarning.toMessage En (IgnoredKinshipNote "Carol")
    =! "Carol has both a resolved Wilp and a kinship note; the note was ignored."

[<Fact>]
let ``ImportWarning toMessage DuplicateNameId`` () =
    ImportWarning.toMessage En (DuplicateNameId 10)
    =! "Duplicate name id #10; only the first occurrence was kept."

[<Fact>]
let ``ImportWarning toMessage DuplicateNameText`` () =
    ImportWarning.toMessage En (DuplicateNameText "Tinker")
    =! "Duplicate name 'Tinker'; the redundant entry was merged."

[<Fact>]
let ``ImportWarning toMessage UnresolvedNameId places person then name id`` () =
    // UnresolvedNameId carries (personId, nameId); UnresolvedNameHolder carries
    // (nameId, personId). Distinct ids so a swapped placeholder fails here.
    ImportWarning.toMessage En (UnresolvedNameId(3, 77))
    =! "Person #3 holds name #77 which does not exist; holding dropped."

[<Fact>]
let ``ImportWarning toMessage UnresolvedNameHolder places name then person id`` () =
    ImportWarning.toMessage En (UnresolvedNameHolder(77, 3))
    =! "Name #77 is held by person #3 which does not exist; holding dropped."

[<Fact>]
let ``ImportWarning toMessage UnheldName`` () =
    ImportWarning.toMessage En (UnheldName(10, "Tinker"))
    =! "Name #10 'Tinker' is held by nobody; dropped."

[<Fact>]
let ``ImportWarning toMessage SelfCoupledMember`` () =
    ImportWarning.toMessage En (SelfCoupledMember(100, 7))
    =! "Couple #100 lists person #7 as both members; couple dropped."

[<Fact>]
let ``ImportWarning toMessage UnorderedNameHolding places name then person id`` () =
    ImportWarning.toMessage En (UnorderedNameHolding(10, 3))
    =! "Name #10 held by person #3 has no order and no usable date; sorted alphabetically."

[<Fact>]
let ``ImportWarning toMessage UnparseableNameDate names person and raw value`` () =
    ImportWarning.toMessage En (UnparseableNameDate(10, 3, "not-a-date"))
    =! "Name #10 held by person #3 has an unparseable date 'not-a-date'; date ignored."

// ---------------------------------------------------------------------------
// ImportWarning.summary — exact text so wrong counts, missing commas, reordered
// categories, or pluralization drift all fail.
// ---------------------------------------------------------------------------

[<Fact>]
let ``ImportWarning summary of empty list is empty string`` () = ImportWarning.summary En [] =! ""

[<Fact>]
let ``ImportWarning summary counts singular categories`` () =
    ImportWarning.summary En [ UnresolvedCoupleId("Carol", 999) ]
    =! "1 unresolved parent couple"

[<Fact>]
let ``ImportWarning summary aggregates same-category warnings into one count`` () =
    let warnings = [
        UnresolvedCoupleId("Carol", 999)
        UnresolvedCoupleId("Dan", 998)
        UnresolvedCoupleId("Greg", 997)
    ]

    ImportWarning.summary En warnings =! "3 unresolved parent couples"

[<Fact>]
let ``ImportWarning summary lists multiple categories comma-separated`` () =
    let warnings = [
        UnresolvedCoupleId("Carol", 999)
        UnparseableDate("Dan", "normalizedDateOfBirth", "not-iso")
        DuplicatePersonId 7
    ]

    ImportWarning.summary En warnings
    =! "1 unresolved parent couple, 1 unparseable date, 1 duplicate person id"

[<Fact>]
let ``ImportWarning summary categorizes UnresolvedMember as dropped couple`` () =
    ImportWarning.summary En [ UnresolvedMember(100, 99); UnresolvedMember(200, 98) ]
    =! "2 dropped couples"

[<Fact>]
let ``ImportWarning summary categorizes UnresolvedWilpId as unresolved wilp`` () =
    ImportWarning.summary En [ UnresolvedWilpId("Carol", 5) ] =! "1 unresolved wilp"

[<Fact>]
let ``ImportWarning summary categorizes UnparsableCoupleDate as unparseable couple date`` () =
    ImportWarning.summary En [ UnparsableCoupleDate(100, "bad") ]
    =! "1 unparseable couple date"

[<Fact>]
let ``ImportWarning summary categorizes DuplicateCoupleId as duplicate couple id`` () =
    ImportWarning.summary En [ DuplicateCoupleId 50 ] =! "1 duplicate couple id"

[<Fact>]
let ``ImportWarning summary categorizes DuplicateWilpId as duplicate wilp id`` () =
    ImportWarning.summary En [ DuplicateWilpId 8 ] =! "1 duplicate wilp id"

[<Fact>]
let ``ImportWarning summary collapses all wilp-validation warnings into dropped huwilp`` () =
    let warnings = [ WilpMissingPdeek 4; WilpMissingNameAndPdeek 5; UnknownPdeek(6, "Bogus") ]
    ImportWarning.summary En warnings =! "3 dropped huwilp"

[<Fact>]
let ``ImportWarning summary singular dropped wilp uses wilp not huwilp`` () =
    // Guards the n=1 branch for the collapsed wilp-validation category: a
    // regression that always emitted the plural would produce "1 dropped huwilp".
    ImportWarning.summary En [ WilpMissingPdeek 1 ] =! "1 dropped wilp"

[<Fact>]
let ``ImportWarning summary pluralizes dropped wilp as huwilp not wilps`` () =
    ImportWarning.summary En [ WilpMissingPdeek 1; WilpMissingPdeek 2 ]
    =! "2 dropped huwilp"

[<Fact>]
let ``ImportWarning summary singular conflicting-pdeek wilp`` () =
    ImportWarning.summary En [ ConflictingWilpPdeek(1, "H", Giskaast) ]
    =! "1 conflicting-pdeek wilp"

[<Fact>]
let ``ImportWarning summary pluralizes conflicting-pdeek wilp as huwilp not wilps`` () =
    ImportWarning.summary En [ ConflictingWilpPdeek(1, "H", Giskaast); ConflictingWilpPdeek(2, "H", Ganeda) ]
    =! "2 conflicting-pdeek huwilp"

[<Fact>]
let ``ImportWarning summary pluralizes unresolved wilp as huwilp not wilps`` () =
    ImportWarning.summary En [ UnresolvedWilpId("A", 1); UnresolvedWilpId("B", 2) ]
    =! "2 unresolved huwilp"

[<Fact>]
let ``ImportWarning summary preserves first-seen category order`` () =
    let warnings = [
        DuplicatePersonId 7
        UnresolvedWilpId("Alice", 9)
        UnparseableDate("Bob", "f", "v")
    ]

    ImportWarning.summary En warnings
    =! "1 duplicate person id, 1 unresolved wilp, 1 unparseable date"

[<Fact>]
let ``ImportWarning summary collapses both birth-wilp warnings into unresolved birth huwilp`` () =
    ImportWarning.summary En [ UnresolvedBirthWilpId("A", 1); BirthWilpNotNamed("B", 2) ]
    =! "2 unresolved birth huwilp"

[<Fact>]
let ``ImportWarning summary singular birth wilp uses wilp not huwilp`` () =
    ImportWarning.summary En [ BirthWilpNotNamed("A", 1) ]
    =! "1 unresolved birth wilp"

[<Fact>]
let ``ImportWarning summary categorizes IgnoredKinshipNote`` () =
    ImportWarning.summary En [ IgnoredKinshipNote "A"; IgnoredKinshipNote "B" ]
    =! "2 ignored kinship notes"

[<Fact>]
let ``ImportWarning summary singular ignored kinship note`` () =
    ImportWarning.summary En [ IgnoredKinshipNote "A" ] =! "1 ignored kinship note"

[<Fact>]
let ``ImportWarning summary categorizes DuplicateNameId as duplicate name id`` () =
    ImportWarning.summary En [ DuplicateNameId 10 ] =! "1 duplicate name id"

[<Fact>]
let ``ImportWarning summary pluralizes DuplicateNameId as duplicate name ids`` () =
    ImportWarning.summary En [ DuplicateNameId 10; DuplicateNameId 11 ]
    =! "2 duplicate name ids"

[<Fact>]
let ``ImportWarning summary categorizes DuplicateNameText as duplicate name`` () =
    ImportWarning.summary En [ DuplicateNameText "Tinker"; DuplicateNameText "Cobbler" ]
    =! "2 duplicate names"

[<Fact>]
let ``ImportWarning summary singular duplicate name`` () =
    ImportWarning.summary En [ DuplicateNameText "Tinker" ] =! "1 duplicate name"

[<Fact>]
let ``ImportWarning summary collapses both name-holding warnings into dropped name holdings`` () =
    ImportWarning.summary En [ UnresolvedNameId(1, 2); UnresolvedNameHolder(2, 1) ]
    =! "2 dropped name holdings"

[<Fact>]
let ``ImportWarning summary singular dropped name holding`` () =
    ImportWarning.summary En [ UnresolvedNameId(1, 2) ] =! "1 dropped name holding"

[<Fact>]
let ``ImportWarning summary categorizes UnheldName as unheld name`` () =
    ImportWarning.summary En [ UnheldName(10, "Tinker") ] =! "1 unheld name"

[<Fact>]
let ``ImportWarning summary pluralizes UnheldName as unheld names`` () =
    ImportWarning.summary En [ UnheldName(10, "Tinker"); UnheldName(11, "Cobbler") ]
    =! "2 unheld names"

[<Fact>]
let ``ImportWarning summary categorizes SelfCoupledMember as self-coupled couple`` () =
    ImportWarning.summary En [ SelfCoupledMember(100, 7) ]
    =! "1 self-coupled couple"

[<Fact>]
let ``ImportWarning summary pluralizes SelfCoupledMember as self-coupled couples`` () =
    ImportWarning.summary En [ SelfCoupledMember(100, 7); SelfCoupledMember(200, 8) ]
    =! "2 self-coupled couples"

[<Fact>]
let ``ImportWarning summary categorizes UnorderedNameHolding as unordered name`` () =
    ImportWarning.summary En [ UnorderedNameHolding(10, 3) ] =! "1 unordered name"

[<Fact>]
let ``ImportWarning summary pluralizes UnorderedNameHolding as unordered names`` () =
    ImportWarning.summary En [ UnorderedNameHolding(10, 3); UnorderedNameHolding(11, 4) ]
    =! "2 unordered names"

[<Fact>]
let ``ImportWarning summary categorizes UnparseableNameDate as unparseable name date`` () =
    ImportWarning.summary En [ UnparseableNameDate(10, 3, "x") ]
    =! "1 unparseable name date"

[<Fact>]
let ``ImportWarning summary pluralizes UnparseableNameDate as unparseable name dates`` () =
    ImportWarning.summary En [ UnparseableNameDate(10, 3, "x"); UnparseableNameDate(11, 4, "y") ]
    =! "2 unparseable name dates"
