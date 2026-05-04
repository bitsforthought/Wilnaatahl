module Wilnaatahl.Tests.ViewModel.ImportMessagesTests

open Xunit
open Swensen.Unquote
open Wilnaatahl.Persistence
open Wilnaatahl.ViewModel

// ---------------------------------------------------------------------------
// ImportError.toMessage — exact user-facing text (part of the UI contract)
// ---------------------------------------------------------------------------

[<Fact>]
let ``ImportError toMessage InvalidJson includes the parser detail`` () =
    ImportError.toMessage (InvalidJson "invalid json case")
    =! "Could not parse the file as JSON: invalid json case"

[<Fact>]
let ``ImportError toMessage EmptyPeopleArray`` () =
    ImportError.toMessage EmptyPeopleArray =! "The file contains no people."

// ---------------------------------------------------------------------------
// ImportWarning.toMessage — exact text. Exact equality (not Contains) so that a
// placeholder transposition — e.g. swapping couple/person id in UnresolvedMember,
// or dropping the InvalidJson prefix — fails rather than passing on substrings.
// ---------------------------------------------------------------------------

[<Fact>]
let ``ImportWarning toMessage UnresolvedCoupleId`` () =
    ImportWarning.toMessage (UnresolvedCoupleId("Carol", 42))
    =! "Carol references couple #42 which does not exist; treated as a root."

[<Fact>]
let ``ImportWarning toMessage UnresolvedMember`` () =
    ImportWarning.toMessage (UnresolvedMember(100, 99))
    =! "Couple #100 references person #99 which does not exist; couple dropped."

[<Fact>]
let ``ImportWarning toMessage UnresolvedWilpId`` () =
    ImportWarning.toMessage (UnresolvedWilpId("Carol", 5))
    =! "Carol references wilp #5 which does not exist or is unusable; Wilp left unset."

[<Fact>]
let ``ImportWarning toMessage UnparseableDate`` () =
    ImportWarning.toMessage (UnparseableDate("Carol", "normalizedDateOfBirth", "not-iso"))
    =! "Carol: could not parse normalizedDateOfBirth value 'not-iso'."

[<Fact>]
let ``ImportWarning toMessage UnparsableCoupleDate`` () =
    ImportWarning.toMessage (UnparsableCoupleDate(100, "circa 1900"))
    =! "Couple #100: could not parse dateOfUnion value 'circa 1900'."

[<Fact>]
let ``ImportWarning toMessage DuplicatePersonId`` () =
    ImportWarning.toMessage (DuplicatePersonId 7)
    =! "Duplicate person id #7; only the first occurrence was kept."

[<Fact>]
let ``ImportWarning toMessage DuplicateCoupleId`` () =
    ImportWarning.toMessage (DuplicateCoupleId 50)
    =! "Duplicate couple id #50; only the first occurrence was kept."

[<Fact>]
let ``ImportWarning toMessage DuplicateWilpId`` () =
    ImportWarning.toMessage (DuplicateWilpId 8)
    =! "Duplicate wilp id #8; only the first occurrence was kept."

[<Fact>]
let ``ImportWarning toMessage WilpMissingPdeek`` () =
    ImportWarning.toMessage (WilpMissingPdeek 4) =! "Wilp #4 has no pdeek; dropped."

[<Fact>]
let ``ImportWarning toMessage WilpMissingNameAndPdeek`` () =
    ImportWarning.toMessage (WilpMissingNameAndPdeek 9)
    =! "Wilp #9 has neither name nor pdeek; dropped."

[<Fact>]
let ``ImportWarning toMessage UnknownPdeek`` () =
    ImportWarning.toMessage (UnknownPdeek(2, "NotAClan"))
    =! "Wilp #2 has unrecognized pdeek 'NotAClan'; dropped."

// ---------------------------------------------------------------------------
// ImportWarning.summary — exact text so wrong counts, missing commas, reordered
// categories, or pluralization drift all fail.
// ---------------------------------------------------------------------------

[<Fact>]
let ``ImportWarning summary of empty list is empty string`` () = ImportWarning.summary [] =! ""

[<Fact>]
let ``ImportWarning summary counts singular categories`` () =
    ImportWarning.summary [ UnresolvedCoupleId("Carol", 999) ]
    =! "1 unresolved parent couple"

[<Fact>]
let ``ImportWarning summary aggregates same-category warnings into one count`` () =
    let warnings = [
        UnresolvedCoupleId("Carol", 999)
        UnresolvedCoupleId("Dan", 998)
        UnresolvedCoupleId("Greg", 997)
    ]

    ImportWarning.summary warnings =! "3 unresolved parent couples"

[<Fact>]
let ``ImportWarning summary lists multiple categories comma-separated`` () =
    let warnings = [
        UnresolvedCoupleId("Carol", 999)
        UnparseableDate("Dan", "normalizedDateOfBirth", "not-iso")
        DuplicatePersonId 7
    ]

    ImportWarning.summary warnings
    =! "1 unresolved parent couple, 1 unparseable date, 1 duplicate person id"

[<Fact>]
let ``ImportWarning summary categorizes UnresolvedMember as dropped couple`` () =
    ImportWarning.summary [ UnresolvedMember(100, 99); UnresolvedMember(200, 98) ]
    =! "2 dropped couples"

[<Fact>]
let ``ImportWarning summary categorizes UnresolvedWilpId as unresolved wilp`` () =
    ImportWarning.summary [ UnresolvedWilpId("Carol", 5) ] =! "1 unresolved wilp"

[<Fact>]
let ``ImportWarning summary categorizes UnparsableCoupleDate as unparseable couple date`` () =
    ImportWarning.summary [ UnparsableCoupleDate(100, "bad") ]
    =! "1 unparseable couple date"

[<Fact>]
let ``ImportWarning summary categorizes DuplicateCoupleId as duplicate couple id`` () =
    ImportWarning.summary [ DuplicateCoupleId 50 ] =! "1 duplicate couple id"

[<Fact>]
let ``ImportWarning summary categorizes DuplicateWilpId as duplicate wilp id`` () =
    ImportWarning.summary [ DuplicateWilpId 8 ] =! "1 duplicate wilp id"

[<Fact>]
let ``ImportWarning summary collapses all wilp-validation warnings into dropped huwilp`` () =
    let warnings = [ WilpMissingPdeek 4; WilpMissingNameAndPdeek 5; UnknownPdeek(6, "Bogus") ]
    ImportWarning.summary warnings =! "3 dropped huwilp"

[<Fact>]
let ``ImportWarning summary singular dropped wilp uses wilp not huwilp`` () =
    // Guards the n=1 branch for the collapsed wilp-validation category: a
    // regression that always emitted the plural would produce "1 dropped huwilp".
    ImportWarning.summary [ WilpMissingPdeek 1 ] =! "1 dropped wilp"

[<Fact>]
let ``ImportWarning summary pluralizes dropped wilp as huwilp not wilps`` () =
    ImportWarning.summary [ WilpMissingPdeek 1; WilpMissingPdeek 2 ]
    =! "2 dropped huwilp"

[<Fact>]
let ``ImportWarning summary pluralizes unresolved wilp as huwilp not wilps`` () =
    ImportWarning.summary [ UnresolvedWilpId("A", 1); UnresolvedWilpId("B", 2) ]
    =! "2 unresolved huwilp"

[<Fact>]
let ``ImportWarning summary preserves first-seen category order`` () =
    let warnings = [
        DuplicatePersonId 7
        UnresolvedWilpId("Alice", 9)
        UnparseableDate("Bob", "f", "v")
    ]

    ImportWarning.summary warnings
    =! "1 duplicate person id, 1 unresolved wilp, 1 unparseable date"
