namespace Wilnaatahl.Model

open System
#if FABLE_COMPILER
open Fable.Core
#endif

/// Represents a unique identifier for a person.
#if FABLE_COMPILER
[<Erase>]
#endif
type PersonId =
    | PersonId of int

    member this.AsInt =
        let (PersonId personId) = this
        personId

/// Represents a unique identifier for a Couple.
#if FABLE_COMPILER
[<Erase>]
#endif
type CoupleId =
    | CoupleId of int

    member this.AsInt =
        let (CoupleId coupleId) = this
        coupleId

/// Represents a Wilp's name; strongly typed to distinguish a Wilp name from other strings.
#if FABLE_COMPILER
[<Erase>]
#endif
type WilpName =
    | WilpName of string

    member this.AsString =
        let (WilpName wilp) = this
        wilp

/// Represents a P'deek (Clan). Each Wilp belongs to exactly one Pdeek. There are four Pdeek
/// in the Gitxsan nation: LaxGibuu (Wolf), LaxSkiik (Eagle), Ganeda (Frog), and Giskaast (Fireweed).
#if FABLE_COMPILER
[<StringEnum>]
#endif
type Pdeek =
    | LaxGibuu
    | LaxSkiik
    | Ganeda
    | Giskaast

/// A Wilp, identified by its Name and tagged with the Pdeek (Clan) it belongs to.
type Wilp = { Name: WilpName; Pdeek: Pdeek }

/// What we know about a Person's matrilineal House (Wilp) affiliation.
///   - `Wilp w`         — the specific Wilp is known.
///   - `UnknownWilp p`  — the Pdeek (Clan) is known but the specific Wilp is not.
///   - `NoneProvided`   — no affiliation information has been recorded.
type Kinship =
    | Wilp of Wilp
    | UnknownWilp of Pdeek
    | NoneProvided

    /// The Pdeek (Clan) of this Kinship, if known. `Wilp w` exposes the Pdeek
    /// of the inner Wilp; `UnknownWilp p` exposes `p`; `NoneProvided` has no
    /// Pdeek to expose.
    member this.Pdeek: Pdeek option =
        match this with
        | Wilp w -> Some w.Pdeek
        | UnknownWilp p -> Some p
        | NoneProvided -> None

/// Stand-in for Gender until we decide how best to handle it.
#if FABLE_COMPILER
[<StringEnum>]
#endif
type NodeShape =
    | Sphere
    | Cube

/// Everything we know about a person in the family tree.
type Person = {
    Id: PersonId
    Label: string option // TODO: Commit to schema for names (colonial vs. traditional)
    Kinship: Kinship
    Shape: NodeShape
    BirthOrder: int
    DateOfBirth: DateOnly option
    DateOfDeath: DateOnly option
} with

    /// Used for situations where we need a prototypical instance of Person just to infer its type.
    static member Empty = {
        Id = PersonId 0
        Label = None
        Kinship = NoneProvided
        Shape = Sphere
        BirthOrder = 0
        DateOfBirth = None
        DateOfDeath = None
    }

/// Represents a Couple of two people in a partnered relationship (married, common-law,
/// or otherwise unioned). Couples may or may not have produced recorded children.
///
/// Members is the unordered pair of people in the Couple. The record constructor is
/// private — use Couple.create, which canonicalizes Members (lower PersonId first) and
/// rejects equal members. This guarantees that two Couples built from the same pair in
/// either order are structurally equal and that no Couple can be in an invalid state.
/// The public read-only properties (Id, Members, DateOfUnion) expose the underlying
/// fields so callers can inspect a Couple with familiar dot-notation.
///
/// DateOfUnion is the optional date when the Couple was formed.
type Couple = private {
    Id_: CoupleId
    Members_: PersonId * PersonId
    DateOfUnion_: DateOnly option
} with

    member this.Id = this.Id_
    member this.Members = this.Members_
    member this.DateOfUnion = this.DateOfUnion_

module Couple =

    /// Constructs a Couple, canonicalizing Members so the two PersonIds appear in
    /// ascending order by their AsInt value. Throws if the two members are equal.
    let create id (member1: PersonId) (member2: PersonId) dateOfUnion : Couple =
        if member1 = member2 then
            invalidArg (nameof member2) $"A Couple's two members must differ; got PersonId %d{member1.AsInt} for both."

        let canonicalMembers =
            if member1.AsInt < member2.AsInt then
                (member1, member2)
            else
                (member2, member1)

        { Id_ = id; Members_ = canonicalMembers; DateOfUnion_ = dateOfUnion }

/// A family tree centered around one Wilp, including partners from outside that Wilp.
/// If a Wilp has mutiple roots, then it will have more than one such tree.
type WilpTree =
    | Leaf of PersonId // Person with no descendants
    | Family of Family

    /// Gets the root of a WilpTree, which is either the Wilp member parent of a
    /// descendant sub-tree, or a Wilp member leaf.
    member this.Root =
        match this with
        | Leaf personId -> personId
        | Family { WilpParent = personId; PartnersAndDescendants = _ } -> personId

/// A Wilp member with one or more partners (Couples) and their descendant sub-trees.
/// PartnersAndDescendants is keyed by the partner's PersonId. Each value is a pair of:
///   - the Couple linking the Wilp member to that partner, and
///   - the sub-tree for each child of that Couple (empty when the Couple has no children).
and Family = {
    WilpParent: PersonId
    PartnersAndDescendants: Map<PersonId, Couple * WilpTree seq>
}

module FamilyGraph =

    type FamilyGraph = private {
        People: Map<int, Person>
        Couples: Map<int, Couple>
        ChildrenByCoupleId: Map<int, PersonId list>
        Huwilp: Set<WilpName>
        HuwilpForests: Map<WilpName, WilpTree seq>
    }

    let createFamilyGraph (people: seq<Person * CoupleId option>) (couples: seq<Couple>) =
        let peopleList = people |> Seq.toList
        let couplesList = couples |> Seq.toList

        // Validate that every CoupleId in `couples` is unique. Map.ofList silently
        // overwrites duplicates, so we have to check before constructing the lookup.
        let duplicateCoupleId =
            couplesList
            |> List.countBy (fun c -> c.Id.AsInt)
            |> List.tryFind (fun (_, count) -> count > 1)

        match duplicateCoupleId with
        | Some(id, count) -> failwith $"Duplicate CoupleId %d{id} appears %d{count} times in the supplied couples."
        | None -> ()

        let peopleMap = peopleList |> List.map (fun (p, _) -> p.Id.AsInt, p) |> Map.ofList

        let couplesMap = couplesList |> List.map (fun c -> c.Id.AsInt, c) |> Map.ofList

        // Validate that every Person's referenced CoupleId exists in the couples set.
        for person, maybeCoupleId in peopleList do
            match maybeCoupleId with
            | Some cId when not (Map.containsKey cId.AsInt couplesMap) ->
                failwith
                    $"Person %d{person.Id.AsInt} references unknown CoupleId %d{cId.AsInt}; not present in the supplied couples."
            | _ -> ()

        // Validate that every Couple's Members reference PersonIds in the supplied people.
        for couple in couplesList do
            let m1, m2 = couple.Members

            for memberId in [ m1; m2 ] do
                if not (Map.containsKey memberId.AsInt peopleMap) then
                    failwith
                        $"Couple %d{couple.Id.AsInt} references unknown PersonId %d{memberId.AsInt}; not present in the supplied people."

        let childrenByCoupleId =
            peopleList
            |> List.choose (fun (p, maybeCoupleId) -> maybeCoupleId |> Option.map (fun cId -> cId.AsInt, p.Id))
            |> List.groupBy fst
            |> List.map (fun (cId, pairs) -> cId, pairs |> List.map snd)
            |> Map.ofList

        let huwilp =
            peopleList
            |> List.choose (fun (p, _) ->
                match p.Kinship with
                | Wilp w -> Some w.Name
                | _ -> None)
            |> Set.ofList

        // Helper to build WilpTree recursively. A Wilp member becomes a Family node if
        // they participate in any Couple — whether or not that Couple has produced
        // recorded children. Childless Couples surface as PartnersAndDescendants entries
        // with an empty descendants sequence.
        let rec buildWilpTree (person: Person) =
            let myCouples =
                couplesList
                |> List.filter (fun c ->
                    let m1, m2 = c.Members
                    m1 = person.Id || m2 = person.Id)

            if List.isEmpty myCouples then
                Leaf person.Id
            else
                let partnersAndDescendants =
                    myCouples
                    |> List.map (fun couple ->
                        let m1, m2 = couple.Members
                        let partnerId = if m1 = person.Id then m2 else m1

                        let descendantTrees =
                            Map.tryFind couple.Id.AsInt childrenByCoupleId
                            |> Option.defaultValue []
                            |> List.map (fun pid -> Map.find pid.AsInt peopleMap)
                            |> List.map buildWilpTree
                            |> Seq.ofList

                        partnerId, (couple, descendantTrees))
                    |> Map.ofList

                Family {
                    WilpParent = person.Id
                    PartnersAndDescendants = partnersAndDescendants
                }

        // For each Wilp, find root persons (with that Wilp and no parents).
        let huwilpForests =
            huwilp
            |> Seq.map (fun w ->
                let roots =
                    peopleList
                    |> List.choose (fun (p, maybeCoupleId) ->
                        match p.Kinship, maybeCoupleId with
                        | Wilp w', None when w'.Name = w -> Some p
                        | _ -> None)

                let trees = roots |> List.map buildWilpTree |> Seq.ofList
                w, trees)
            |> Map.ofSeq

        {
            People = peopleMap
            Couples = couplesMap
            ChildrenByCoupleId = childrenByCoupleId
            Huwilp = huwilp
            HuwilpForests = huwilpForests
        }

    let allPeople graph =
        graph.People |> Map.values :> Person seq

    let couples graph : Couple seq =
        graph.Couples |> Map.values :> Couple seq

    let huwilp graph = graph.Huwilp

    let findPerson (PersonId personId) graph = graph.People |> Map.find personId

    /// Returns the recorded children of a given Couple, in insertion order.
    /// Returns an empty list for a Couple that has no recorded children
    /// (i.e. nobody references its CoupleId via their parents pointer).
    let findChildrenOfCouple (couple: Couple) graph =
        Map.tryFind couple.Id.AsInt graph.ChildrenByCoupleId |> Option.defaultValue []

    /// Catamorphism for WilpTree forests, one per WilpName. Returns a sequence of 'R, one
    /// for each root in the forest. The visitLeaf, visitParent, visitPartner and visitFamily
    /// callbacks combine each visited node (or its constituent parts) into a result.
    ///
    /// Sorting is delegated entirely to the caller through two predicates:
    ///   - compareTrees orders children within a single Couple's descendant group.
    ///   - compareGroups orders the groups themselves under a Wilp parent. Each group is
    ///     supplied as the Couple plus its already-sorted (per compareTrees) descendants.
    let visitWilpForest
        wilpName
        (visitLeaf: PersonId -> 'R)
        (visitParent: PersonId -> 'P)
        (visitPartner: PersonId -> 'C)
        (visitFamily: 'P -> ('C * 'R seq)[] -> 'R)
        (compareTrees: WilpTree -> WilpTree -> int)
        (compareGroups: (Couple * WilpTree list) -> (Couple * WilpTree list) -> int)
        graph
        : seq<'R> =
        let rec visit tree =
            match tree with
            | Leaf personId -> visitLeaf personId
            | Family family ->
                // Sort each group's descendants once into a list. Materialization is
                // necessary because Seq.sortWith re-sorts on every enumeration of its
                // result, and the sorted descendants are enumerated twice below: once by
                // compareGroups and once by `visit`.
                let sortGroupDescendants _ ((couple: Couple), (trees: WilpTree seq)) =
                    couple, trees |> Seq.sortWith compareTrees |> List.ofSeq

                let visitGroup (partnerId, (_, sortedTrees: WilpTree list)) =
                    visitPartner partnerId, sortedTrees |> List.map visit |> Seq.ofList

                let sortedGroups =
                    family.PartnersAndDescendants
                    |> Map.map sortGroupDescendants
                    |> Map.toSeq
                    |> Seq.sortWith (fun (_, group1) (_, group2) -> compareGroups group1 group2)
                    |> Seq.map visitGroup
                    |> Array.ofSeq

                visitFamily (visitParent family.WilpParent) sortedGroups

        match graph.HuwilpForests |> Map.tryFind wilpName with
        | Some forest -> Seq.map visit forest
        | None -> Seq.empty

module Initial =

    // Wilp A is the primary (matriline) Wilp; B, C, and D are used only for in-marrying husbands.
    // The husbands' Kinship varies to exercise all three Kinship cases:
    //   - `Wilp w` for husbands whose specific Wilp is recorded (most husbands).
    //   - `UnknownWilp pdeek` for Henry Lee, whose Pdeek (Ganeda) is recorded but
    //     whose specific Wilp is not.
    //   - `NoneProvided` for husbands with no recorded affiliation.
    // The matrilineal invariant — every internal mother is Sphere/Wilp A, every internal
    // father is a Cube whose Kinship is not `Wilp A` — holds throughout this dataset.
    //
    // Each Wilp belongs to exactly one Pdeek (Clan). We assign all four Pdeek so the visualization
    // exercises the full color palette. Wilp A is Giskaast (red) so the bulk of the visible nodes
    // remain red, matching the prior visual impression of the test data.
    //
    // Two Couples in the seed exercise the childless-marriages path:
    //   - Susan + Frank: Susan is a Wilp leaf with no recorded children, so this Couple
    //     turns her from a Leaf into a Family with empty descendants.
    //   - Margaret + Roy: an extra childless Couple under Margaret (who already has three
    //     procreative Couples) exercises the layout sort that interleaves childless and
    //     procreative Couples by effective date of union.
    let private wilpA = Wilp { Name = WilpName "A"; Pdeek = Giskaast }
    let private wilpB = Wilp { Name = WilpName "B"; Pdeek = Ganeda }
    let private wilpC = Wilp { Name = WilpName "C"; Pdeek = LaxSkiik }
    let private wilpD = Wilp { Name = WilpName "D"; Pdeek = LaxGibuu }

    let private person id label shape kinship = {
        Person.Empty with
            Id = PersonId id
            Label = Some label
            Kinship = kinship
            Shape = shape
    }

    let private withDob (year, month, day) p = { p with DateOfBirth = Some(DateOnly(year, month, day)) }

    let private withBirthOrder n p = { p with BirthOrder = n }

    // ----- Forest root #1: Mary's matriline -----

    let private mary = person 0 "Mary Whitfield" Sphere wilpA
    let private george = person 1 "George Ashford" Cube wilpB

    // Gen 1 — six children of (Mary, George). DOB tie between Elizabeth and John exercises the
    // equal-DOB branch of the comparator; Susan has no DOB so her ordering falls back to BirthOrder.
    let private anne = person 2 "Anne Ashford" Sphere wilpA |> withDob (1925, 3, 10)
    let private james = person 3 "James Ashford" Cube wilpA |> withDob (1927, 7, 22)

    let private elizabeth =
        person 4 "Elizabeth Ashford" Sphere wilpA |> withDob (1929, 11, 2)

    let private john = person 5 "John Ashford" Cube wilpA |> withDob (1929, 11, 2)

    let private margaret =
        person 6 "Margaret Ashford" Sphere wilpA |> withDob (1932, 4, 17)

    let private susan = person 7 "Susan Ashford" Sphere wilpA |> withBirthOrder 5

    // Gen 1 husbands.
    let private henry = person 8 "Henry Lee" Cube (UnknownWilp Ganeda) // Pdeek-known, specific Wilp unknown; Anne's husband
    let private richard = person 9 "Richard Cromwell" Cube wilpD // Elizabeth spouse #1
    let private charles = person 10 "Charles Davenport" Cube wilpB // Elizabeth spouse #2
    let private frederick = person 11 "Frederick Easton" Cube wilpC // Margaret spouse #1
    let private albert = person 12 "Albert Fitzgerald" Cube NoneProvided // unaffiliated; Margaret spouse #2
    let private samuel = person 13 "Samuel Greenwood" Cube wilpD // Margaret spouse #3

    // Gen 2 children.
    let private catherine =
        person 14 "Catherine Lee" Sphere wilpA |> withDob (1950, 5, 1)

    let private robert = person 15 "Robert Cromwell" Cube wilpA |> withDob (1952, 4, 4)
    let private jane = person 16 "Jane Cromwell" Sphere wilpA |> withDob (1954, 8, 19)

    let private thomas =
        person 17 "Thomas Davenport" Cube wilpA |> withDob (1960, 2, 14)

    let private sarah = person 18 "Sarah Easton" Sphere wilpA |> withDob (1956, 3, 3)

    let private william =
        person 19 "William Fitzgerald" Cube wilpA |> withDob (1958, 6, 1)

    let private emily =
        person 20 "Emily Fitzgerald" Sphere wilpA |> withDob (1960, 9, 9)

    let private edward =
        person 21 "Edward Greenwood" Cube wilpA |> withDob (1962, 12, 12)

    // Gen 2 husbands.
    let private daniel = person 22 "Daniel Featherstonhaugh" Cube wilpC // Catherine's husband
    let private peter = person 23 "Peter Ng" Cube NoneProvided // unaffiliated; Jane's husband

    // Gen 3 children.
    let private michael =
        person 24 "Michael Featherstonhaugh" Cube wilpA |> withDob (1975, 6, 15)

    let private lucy =
        person 25 "Lucy Featherstonhaugh" Sphere wilpA |> withDob (1977, 9, 30)

    let private christopher =
        person 26 "Christopher Featherstonhaugh" Cube wilpA |> withDob (1980, 1, 8)

    let private rachel = person 27 "Rachel Ng" Sphere wilpA |> withDob (1982, 12, 25)

    // ----- Forest root #2: Helen's matriline (independent root, exercises multi-root forest) -----

    let private helen = person 28 "Helen Whitfield-Brook" Sphere wilpA
    let private walter = person 29 "Walter Yu" Cube wilpD
    let private grace = person 30 "Grace Yu" Sphere wilpA |> withBirthOrder 0
    let private benjamin = person 31 "Benjamin Yu" Cube wilpA |> withBirthOrder 1

    // Husbands for the childless Couples below. Both unaffiliated.
    let private frank = person 32 "Frank Hollister" Cube NoneProvided // Susan's husband (childless)
    let private roy = person 33 "Roy Pemberton" Cube NoneProvided // Margaret's fourth partner (childless)

    // Couples for the seed. CoupleIds are assigned sequentially so the seed remains
    // hand-readable; their numeric values do not carry meaning beyond uniqueness.
    let couples = [
        Couple.create (CoupleId 0) mary.Id george.Id None
        Couple.create (CoupleId 1) anne.Id henry.Id None
        Couple.create (CoupleId 2) elizabeth.Id richard.Id None
        Couple.create (CoupleId 3) elizabeth.Id charles.Id None
        Couple.create (CoupleId 4) margaret.Id frederick.Id None
        Couple.create (CoupleId 5) margaret.Id albert.Id None
        Couple.create (CoupleId 6) margaret.Id samuel.Id None
        Couple.create (CoupleId 7) catherine.Id daniel.Id None
        Couple.create (CoupleId 8) jane.Id peter.Id None
        Couple.create (CoupleId 9) helen.Id walter.Id None
        // Childless Couples introduced for the childless-marriages feature.
        Couple.create (CoupleId 10) susan.Id frank.Id (Some(DateOnly(1955, 6, 15)))
        Couple.create (CoupleId 11) margaret.Id roy.Id (Some(DateOnly(1965, 3, 1)))
    ]

    // Lookup table from a canonical (lower, higher) pair of PersonId.AsInt to CoupleId,
    // matching how Couple.create canonicalizes Members. Lets `parents` resolve a pair
    // to its CoupleId regardless of the order the caller writes the two parents.
    let private coupleIdByPair =
        couples
        |> List.map (fun c ->
            let m1, m2 = c.Members
            (m1.AsInt, m2.AsInt), c.Id)
        |> Map.ofList

    let private parents (mother: Person) (father: Person) =
        let key = (min mother.Id.AsInt father.Id.AsInt, max mother.Id.AsInt father.Id.AsInt)
        Some(Map.find key coupleIdByPair)

    let peopleAndParents = [
        // Forest #1 roots
        mary, None
        george, None

        // Gen 1: children of Mary + George
        anne, parents mary george
        james, parents mary george
        elizabeth, parents mary george
        john, parents mary george
        margaret, parents mary george
        susan, parents mary george

        // Gen 1 husbands (all roots)
        henry, None
        richard, None
        charles, None
        frederick, None
        albert, None
        samuel, None

        // Gen 2: Anne + Henry (1 child)
        catherine, parents anne henry

        // Gen 2: Elizabeth + Richard (2 children)
        robert, parents elizabeth richard
        jane, parents elizabeth richard

        // Gen 2: Elizabeth + Charles (1 child, second partner of Elizabeth)
        thomas, parents elizabeth charles

        // Gen 2: Margaret + Frederick (1 child, first of three partners of Margaret)
        sarah, parents margaret frederick

        // Gen 2: Margaret + Albert (2 children, middle partner of Margaret)
        william, parents margaret albert
        emily, parents margaret albert

        // Gen 2: Margaret + Samuel (1 child, third partner of Margaret)
        edward, parents margaret samuel

        // Gen 2 husbands (roots)
        daniel, None
        peter, None

        // Gen 3: Catherine + Daniel (3 children)
        michael, parents catherine daniel
        lucy, parents catherine daniel
        christopher, parents catherine daniel

        // Gen 3: Jane + Peter (1 child)
        rachel, parents jane peter

        // Forest #2: Helen's matriline
        helen, None
        walter, None
        grace, parents helen walter
        benjamin, parents helen walter

        // Childless-Couple husbands.
        frank, None
        roy, None
    ]
