module Wilnaatahl.Tests.SceneTests

open System
open Xunit
open Swensen.Unquote
open Wilnaatahl.ViewModel
open Wilnaatahl.Model
open Wilnaatahl.Model.FamilyGraph
open Wilnaatahl.Tests.TestData
open Wilnaatahl.Tests.TestUtils

let private mapFamily (family: RenderedFamily<TestFamilyMember>) =
    let parent1, parent2 = family.Parents

    {|
        Parents = parent1.Id, parent2.Id
        Children = family.Children |> List.map _.Id
    |}

[<Fact>]
let ``ExtractFamilies produces correct results`` () =
    let graph = createFamilyGraph testPeopleAndParents testCouples []

    let families =
        Scene.extractFamilies graph initialNodes |> Seq.toList |> List.map mapFamily

    families.Length =! 1
    let fam = families.Head
    fam.Parents =! (0, 1)

    Set.ofList fam.Children =! Set.ofList [ 2; 3; 4 ]

[<Fact>]
let ``extractFamilies yields RenderedFamily with empty Children for a childless Couple`` () =
    // Set up a graph that has both a childless Couple (from the shared TestData
    // fixture) and a small procreative Couple in the same Wilp. Both should appear
    // as RenderedFamily values; only the childless one should have an empty
    // Children list.
    let wilpName = WilpName "Q"

    let procreativeHead = {
        Person.Empty with
            Id = PersonId 110
            Kinship = childlessWilp
            Shape = Sphere
    }

    let procreativeSpouse = { Person.Empty with Id = PersonId 111; Shape = Cube }

    let onlyChild = {
        Person.Empty with
            Id = PersonId 112
            Kinship = childlessWilp
            Shape = Sphere
    }

    let procreativeCouple =
        Couple.create (CoupleId 110) procreativeHead.Id procreativeSpouse.Id None

    let people = [
        childlessHead, None
        childlessPartner, None
        procreativeHead, None
        procreativeSpouse, None
        onlyChild, Some procreativeCouple.Id
    ]

    let couples = [ childlessCouple; procreativeCouple ]
    let graph = createFamilyGraph people couples []

    let nodes = [
        TestFamilyMember(childlessHead, wilpName, MemberNode childlessHead.Id)
        TestFamilyMember(childlessPartner, wilpName, MemberNode childlessPartner.Id)
        TestFamilyMember(procreativeHead, wilpName, MemberNode procreativeHead.Id)
        TestFamilyMember(procreativeSpouse, wilpName, MemberNode procreativeSpouse.Id)
        TestFamilyMember(onlyChild, wilpName, MemberNode onlyChild.Id)
    ]

    let families = Scene.extractFamilies graph nodes |> Seq.toList |> List.map mapFamily

    families.Length =! 2

    let childlessFamily = families |> List.find (fun f -> List.isEmpty f.Children)

    Set.ofList [ fst childlessFamily.Parents; snd childlessFamily.Parents ]
    =! Set.ofList [ childlessHead.Id.AsInt; childlessPartner.Id.AsInt ]

    let procreativeFamily =
        families |> List.find (fun f -> not (List.isEmpty f.Children))

    Set.ofList [ fst procreativeFamily.Parents; snd procreativeFamily.Parents ]
    =! Set.ofList [ procreativeHead.Id.AsInt; procreativeSpouse.Id.AsInt ]

    procreativeFamily.Children =! [ onlyChild.Id.AsInt ]

[<Fact>]
let ``layoutGraph assigns correct positions`` () =
    let graph = createFamilyGraph extendedFamily extendedCouples []
    let rootOffset, rootBox = Scene.layoutGraph (WilpName "H") graph

    let actual =
        setPositions (rootOffset, rootBox)
        |> List.ofSeq
        |> List.sortBy (fun (p, _) -> p.AsInt)

    let expected = [
        PersonId 0, { X = -0.975<w>; Y = 0.0<w>; Z = 0.0<w> }
        PersonId 1, { X = 0.975<w>; Y = 0.0<w>; Z = 0.0<w> }
        PersonId 2, { X = -3.9<w>; Y = -2.0<w>; Z = 0.0<w> }
        PersonId 3, { X = -1.95<w>; Y = -2.0<w>; Z = 0.0<w> }
        PersonId 4, { X = 0.0<w>; Y = -2.0<w>; Z = 0.0<w> }
        PersonId 5, { X = 4.3875<w>; Y = -2.0<w>; Z = 0.0<w> }
        PersonId 6, { X = 1.95<w>; Y = -2.0<w>; Z = 0.0<w> }
        PersonId 7, { X = 6.825<w>; Y = -2.0<w>; Z = 0.0<w> }
        PersonId 8, { X = 3.9<w>; Y = -4.0<w>; Z = 0.0<w> }
        PersonId 9, { X = 1.95<w>; Y = -4.0<w>; Z = 0.0<w> }
        PersonId 10, { X = 5.85<w>; Y = -4.0<w>; Z = 0.0<w> }
    ]

    let areCoordinatesNearEqual a e = abs (a - e) <= LayoutBox.nearZero

    let areVectorsNearEqual a e =
        areCoordinatesNearEqual a.X e.X
        && areCoordinatesNearEqual a.Y e.Y
        && areCoordinatesNearEqual a.Z e.Z

    // Unforunately, due to the nature of floating point numbers, structural equality
    // isn't always going to work here. Instead, we iterate over the positions and
    // check co-ordinates with some tolerance.
    List.zip actual expected
    |> List.map (fun ((actualPersonId, actualOffset), (expectedPersonId, expectedOffset)) ->
        test
            <@
                actualPersonId = expectedPersonId
                && areVectorsNearEqual actualOffset expectedOffset
            @>)

[<Fact>]
let ``layoutGraph positions partner of a childless Couple adjacent to the Wilp parent`` () =
    // Use the shared childless-Couple fixture: the Wilp parent (childlessHead) and the
    // outsider partner (childlessPartner) form the only Couple in the Wilp. After
    // layout, both should have positions and the partner should sit exactly one
    // default spacing unit horizontally from the Wilp parent (i.e. adjacent, with
    // no other partners or descendants between them).
    let graph =
        createFamilyGraph [ childlessHead, None; childlessPartner, None ] [ childlessCouple ] []

    let rootOffset, rootBox = Scene.layoutGraph (WilpName "Q") graph
    let positions = setPositions (rootOffset, rootBox) |> List.ofSeq |> Map.ofList

    let headPos = positions |> Map.find childlessHead.Id
    let partnerPos = positions |> Map.find childlessPartner.Id

    // No phantom positions for absent children.
    Map.count positions =! 2

    // Adjacency: partner is exactly one default horizontal spacing away on the X
    // axis, at the same Y as the Wilp parent (since there are no descendants to
    // push the partner row downward).
    let coordsNearEqual a e = abs (a - e) <= LayoutBox.nearZero
    let xGap = abs (partnerPos.X - headPos.X)
    test <@ coordsNearEqual xGap SceneConstants.defaultXSpacing @>
    test <@ coordsNearEqual partnerPos.Y headPos.Y @>

[<Fact>]
let ``layoutGraph sorts children by DateOfBirth then BirthOrder`` () =
    // Build a family with children that exercise all 3 DateOfBirth comparison paths:
    //   childA (DoB 2000/6/1)  vs childB (DoB 2005/1/1) → dob1 < dob2
    //   childB (DoB 2005/1/1)  vs childA (DoB 2000/6/1) → dob1 > dob2
    //   childC (DoB 2000/6/1, BirthOrder=1) vs childA (DoB 2000/6/1, BirthOrder=0) → equal DoB, fallback to BirthOrder
    let mother = {
        Person.Empty with
            Id = PersonId 100
            ColonialName = Some "Mother"
            Shape = Sphere
            Kinship = Wilp { Name = WilpName "T"; Pdeek = Giskaast }
    }

    let father = {
        Person.Empty with
            Id = PersonId 101
            ColonialName = Some "Father"
            Shape = Cube
    }

    let childA = {
        Person.Empty with
            Id = PersonId 102
            ColonialName = Some "ChildA"
            Shape = Sphere
            Kinship = Wilp { Name = WilpName "T"; Pdeek = Giskaast }
            DateOfBirth = Some(System.DateOnly(2000, 6, 1))
            BirthOrder = 0
    }

    let childB = {
        Person.Empty with
            Id = PersonId 103
            ColonialName = Some "ChildB"
            Shape = Sphere
            Kinship = Wilp { Name = WilpName "T"; Pdeek = Giskaast }
            DateOfBirth = Some(System.DateOnly(2005, 1, 1))
    }

    let childC = {
        Person.Empty with
            Id = PersonId 104
            ColonialName = Some "ChildC"
            Shape = Sphere
            Kinship = Wilp { Name = WilpName "T"; Pdeek = Giskaast }
            DateOfBirth = Some(System.DateOnly(2000, 6, 1))
            BirthOrder = 1
    }

    let parentsCouple = Couple.create (CoupleId 200) mother.Id father.Id None

    let family = [
        mother, None
        father, None
        childA, Some parentsCouple.Id
        childB, Some parentsCouple.Id
        childC, Some parentsCouple.Id
    ]

    let graph = createFamilyGraph family [ parentsCouple ] []
    let _, rootBox = Scene.layoutGraph (WilpName "T") graph

    // Collect the X positions of children from the layout.
    let childPositions =
        setPositions ({ X = 0.0<w>; Y = 0.0<w>; Z = 0.0<w> }, rootBox)
        |> List.ofSeq
        |> List.choose (fun (pid, pos) ->
            match pid with
            | PersonId 102
            | PersonId 103
            | PersonId 104 -> Some(pid, pos.X)
            | _ -> None)
        |> List.sortBy snd

    // Expected sort order: childA (DoB 2000, order 0), childC (DoB 2000, order 1), childB (DoB 2005)
    let sortedIds = childPositions |> List.map fst
    sortedIds =! [ PersonId 102; PersonId 104; PersonId 103 ]

// ----- compareCouplesByEffectiveDate -------------------------------------------------

/// Constructs a tiny FamilyGraph containing the supplied Couples and the four
/// shared `anon*` Persons that the comparator tests below use to build Couples
/// from. Optionally also includes a single child of one of the Couples — pass
/// `Some (child, parentCouple)` to add a Person who points to `parentCouple` as
/// their parents. Returns the graph along with the matching `descendants` list
/// that can be handed to `Scene.compareCouplesByEffectiveDate` (`[]` when no
/// child was supplied, `[ Leaf child.Id ]` when one was).
let private makeComparatorGraph (couples: Couple list) (procreativeChild: (Person * Couple) option) =
    let baseEntries = [ anon1, None; anon2, None; anon3, None; anon4, None ]

    let people, descendants =
        match procreativeChild with
        | None -> baseEntries, []
        | Some(child, parentCouple) -> baseEntries @ [ (child, Some parentCouple.Id) ], [ Leaf child.Id ]

    createFamilyGraph people couples [], descendants

[<Fact>]
let ``compareCouplesByEffectiveDate: childless-with-date sorts before procreative-with-later-DoB`` () =
    // c1's DateOfUnion (1990) is earlier than c2's eldest child's DateOfBirth (1995),
    // so c1 (childless) sorts before c2 (procreative).
    let child = {
        Person.Empty with
            Id = PersonId 250
            DateOfBirth = Some(DateOnly(1995, 1, 1))
    }

    let cChildless =
        Couple.create (CoupleId 1) anon1.Id anon2.Id (Some(DateOnly(1990, 1, 1)))

    let cProcreative = Couple.create (CoupleId 2) anon3.Id anon4.Id None

    let graph, descendants =
        makeComparatorGraph [ cChildless; cProcreative ] (Some(child, cProcreative))

    Scene.compareCouplesByEffectiveDate graph (cChildless, []) (cProcreative, descendants)
    <! 0

    Scene.compareCouplesByEffectiveDate graph (cProcreative, descendants) (cChildless, [])
    >! 0

[<Fact>]
let ``compareCouplesByEffectiveDate: both childless, equal dates, tie-break by CoupleId`` () =
    let date = DateOnly(2000, 1, 1)
    let cLow = Couple.create (CoupleId 10) anon1.Id anon2.Id (Some date)
    let cHigh = Couple.create (CoupleId 20) anon3.Id anon4.Id (Some date)
    let graph, _ = makeComparatorGraph [ cLow; cHigh ] None

    Scene.compareCouplesByEffectiveDate graph (cLow, []) (cHigh, []) <! 0
    Scene.compareCouplesByEffectiveDate graph (cHigh, []) (cLow, []) >! 0

[<Fact>]
let ``compareCouplesByEffectiveDate: both childless with no date, fall back to CoupleId`` () =
    let cLow = Couple.create (CoupleId 10) anon1.Id anon2.Id None
    let cHigh = Couple.create (CoupleId 20) anon3.Id anon4.Id None
    let graph, _ = makeComparatorGraph [ cLow; cHigh ] None

    Scene.compareCouplesByEffectiveDate graph (cLow, []) (cHigh, []) <! 0

[<Fact>]
let ``compareCouplesByEffectiveDate: when one childless Couple lacks a date, fall back to CoupleId`` () =
    // Per the comparator contract, a missing effective date on either side drops both
    // sides to a CoupleId comparison — the dated side does NOT win automatically.
    // Give the dated Couple the higher CoupleId so the test would fail under the old
    // "dated wins" rule (which would put it first regardless of CoupleId).
    let cUndated = Couple.create (CoupleId 10) anon1.Id anon2.Id None

    let cDated =
        Couple.create (CoupleId 20) anon3.Id anon4.Id (Some(DateOnly(2000, 1, 1)))

    let graph, _ = makeComparatorGraph [ cUndated; cDated ] None

    Scene.compareCouplesByEffectiveDate graph (cUndated, []) (cDated, []) <! 0
    Scene.compareCouplesByEffectiveDate graph (cDated, []) (cUndated, []) >! 0

[<Fact>]
let ``compareCouplesByEffectiveDate: childless-no-date vs procreative-with-DoB-on-eldest falls back to CoupleId`` () =
    // Childless without DateOfUnion has no effective date; the procreative Couple's
    // eldest has a DoB. Since one side is undated, both compare by CoupleId — the
    // dated side does NOT win automatically. Give the procreative (dated) Couple the
    // higher CoupleId so the test would fail under the old "dated wins" rule.
    let child = {
        Person.Empty with
            Id = PersonId 250
            DateOfBirth = Some(DateOnly(1990, 1, 1))
    }

    let cChildless = Couple.create (CoupleId 10) anon1.Id anon2.Id None
    let cProcreative = Couple.create (CoupleId 20) anon3.Id anon4.Id None

    let graph, descendants =
        makeComparatorGraph [ cChildless; cProcreative ] (Some(child, cProcreative))

    Scene.compareCouplesByEffectiveDate graph (cChildless, []) (cProcreative, descendants)
    <! 0

    Scene.compareCouplesByEffectiveDate graph (cProcreative, descendants) (cChildless, [])
    >! 0

[<Fact>]
let ``compareCouplesByEffectiveDate: childless-no-date vs procreative-with-no-DoB-on-eldest`` () =
    // Neither side is dated; fall back to CoupleId comparison.
    let child = { Person.Empty with Id = PersonId 250 } // no DoB
    let cChildless = Couple.create (CoupleId 10) anon1.Id anon2.Id None
    let cProcreative = Couple.create (CoupleId 20) anon3.Id anon4.Id None

    let graph, descendants =
        makeComparatorGraph [ cChildless; cProcreative ] (Some(child, cProcreative))

    Scene.compareCouplesByEffectiveDate graph (cChildless, []) (cProcreative, descendants)
    <! 0

    Scene.compareCouplesByEffectiveDate graph (cProcreative, descendants) (cChildless, [])
    >! 0

[<Fact>]
let ``compareCouplesByEffectiveDate: procreative-with-no-DoB vs childless-with-date falls back to CoupleId`` () =
    // c1 procreative whose eldest has no DoB; c2 childless with a date. The procreative
    // side is undated, so the comparison falls back to CoupleId — the dated side does
    // NOT automatically win.
    let child = { Person.Empty with Id = PersonId 250 } // no DoB
    let cProcreative = Couple.create (CoupleId 10) anon1.Id anon2.Id None

    let cChildless =
        Couple.create (CoupleId 20) anon3.Id anon4.Id (Some(DateOnly(2000, 1, 1)))

    let graph, descendants =
        makeComparatorGraph [ cProcreative; cChildless ] (Some(child, cProcreative))

    Scene.compareCouplesByEffectiveDate graph (cProcreative, descendants) (cChildless, [])
    <! 0

[<Fact>]
let ``compareCouplesByEffectiveDate: procreative-with-DoB vs childless-no-date falls back to CoupleId`` () =
    // c1 procreative with an eldest DoB; c2 childless without a date. The childless
    // side is undated, so the comparison falls back to CoupleId — the dated side does
    // NOT automatically win. Give the procreative (dated) Couple the higher CoupleId
    // so the test would fail under the old "dated wins" rule.
    let child = {
        Person.Empty with
            Id = PersonId 250
            DateOfBirth = Some(DateOnly(2000, 1, 1))
    }

    let cChildless = Couple.create (CoupleId 10) anon1.Id anon2.Id None
    let cProcreative = Couple.create (CoupleId 20) anon3.Id anon4.Id None

    let graph, descendants =
        makeComparatorGraph [ cProcreative; cChildless ] (Some(child, cProcreative))

    Scene.compareCouplesByEffectiveDate graph (cProcreative, descendants) (cChildless, [])
    >! 0

// ----- layoutGraph integration with the comparator -------------------------------------

[<Fact>]
let ``layoutGraph interleaves childless and procreative Couples by effective date`` () =
    // Same scenario as the comparator unit tests, but exercised end-to-end through
    // layoutGraph. One Wilp parent has three Couples whose effective dates of union
    // should sort the partner row in this order regardless of the input order:
    //   - Childless Couple with DateOfUnion 1990 (earliest)
    //   - Procreative Couple whose eldest child was born 1995
    //   - Childless Couple with DateOfUnion 2000 (latest)
    let wilpName = WilpName "S"
    let kinship = Wilp { Name = wilpName; Pdeek = Giskaast }

    let wilpHead = {
        Person.Empty with
            Id = PersonId 300
            Kinship = kinship
            Shape = Sphere
    }

    let earlyPartner = { Person.Empty with Id = PersonId 301; Shape = Cube }
    let midPartner = { Person.Empty with Id = PersonId 302; Shape = Cube }
    let latePartner = { Person.Empty with Id = PersonId 303; Shape = Cube }

    let child = {
        Person.Empty with
            Id = PersonId 304
            Kinship = kinship
            Shape = Sphere
            DateOfBirth = Some(DateOnly(1995, 6, 15))
    }

    // Supplied in scrambled order so the test would fail on a stable-but-wrong sort.
    let coupleMid = Couple.create (CoupleId 301) wilpHead.Id midPartner.Id None

    let coupleLate =
        Couple.create (CoupleId 302) wilpHead.Id latePartner.Id (Some(DateOnly(2000, 1, 1)))

    let coupleEarly =
        Couple.create (CoupleId 303) wilpHead.Id earlyPartner.Id (Some(DateOnly(1990, 1, 1)))

    let people = [
        wilpHead, None
        earlyPartner, None
        midPartner, None
        latePartner, None
        child, Some coupleMid.Id
    ]

    let couples = [ coupleMid; coupleLate; coupleEarly ]
    let graph = createFamilyGraph people couples []

    let rootOffset, rootBox = Scene.layoutGraph wilpName graph
    let positions = setPositions (rootOffset, rootBox) |> List.ofSeq |> Map.ofList

    // The three partners should be laid out left-to-right in effective-date order.
    let xOf personId = (Map.find personId positions).X
    test <@ xOf earlyPartner.Id < xOf midPartner.Id @>
    test <@ xOf midPartner.Id < xOf latePartner.Id @>

// ----- enumerateHuwilpToRender -----------------------------------------------------------

[<Fact>]
let ``enumerateHuwilpToRender for the chosen Wilp excludes people from other huwilp`` () =
    // Two unrelated huwilp, each with a single root member. The renderer
    // currently picks the alphabetically-first Wilp ("A") and must not list
    // people who don't appear in that Wilp's forest — otherwise the layout
    // pass blows up looking them up in the Wilp-A layout map.
    let wilpA = Wilp { Name = WilpName "A"; Pdeek = Giskaast }
    let wilpB = Wilp { Name = WilpName "B"; Pdeek = Ganeda }

    let alice = { Person.Empty with Id = PersonId 0; Kinship = wilpA; Shape = Sphere }

    let bob = { Person.Empty with Id = PersonId 1; Kinship = wilpB; Shape = Sphere }

    let graph = createFamilyGraph [ alice, None; bob, None ] [] []

    let result = Scene.enumerateHuwilpToRender graph

    let aPeople =
        result |> Map.find (WilpName "A") |> Seq.map (snd >> _.Id) |> Set.ofSeq

    aPeople =! Set.singleton (PersonId 0)

[<Fact>]
let ``enumerateHuwilpToRender includes outside-Wilp partners that appear in the Wilp's tree`` () =
    // The Wilp parent's partner is from outside the Wilp but appears in the
    // rendered tree as the partner side of a Family node — they must be
    // included so the rendering pass can place them.
    let wilpA = Wilp { Name = WilpName "A"; Pdeek = Giskaast }
    let wilpB = Wilp { Name = WilpName "B"; Pdeek = Ganeda }

    let alice = { Person.Empty with Id = PersonId 0; Kinship = wilpA; Shape = Sphere }

    let outsidePartner = { Person.Empty with Id = PersonId 1; Kinship = wilpB; Shape = Cube }

    let aliceAndPartner = Couple.create (CoupleId 100) alice.Id outsidePartner.Id None

    let graph =
        createFamilyGraph [ alice, None; outsidePartner, None ] [ aliceAndPartner ] []

    let result = Scene.enumerateHuwilpToRender graph

    let aPeople =
        result |> Map.find (WilpName "A") |> Seq.map (snd >> _.Id) |> Set.ofSeq

    aPeople =! Set.ofList [ PersonId 0; PersonId 1 ]

[<Fact>]
let ``enumerateHuwilpToRender picks the wilp with the most members`` () =
    // Two huwilp: "A" has one member, "B" has two. The most-populous Wilp
    // ("B") must be chosen even though "A" sorts first alphabetically.
    let wilpA = Wilp { Name = WilpName "A"; Pdeek = Giskaast }
    let wilpB = Wilp { Name = WilpName "B"; Pdeek = Ganeda }

    let alice = { Person.Empty with Id = PersonId 0; Kinship = wilpA; Shape = Sphere }

    let bob = { Person.Empty with Id = PersonId 1; Kinship = wilpB; Shape = Sphere }

    let carol = { Person.Empty with Id = PersonId 2; Kinship = wilpB; Shape = Sphere }

    let graph = createFamilyGraph [ alice, None; bob, None; carol, None ] [] []

    let result = Scene.enumerateHuwilpToRender graph
    result |> Map.containsKey (WilpName "B") =! true
    result |> Map.containsKey (WilpName "A") =! false

[<Fact>]
let ``enumerateHuwilpToRender breaks ties on member count by alphabetical name`` () =
    // Two huwilp with the same member count. The alphabetically-first name
    // ("Bravo" < "Charlie") must win.
    let wilpBravo = Wilp { Name = WilpName "Bravo"; Pdeek = Giskaast }
    let wilpCharlie = Wilp { Name = WilpName "Charlie"; Pdeek = Ganeda }

    let alice = {
        Person.Empty with
            Id = PersonId 0
            Kinship = wilpBravo
            Shape = Sphere
    }

    let bob = {
        Person.Empty with
            Id = PersonId 1
            Kinship = wilpCharlie
            Shape = Sphere
    }

    let graph = createFamilyGraph [ alice, None; bob, None ] [] []

    let result = Scene.enumerateHuwilpToRender graph
    result |> Map.containsKey (WilpName "Bravo") =! true
    result |> Map.containsKey (WilpName "Charlie") =! false

[<Fact>]
let ``enumerateHuwilpToRender counts members by Person.Kinship, not partners-from-outside`` () =
    // wilpA has 1 member (alice). wilpB has 1 member (bob) plus a partner-from-
    // outside (carol, whose Kinship is wilpA). If "members" were counted as
    // "person ids that appear in the Wilp's tree", wilpB would falsely win
    // 2 vs 1. The correct count is by Person.Kinship, so this is a tie and
    // alphabetical wilpA wins.
    let wilpA = Wilp { Name = WilpName "A"; Pdeek = Giskaast }
    let wilpB = Wilp { Name = WilpName "B"; Pdeek = Ganeda }

    let alice = { Person.Empty with Id = PersonId 0; Kinship = wilpA; Shape = Sphere }

    let bob = { Person.Empty with Id = PersonId 1; Kinship = wilpB; Shape = Sphere }

    let carol = { Person.Empty with Id = PersonId 2; Kinship = wilpA; Shape = Sphere }

    let bobAndCarol = Couple.create (CoupleId 100) bob.Id carol.Id None

    let graph =
        createFamilyGraph [ alice, None; bob, None; carol, None ] [ bobAndCarol ] []

    let result = Scene.enumerateHuwilpToRender graph
    result |> Map.containsKey (WilpName "A") =! true
    result |> Map.containsKey (WilpName "B") =! false

[<Fact>]
let ``enumerateHuwilpToRender emits a separate PartnerNode per marriage of an outside spouse`` () =
    // The shared outside spouse is married to two "MM" members. Each marriage must
    // surface as its own PartnerNode (keyed by that marriage's CoupleId) so the two
    // marriages render as distinct nodes; the members surface once each as MemberNodes.
    let graph = createFamilyGraph multiMarriagePeople multiMarriageCouples []

    let nodes =
        Scene.enumerateHuwilpToRender graph |> Map.find (WilpName "MM") |> Seq.toList

    let spouseNodeKeys =
        nodes
        |> List.filter (fun (_, person) -> person.Id = multiMarriageSpouse.Id)
        |> List.map fst

    Set.ofList spouseNodeKeys
    =! Set.ofList [
        PartnerNode(multiMarriageSpouse.Id, multiMarriageCouple1.Id)
        PartnerNode(multiMarriageSpouse.Id, multiMarriageCouple2.Id)
    ]

    let memberNodeKeys =
        nodes
        |> List.map fst
        |> List.filter (function
            | MemberNode _ -> true
            | PartnerNode _ -> false)

    Set.ofList memberNodeKeys
    =! Set.ofList [ MemberNode multiMarriageMember1.Id; MemberNode multiMarriageMember2.Id ]

[<Fact>]
let ``extractFamilies resolves each marriage to its own partner node`` () =
    // The outside spouse has a dedicated PartnerNode per marriage. extractFamilies must
    // attach each Couple's spouse-bar to that marriage's partner node (not a shared one),
    // while the member parent reuses its single MemberNode.
    let graph = createFamilyGraph multiMarriagePeople multiMarriageCouples []
    let wilpName = WilpName "MM"

    let member1Key = MemberNode multiMarriageMember1.Id
    let member2Key = MemberNode multiMarriageMember2.Id
    let spouseKey1 = PartnerNode(multiMarriageSpouse.Id, multiMarriageCouple1.Id)
    let spouseKey2 = PartnerNode(multiMarriageSpouse.Id, multiMarriageCouple2.Id)

    let nodes = [
        TestFamilyMember(multiMarriageMember1, wilpName, member1Key)
        TestFamilyMember(multiMarriageMember2, wilpName, member2Key)
        TestFamilyMember(multiMarriageSpouse, wilpName, spouseKey1)
        TestFamilyMember(multiMarriageSpouse, wilpName, spouseKey2)
    ]

    let familyKeys =
        Scene.extractFamilies graph nodes
        |> Seq.map (fun family ->
            let parent1, parent2 = family.Parents

            Set.ofList [
                (parent1 :> IFamilyMemberInfo).NodeKey
                (parent2 :> IFamilyMemberInfo).NodeKey
            ])
        |> Set.ofSeq

    // One family per marriage; each pairs the member's MemberNode with that marriage's PartnerNode.
    familyKeys
    =! Set.ofList [ Set.ofList [ member1Key; spouseKey1 ]; Set.ofList [ member2Key; spouseKey2 ] ]

[<Fact>]
let ``enumerateHuwilpToRender emits one MemberNode per member for an endogamous couple`` () =
    // Two members of the same rendered Wilp married to each other. Because neither
    // partner is from outside the Wilp, each must surface exactly once as a MemberNode
    // with no PartnerNode — otherwise each member would render as two nodes.
    let graph = createFamilyGraph endogamyPeople endogamyCouples []

    let nodes =
        Scene.enumerateHuwilpToRender graph |> Map.find (WilpName "EN") |> Seq.toList

    let nodeKeys = nodes |> List.map fst

    // Exactly two node entities, one MemberNode per member and no PartnerNode.
    nodeKeys.Length =! 2

    Set.ofList nodeKeys
    =! Set.ofList [ MemberNode endogamyMember1.Id; MemberNode endogamyMember2.Id ]

[<Fact>]
let ``extractFamilies resolves an endogamous couple's parents to two MemberNodes`` () =
    // Feed extractFamilies the production node set (from enumerateHuwilpToRender) so
    // the realistic key shape is exercised: an endogamous couple must resolve both
    // parents to their MemberNodes, yielding a single family joining the two members.
    let graph = createFamilyGraph endogamyPeople endogamyCouples []
    let wilpName = WilpName "EN"

    let nodes =
        Scene.enumerateHuwilpToRender graph
        |> Map.find wilpName
        |> Seq.map (fun (nodeKey, person) -> TestFamilyMember(person, wilpName, nodeKey))

    let familyKeys =
        Scene.extractFamilies graph nodes
        |> Seq.map (fun family ->
            let parent1, parent2 = family.Parents

            Set.ofList [
                (parent1 :> IFamilyMemberInfo).NodeKey
                (parent2 :> IFamilyMemberInfo).NodeKey
            ])
        |> Seq.toList

    familyKeys
    =! [ Set.ofList [ MemberNode endogamyMember1.Id; MemberNode endogamyMember2.Id ] ]

[<Fact>]
let ``layoutGraph emits only MemberNode leaves for an endogamous couple`` () =
    // layoutGraph must classify the endogamous partner by Kinship exactly as
    // enumerateHuwilpToRender does: both members are inside the rendered Wilp, so every
    // emitted leaf is a MemberNode and no PartnerNode appears. If the partner were
    // misclassified as a PartnerNode here, its key would diverge from the MemberNode
    // entity that spawns, and this set would contain that stray PartnerNode.
    let graph = createFamilyGraph endogamyPeople endogamyCouples []
    let _, rootBox = Scene.layoutGraph (WilpName "EN") graph

    let emittedKeys =
        rootBox
        |> LayoutBox.visit
            (fun _ (nodeKey: NodeKey) _ -> Seq.singleton nodeKey)
            (fun _ results -> Seq.concat results)
            LayoutVector<w>.Zero
        |> List.ofSeq

    Set.ofList emittedKeys
    =! Set.ofList [ MemberNode endogamyMember1.Id; MemberNode endogamyMember2.Id ]
