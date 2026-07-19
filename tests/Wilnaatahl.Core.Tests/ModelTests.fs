module Wilnaatahl.Tests.ModelTests

open System
open Xunit
open Swensen.Unquote
open System.Collections.Generic
open Wilnaatahl.Model
open Wilnaatahl.Model.FamilyGraph
open Wilnaatahl.Tests.TestData

[<Fact>]
let ``findPerson returns correct person for all ids`` () =
    let graph = createFamilyGraph testPeopleAndParents testCouples []
    let expectedPeople = [| p0; p1; p2; p3; p4 |]

    [ 0..4 ]
    |> List.iter (fun id -> findPerson (PersonId id) graph =! expectedPeople[id])

[<Fact>]
let ``createFamilyGraph handles empty input`` () =
    let graph = createFamilyGraph [] [] []
    // All public API should return empty/throw as appropriate
    <@ findPerson (PersonId 0) graph |> ignore @> |> raises<KeyNotFoundException>

    couples graph |> Seq.toList =! []

[<Fact>]
let ``couples returns all couples`` () =
    let graph = createFamilyGraph testPeopleAndParents testCouples []
    let couplesSet = couples graph |> Set.ofSeq
    couplesSet =! Set.ofList testCouples

[<Fact>]
let ``findChildrenOfCouple returns children in input order for a procreative Couple`` () =
    // testPeopleAndParents lists p2, p3, p4 as children of coupleP0P1 in that order.
    let graph = createFamilyGraph testPeopleAndParents testCouples []
    findChildrenOfCouple coupleP0P1 graph =! [ p2.Id; p3.Id; p4.Id ]

[<Fact>]
let ``findChildrenOfCouple returns the empty list for a childless Couple`` () =
    // Construct a Couple between two roots that no Person references as their parents.
    let childlessCouple = Couple.create (CoupleId 999) p0.Id p1.Id None

    let graph = createFamilyGraph [ p0, None; p1, None ] [ childlessCouple ] []

    findChildrenOfCouple childlessCouple graph =! []

[<Fact>]
let ``findChildrenOfCouple returns the empty list for a Couple absent from the graph`` () =
    // The function's contract is keyed on CoupleId, and a Couple that the graph never
    // saw cannot have any children attributed to it.
    let graph = createFamilyGraph testPeopleAndParents testCouples []
    let strangerCouple = Couple.create (CoupleId 12345) p0.Id p1.Id None
    findChildrenOfCouple strangerCouple graph =! []

[<Fact>]
let ``huwilp returns all unique huwilp`` () =
    let graph = createFamilyGraph testPeopleAndParents testCouples []
    let huwilpSet = huwilp graph
    huwilpSet =! Set.ofList [ WilpName "H"; WilpName "L" ]

type private TreeStats = {
    NodeCount: int
    LeafCount: int
    MaxDepth: int
    VisitedPeople: PersonId[] // Use an array so we can observe sort order
}

[<Fact>]
let ``WilpName returns itself as a string`` () =
    let wilp = WilpName "Test"
    wilp.AsString =! "Test"

[<Fact>]
let ``Kinship.Pdeek exposes the Pdeek of a known Wilp`` () =
    let kinship = Wilp { Name = WilpName "K"; Pdeek = Giskaast }
    kinship.Pdeek =! Some Giskaast

[<Fact>]
let ``Kinship.Pdeek exposes the Pdeek of an UnknownWilp`` () =
    let kinship = UnknownWilp Ganeda
    kinship.Pdeek =! Some Ganeda

[<Fact>]
let ``Kinship.Pdeek is None for NoneProvided`` () =
    (NoneProvided None).Pdeek =! None
    // The note payload must not affect Pdeek resolution.
    (NoneProvided(Some "raised by aunt")).Pdeek =! None

[<Fact>]
let ``Initial peopleAndParents assigns each Wilp to a distinct Pdeek`` () =
    // Wilp A is the primary matriline; per project conventions it is Giskaast (red) so
    // most visible nodes in the rendered tree retain the historical red appearance.
    let huwilpByName =
        Initial.peopleAndParents
        |> Seq.choose (fun (p, _) ->
            match p.Kinship with
            | Wilp w -> Some w
            | _ -> None)
        |> Seq.map (fun w -> w.Name, w)
        |> Map.ofSeq

    huwilpByName |> Map.find (WilpName "A")
    =! { Name = WilpName "A"; Pdeek = Giskaast }

    // All four Pdeek must be represented across the four huwilp so the visualization
    // exercises the full color palette.
    let representedPdeek =
        huwilpByName |> Map.values |> Seq.map (fun w -> w.Pdeek) |> Set.ofSeq

    representedPdeek =! Set.ofList [ Giskaast; Ganeda; LaxSkiik; LaxGibuu ]

[<Fact>]
let ``Initial sample data includes an outside spouse married to multiple members of one Wilp`` () =
    // The renderer draws each marriage of a from-outside spouse as its own node. The
    // sample data must exercise that: some person whose Kinship is not Wilp W must be
    // partnered to at least two distinct members of Wilp W. Without such a case the
    // crossed-connector scenario the renderer guards against would go undemonstrated.
    let personById =
        Initial.peopleAndParents |> List.map (fun (p, _) -> p.Id, p) |> Map.ofList

    let wilpNameOf personId =
        match (personById |> Map.find personId).Kinship with
        | Wilp w -> Some w.Name
        | UnknownWilp _
        | NoneProvided _ -> None

    let partnerWilpsByPerson =
        Initial.couples
        |> List.collect (fun couple ->
            let m1, m2 = couple.Members
            [ m1, m2; m2, m1 ])
        |> List.groupBy fst
        |> List.map (fun (personId, pairs) ->
            // Distinct partner people first, so multiple marriages to the same member
            // count as one member rather than masquerading as "two distinct members".
            let distinctPartnerWilps =
                pairs |> List.map snd |> List.distinct |> List.choose wilpNameOf

            personId, distinctPartnerWilps)

    let isOutsideMultiMarriage (personId, partnerWilps) =
        partnerWilps
        |> List.countBy id
        |> List.exists (fun (wilp, count) -> count >= 2 && wilpNameOf personId <> Some wilp)

    test <@ partnerWilpsByPerson |> List.exists isOutsideMultiMarriage @>

[<Fact>]
let ``createFamilyGraph excludes UnknownWilp people from huwilp`` () =
    // A person whose specific Wilp is unknown contributes no WilpName to the
    // huwilp set, even if their Pdeek matches that of another person with a
    // fully known Wilp. The fully known Wilp is the only entry that should
    // appear in the set.
    let graph =
        createFamilyGraph [ wilpKMember, None; ganedaPdeekOnlyPerson, None ] [] []

    huwilp graph =! Set.singleton (WilpName "K")

[<Fact>]
let ``UnknownWilp person with no Couple does not appear in any forest`` () =
    // Parity with the long-standing NoneProvided behaviour: a Pdeek-only Person
    // who is not married to a Wilp head is silently absent from every rendered
    // forest. The shared Pdeek between `wilpKMember` and `ganedaPdeekOnlyPerson`
    // means a buggy implementation that grouped by Pdeek would mis-include
    // ganedaPdeekOnlyPerson in wilpKMember's forest.
    let graph =
        createFamilyGraph [ wilpKMember, None; ganedaPdeekOnlyPerson, None ] [] []

    let visited =
        huwilp graph
        |> Seq.collect (fun wilpName ->
            visitWilpForest
                wilpName
                id
                id
                (fun partnerId _ -> partnerId)
                (fun parent _ -> parent)
                (fun _ _ -> 0)
                (fun _ _ -> 0)
                graph)
        |> Set.ofSeq

    visited =! Set.singleton wilpKMember.Id

[<Fact>]
let ``visitWilpForest computes correct tree statistics`` () =
    let graph = createFamilyGraph extendedFamily extendedCouples []
    let wilp = WilpName "H"

    let aggregateStats stats = {
        NodeCount = stats |> Seq.sumBy _.NodeCount
        LeafCount = stats |> Seq.sumBy _.LeafCount
        MaxDepth = stats |> Seq.map _.MaxDepth |> Seq.max
        VisitedPeople = stats |> Seq.map _.VisitedPeople |> Array.concat
    }

    // Recursive visitors that accumulate stats in the return value
    let visitLeaf personId = {
        NodeCount = 1
        LeafCount = 1
        MaxDepth = 1
        VisitedPeople = [| personId |]
    }

    let visitFamily parentId partnersAndChildren =
        let processChildGroup (partnerId, childStats) =
            let combinedChildGroupStats = childStats |> aggregateStats

            {
                combinedChildGroupStats with
                    VisitedPeople = Array.append combinedChildGroupStats.VisitedPeople [| partnerId |]
                    NodeCount = combinedChildGroupStats.NodeCount + 1
            }

        let combinedDescendantStats =
            partnersAndChildren |> Array.map processChildGroup |> aggregateStats

        {
            combinedDescendantStats with
                VisitedPeople = Array.append combinedDescendantStats.VisitedPeople [| parentId |]
                NodeCount = combinedDescendantStats.NodeCount + 1
                MaxDepth = combinedDescendantStats.MaxDepth + 1
        }

    // For this test we want a deterministic reverse-by-PersonId order so we can verify
    // that sorting actually happens. visitWilpForest takes two comparators:
    //   - compareTrees orders descendants within a single Couple's child group.
    //   - compareGroups orders the groups themselves.
    // We sort both by the (reversed) PersonId of the eldest descendant so the test data
    // (which only contains procreative Couples, so groups always have a descendant)
    // produces a fully deterministic walk order.
    let comparePeopleReversed (p1: Person) (p2: Person) = compare p2.Id p1.Id

    let compareTrees (t1: WilpTree) (t2: WilpTree) =
        comparePeopleReversed (graph |> findPerson t1.Root) (graph |> findPerson t2.Root)

    let compareGroups (_, descendants1) (_, descendants2) =
        match descendants1, descendants2 with
        | t1 :: _, t2 :: _ -> compareTrees t1 t2
        | _ -> 0 // The fixture has no childless Couples, so this branch is unreachable.

    let totalStats =
        visitWilpForest wilp visitLeaf id (fun partnerId _ -> partnerId) visitFamily compareTrees compareGroups graph
        |> aggregateStats

    let expected = {
        NodeCount = 11
        LeafCount = 6
        MaxDepth = 3

        // Siblings will be sorted in descending order by ID, followed by their non-Wilp partner.
        // Groups of siblings under a partner are sorted before their Wilp parent.
        VisitedPeople = [|
            PersonId 10
            PersonId 7
            PersonId 9
            PersonId 8
            PersonId 6
            PersonId 5
            PersonId 4
            PersonId 3
            PersonId 2
            PersonId 1
            PersonId 0
        |]
    }

    totalStats =! expected

[<Fact>]
let ``visitWilpForest passes the marriage Couple to visitPartner`` () =
    // A Wilp member married to a single outside partner. visitPartner must receive both
    // the partner's PersonId and the Couple linking them, so a partner can be identified
    // per marriage.
    let wilpMember = {
        Person.Empty with
            Id = PersonId 400
            Kinship = Wilp { Name = WilpName "V"; Pdeek = Giskaast }
            Shape = Sphere
    }

    let outsider = { Person.Empty with Id = PersonId 401; Shape = Cube }
    let marriage = Couple.create (CoupleId 400) wilpMember.Id outsider.Id None

    let graph = createFamilyGraph [ wilpMember, None; outsider, None ] [ marriage ] []

    let visitPartner (partnerId: PersonId) (couple: Couple) = partnerId, couple.Id

    let visitFamily _ (groups: ((PersonId * CoupleId) * (PersonId * CoupleId) list seq)[]) =
        groups |> Array.map fst |> List.ofArray

    let neverCompareTrees _ _ = 0
    let neverCompareGroups _ _ = 0

    let results =
        visitWilpForest
            (WilpName "V")
            (fun _ -> [])
            ignore
            visitPartner
            visitFamily
            neverCompareTrees
            neverCompareGroups
            graph
        |> Seq.collect id
        |> Seq.toList

    results =! [ (outsider.Id, marriage.Id) ]

[<Fact>]
let ``allPeople returns all people in the graph`` () =
    let graph = createFamilyGraph testPeopleAndParents testCouples []
    let people = allPeople graph |> Seq.toList

    // Should contain all test data people, regardless of parentage
    people =! [ p0; p1; p2; p3; p4 ]

[<Fact>]
let ``visitWilpForest returns empty sequence for missing Wilp`` () =
    let graph = createFamilyGraph testPeopleAndParents testCouples []
    let missingWilp = WilpName "Nonexistent"

    let trivialCompareTrees (_: WilpTree) (_: WilpTree) = 0
    let trivialCompareGroups (_: Couple * WilpTree list) (_: Couple * WilpTree list) = 0

    let results =
        visitWilpForest
            missingWilp
            (fun _ -> 0)
            id
            (fun partnerId _ -> partnerId)
            (fun _ _ -> 0)
            trivialCompareTrees
            trivialCompareGroups
            graph
        |> Seq.toList

    results =! []

[<Fact>]
let ``Couple.create canonicalizes Members so order does not matter`` () =
    let p1 = PersonId 7
    let p2 = PersonId 3
    let cId = CoupleId 42

    let c1 = Couple.create cId p1 p2 None
    let c2 = Couple.create cId p2 p1 None

    // Two Couples built from the same pair in either order must be structurally equal.
    c1 =! c2
    // The canonical order is lower PersonId first; p2 (id=3) precedes p1 (id=7).
    c1.Members =! (p2, p1)

[<Fact>]
let ``Couple.create rejects equal members`` () =
    let p = PersonId 5
    let cId = CoupleId 1

    let ex =
        Assert.Throws<ArgumentException>(fun () -> Couple.create cId p p None |> ignore)

    ex.ParamName =! "member2"

    // Build the expected message via ArgumentException itself so the framework's
    // "(Parameter 'member2')" suffix formatting stays portable across runtimes.
    let expected =
        ArgumentException("A Couple's two members must differ; got PersonId 5 for both.", "member2")

    ex.Message =! expected.Message

[<Fact>]
let ``Couple.create stores Id and DateOfUnion as supplied`` () =
    let p1 = PersonId 1
    let p2 = PersonId 2
    let cId = CoupleId 99
    let date = DateOnly(2020, 6, 15)

    let c = Couple.create cId p1 p2 (Some date)
    c.Id =! cId
    c.DateOfUnion =! Some date

[<Fact>]
let ``createFamilyGraph throws when a Person references an unknown CoupleId`` () =
    let p1 = { Person.Empty with Id = PersonId 1 }
    let p2 = { Person.Empty with Id = PersonId 2 }
    let people = [ p1, None; p2, Some(CoupleId 99) ]
    let couples: Couple list = []

    let ex =
        Assert.ThrowsAny<exn>(fun () -> createFamilyGraph people couples [] |> ignore)

    test <@ ex.Message = "Person 2 references unknown CoupleId 99; not present in the supplied couples." @>

[<Fact>]
let ``createFamilyGraph throws when a Couple references an unknown PersonId`` () =
    let p1 = { Person.Empty with Id = PersonId 1 }
    let p2 = { Person.Empty with Id = PersonId 2 }
    let unknown = PersonId 99
    let couple = Couple.create (CoupleId 0) p1.Id unknown None
    let people = [ p1, None; p2, None ]
    let couples = [ couple ]

    let ex =
        Assert.ThrowsAny<exn>(fun () -> createFamilyGraph people couples [] |> ignore)

    test <@ ex.Message = "Couple 0 references unknown PersonId 99; not present in the supplied people." @>

[<Fact>]
let ``createFamilyGraph throws on duplicate CoupleId`` () =
    let p1 = { Person.Empty with Id = PersonId 1 }
    let p2 = { Person.Empty with Id = PersonId 2 }
    let p3 = { Person.Empty with Id = PersonId 3 }
    let p4 = { Person.Empty with Id = PersonId 4 }
    let dupId = CoupleId 5
    let c1 = Couple.create dupId p1.Id p2.Id None
    let c2 = Couple.create dupId p3.Id p4.Id None
    let people = [ p1, None; p2, None; p3, None; p4, None ]
    let couples = [ c1; c2 ]

    let ex =
        Assert.ThrowsAny<exn>(fun () -> createFamilyGraph people couples [] |> ignore)

    test <@ ex.Message = "Duplicate CoupleId 5 appears 2 times in the supplied couples." @>

// Captures the tree shape produced by visitWilpForest for use in equality assertions.
type private CapturedTree =
    | CapturedLeaf of PersonId
    | CapturedFamily of wilpParent: PersonId * partnersAndDescendants: (PersonId * CapturedTree list) list

[<Fact>]
let ``buildWilpTree exposes childless Couples as empty PartnersAndDescendants entries`` () =
    let mWilp = {
        Person.Empty with
            Id = PersonId 100
            Kinship = Wilp { Name = WilpName "M"; Pdeek = Giskaast }
            Shape = Sphere
    }

    let pOutsider = { Person.Empty with Id = PersonId 101; Shape = Cube }

    let childlessCouple = Couple.create (CoupleId 200) mWilp.Id pOutsider.Id None
    let people = [ mWilp, None; pOutsider, None ]
    let couples = [ childlessCouple ]

    let graph = createFamilyGraph people couples []

    let visitFamily wilpParent (partnerResults: (PersonId * CapturedTree seq)[]) =
        let partners =
            partnerResults
            |> Array.map (fun (cp, descs) -> cp, Seq.toList descs)
            |> List.ofArray

        CapturedFamily(wilpParent, partners)

    // This fixture has only one Couple so the comparators never fire on a non-empty
    // pair; trivial implementations are sufficient.
    let neverCompareTrees _ _ = 0
    let neverCompareGroups _ _ = 0

    let results =
        visitWilpForest
            (WilpName "M")
            CapturedLeaf
            id
            (fun partnerId _ -> partnerId)
            visitFamily
            neverCompareTrees
            neverCompareGroups
            graph
        |> Seq.toList

    results =! [ CapturedFamily(mWilp.Id, [ (pOutsider.Id, []) ]) ]

// ---------------------------------------------------------------------------
// Name and Pdeek.displayName
// ---------------------------------------------------------------------------

[<Fact>]
let ``Name.AsString returns the underlying text`` () = (Name "Tinker").AsString =! "Tinker"

[<Fact>]
let ``Pdeek.displayName renders the Gitxsan orthography for every clan`` () =
    // Exact spelling is a fixed convention: these carry the underline diacritic
    // (combining U+0331) and a glottal apostrophe that must match precisely.
    Pdeek.displayName LaxGibuu =! "Lax̱ Gibuu"
    Pdeek.displayName LaxSkiik =! "Lax̱ Skiik"
    Pdeek.displayName Ganeda =! "G̱aneda"
    Pdeek.displayName Giskaast =! "Gisḵ'aast"

// ---------------------------------------------------------------------------
// Name holdings: storage, recency ordering, and accessors
// ---------------------------------------------------------------------------

let private holder id = { Person.Empty with Id = PersonId id }
let private held text date order = { Name = Name text; NameDate = date; NameOrder = order }

[<Fact>]
let ``namesHeldBy orders holdings with parseable dates most-recent-first`` () =
    let recent = held "Recent" (on 2000 1 1) None
    let older = held "Older" (on 1990 1 1) None

    let graph =
        createFamilyGraph [ (holder 1, None) ] [] [ PersonId 1, older; PersonId 1, recent ]

    graph |> namesHeldBy (PersonId 1) =! [ recent; older ]

[<Fact>]
let ``namesHeldBy ranks a dated holding ahead of undated ones`` () =
    let dated = held "Dated" (on 2000 1 1) None
    let absentDate = held "AbsentDate" None None
    let undated = held "Undated" None None

    // A holding with a NameDate is the most recent group and heads the list. The
    // two undated holdings share the unordered group and so are ordered
    // alphabetically by Name text ("AbsentDate" before "Undated") — not by input
    // order. The dated holding is supplied at the head so the sort must actively
    // relocate it ahead of the undated ones anyway.
    let graph =
        createFamilyGraph [ (holder 1, None) ] [] [ PersonId 1, dated; PersonId 1, absentDate; PersonId 1, undated ]

    graph |> namesHeldBy (PersonId 1) =! [ dated; absentDate; undated ]

[<Fact>]
let ``namesHeldBy falls back to NameOrder descending when dates are absent`` () =
    let high = held "High" None (Some 5)
    let low = held "Low" None (Some 1)
    let noOrder = held "NoOrder" None None

    let graph =
        createFamilyGraph [ (holder 1, None) ] [] [ PersonId 1, low; PersonId 1, noOrder; PersonId 1, high ]

    graph |> namesHeldBy (PersonId 1) =! [ high; low; noOrder ]

[<Fact>]
let ``namesHeldBy ranks a dated holding ahead of an order-only holding`` () =
    // A holding with a parseable date is in the most-recent group, ahead of any
    // holding that has only a NameOrder — even when that order (99) is far higher
    // than the dated holding's absent order. The date group dominates; order only
    // sorts within the dateless group.
    let datedLowOrder = held "Dated" (on 2000 1 1) None
    let undatedHighOrder = held "Undated" None (Some 99)

    let graph =
        createFamilyGraph [ (holder 1, None) ] [] [ PersonId 1, undatedHighOrder; PersonId 1, datedLowOrder ]

    graph |> namesHeldBy (PersonId 1) =! [ datedLowOrder; undatedHighOrder ]

[<Fact>]
let ``namesHeldBy tiebreaks equal parseable dates on NameOrder descending`` () =
    // Same date on both, so the primary key ties and NameOrder decides:
    // the higher order is more recent.
    let sameDateHighOrder = held "HighOrder" (on 2000 1 1) (Some 5)
    let sameDateLowOrder = held "LowOrder" (on 2000 1 1) (Some 1)

    let graph =
        createFamilyGraph [ (holder 1, None) ] [] [ PersonId 1, sameDateLowOrder; PersonId 1, sameDateHighOrder ]

    graph |> namesHeldBy (PersonId 1) =! [ sameDateHighOrder; sameDateLowOrder ]

[<Fact>]
let ``namesHeldBy tiebreaks equal dates with a present NameOrder ahead of an absent one`` () =
    // Equal parseable dates: the holding carrying a NameOrder is treated as more
    // recent than one with none. Asserted for both input orders, so the comparator
    // is symmetric about which argument holds the order.
    let withOrder = held "WithOrder" (on 2000 1 1) (Some 3)
    let withoutOrder = held "WithoutOrder" (on 2000 1 1) None

    let ordered input =
        createFamilyGraph [ (holder 1, None) ] [] input |> namesHeldBy (PersonId 1)

    ordered [ PersonId 1, withoutOrder; PersonId 1, withOrder ]
    =! [ withOrder; withoutOrder ]

    ordered [ PersonId 1, withOrder; PersonId 1, withoutOrder ]
    =! [ withOrder; withoutOrder ]

[<Fact>]
let ``namesHeldBy tiebreaks equal dates with no orders alphabetically by Name text`` () =
    // Equal parseable dates and neither holding carries a NameOrder, so the
    // alphabetical final tiebreak decides ("Alpha" before "Beta"), not input order.
    let beta = held "Beta" (on 2000 1 1) None
    let alpha = held "Alpha" (on 2000 1 1) None

    let graph =
        createFamilyGraph [ (holder 1, None) ] [] [ PersonId 1, beta; PersonId 1, alpha ]

    graph |> namesHeldBy (PersonId 1) =! [ alpha; beta ]

[<Fact>]
let ``namesHeldBy tiebreaks equal dates with equal NameOrders alphabetically by Name text`` () =
    // Group 1, equal parseable dates AND equal (non-absent) NameOrders, so neither
    // the date nor the order separates them and the alphabetical final tiebreak
    // decides ("Alpha" before "Beta"). Asserted for both input orders so a
    // regression that returned 0 on equal orders (leaning on stable input order)
    // would be caught.
    let ordered input =
        createFamilyGraph [ (holder 1, None) ] [] input |> namesHeldBy (PersonId 1)

    let beta = held "Beta" (on 2000 1 1) (Some 3)
    let alpha = held "Alpha" (on 2000 1 1) (Some 3)

    ordered [ PersonId 1, beta; PersonId 1, alpha ] =! [ alpha; beta ]
    ordered [ PersonId 1, alpha; PersonId 1, beta ] =! [ alpha; beta ]

[<Fact>]
let ``namesHeldBy tiebreaks equal NameOrders in the order-only group alphabetically by Name text`` () =
    // Group 2 (no parseable date), equal NameOrders, so the order does not separate
    // them and the alphabetical final tiebreak decides ("Alpha" before "Beta").
    // Asserted for both input orders so a regression that returned 0 on equal
    // orders (leaning on stable input order) would be caught.
    let ordered input =
        createFamilyGraph [ (holder 1, None) ] [] input |> namesHeldBy (PersonId 1)

    let beta = held "Beta" None (Some 3)
    let alpha = held "Alpha" None (Some 3)

    ordered [ PersonId 1, beta; PersonId 1, alpha ] =! [ alpha; beta ]
    ordered [ PersonId 1, alpha; PersonId 1, beta ] =! [ alpha; beta ]

[<Fact>]
let ``namesHeldBy orders holdings by the three recency groups most-recent-first`` () =
    // The full group ordering in one case: two dated holdings (group 1, date
    // descending), two order-only holdings (group 2, order descending), and two
    // unordered holdings (group 3, alphabetical). Groups never interleave, so the
    // most-recent-first result is:
    //   Cook (1990) > Ledger (1980) | Mason (5) > Piper (2) | Cobbler < Tinker
    let ledger = held "Ledger" (on 1980 1 1) None
    let cook = held "Cook" (on 1990 6 15) None
    let piper = held "Piper" None (Some 2)
    let mason = held "Mason" None (Some 5)
    let tinker = held "Tinker" None None
    let cobbler = held "Cobbler" None None

    let graph =
        createFamilyGraph [ (holder 1, None) ] [] [
            PersonId 1, ledger
            PersonId 1, cook
            PersonId 1, piper
            PersonId 1, mason
            PersonId 1, tinker
            PersonId 1, cobbler
        ]

    graph |> namesHeldBy (PersonId 1)
    =! [ cook; ledger; mason; piper; cobbler; tinker ]

[<Fact>]
let ``namesHeldBy tiebreaks unordered holdings alphabetically by Name text`` () =
    // Both holdings fall in the unordered group — neither has a date or an order.
    // With nothing to separate them, they sort alphabetically by Name text
    // ascending ("Apple" before "Zebra"), regardless of input order.
    let zebra = held "Zebra" None None
    let apple = held "Apple" None None

    let graph =
        createFamilyGraph [ (holder 1, None) ] [] [ PersonId 1, zebra; PersonId 1, apple ]

    graph |> namesHeldBy (PersonId 1) =! [ apple; zebra ]

[<Fact>]
let ``namesHeldBy tiebreaks names by ordinal, not culture-sensitive, comparison`` () =
    // The tiebreak must be ordinal (String.CompareOrdinal), not culture-sensitive,
    // so the ordering is locale-independent and the .NET and Fable/browser builds
    // agree. Ordinal puts all uppercase ASCII (e.g. 'Z' = 90) before all lowercase
    // ('a' = 97), so "Zulu" precedes "alpha"; a culture-sensitive comparison would
    // instead order "alpha" first. Both holdings sit in the unordered group so only
    // the name tiebreak decides.
    let zulu = held "Zulu" None None
    let alpha = held "alpha" None None

    let graph =
        createFamilyGraph [ (holder 1, None) ] [] [ PersonId 1, alpha; PersonId 1, zulu ]

    graph |> namesHeldBy (PersonId 1) =! [ zulu; alpha ]

[<Fact>]
let ``namesHeldBy is empty for a person with no holdings and one absent from the graph`` () =
    let graph = createFamilyGraph [ (holder 1, None) ] [] []
    graph |> namesHeldBy (PersonId 1) =! []
    graph |> namesHeldBy (PersonId 42) =! []

[<Fact>]
let ``allNameHoldings exposes every holding across all holders`` () =
    let a = held "A" None (Some 1)
    let b = held "B" None (Some 1)

    let graph =
        createFamilyGraph [ holder 1, None; holder 2, None ] [] [ PersonId 1, a; PersonId 2, b ]

    graph
    |> allNameHoldings
    |> Seq.sortBy (fun (p: PersonId, _) -> p.AsInt)
    |> Seq.toList
    =! [ PersonId 1, a; PersonId 2, b ]

[<Fact>]
let ``createFamilyGraph throws when a Name holding references an unknown PersonId`` () =
    let ex =
        Assert.ThrowsAny<exn>(fun () ->
            createFamilyGraph [ (holder 1, None) ] [] [ (PersonId 99, held "X" None None) ]
            |> ignore)

    test <@ ex.Message = "Name holding references unknown PersonId 99; not present in the supplied people." @>
