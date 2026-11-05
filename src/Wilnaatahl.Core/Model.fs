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

/// Identity of a rendered family-tree node. A member of the rendered Wilp has
/// exactly one node (`MemberNode`), keyed by their PersonId. An outside spouse
/// gets a distinct node for each marriage they appear in (`PartnerNode`), keyed
/// by their PersonId together with the CoupleId of that marriage, so a spouse
/// married to several Wilp members renders as several separate nodes.
type NodeKey =
    | MemberNode of PersonId
    | PartnerNode of PersonId * CoupleId

    /// The PersonId of the node, regardless of which case it is.
    member this.PersonId =
        match this with
        | MemberNode personId -> personId
        | PartnerNode(personId, _) -> personId

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

module Pdeek =

    /// The Pdeek's name in Gitxsan (Sim Algyax) orthography, carrying the
    /// underline diacritics and glottal marks that identify each clan. This is
    /// the human-facing spelling, distinct from the DU case label — the DU
    /// label is an ASCII identifier, never shown to users.
    let internal displayName pdeek =
        match pdeek with
        | LaxGibuu -> "Lax̱ Gibuu"
        | LaxSkiik -> "Lax̱ Skiik"
        | Ganeda -> "G̱aneda"
        | Giskaast -> "Gisḵ'aast"

/// A Wilp, identified by its Name and tagged with the Pdeek (Clan) it belongs to.
type Wilp = { Name: WilpName; Pdeek: Pdeek }

/// What we know about a Person's matrilineal House (Wilp) affiliation.
///   - `Wilp w`         — the specific Wilp is known.
///   - `UnknownWilp p`  — the Pdeek (Clan) is known but the specific Wilp is not.
///   - `NoneProvided`   — no structured affiliation is known, carrying an optional
///                        free-form note describing whatever is recorded instead.
type Kinship =
    | Wilp of Wilp
    | UnknownWilp of Pdeek
    | NoneProvided of string option

    /// The Pdeek (Clan) of this Kinship, if known. `Wilp w` exposes the Pdeek
    /// of the inner Wilp; `UnknownWilp p` exposes `p`; `NoneProvided` has no
    /// Pdeek to expose.
    member this.Pdeek: Pdeek option =
        match this with
        | Wilp w -> Some w.Pdeek
        | UnknownWilp p -> Some p
        | NoneProvided _ -> None

/// Stand-in for Gender until we decide how best to handle it.
#if FABLE_COMPILER
[<StringEnum>]
#endif
type NodeShape =
    | Sphere
    | Cube

/// A heritable Gitxsan Name. Its identity *is* its text: two holdings carrying
/// the same text denote the same Name. Held by value — there is no separate id.
#if FABLE_COMPILER
[<Erase>]
#endif
type Name =
    | Name of string

    member this.AsString =
        let (Name text) = this
        text

/// One Person's holding of one Name, carrying the life-order in which it was
/// given. The Name is held *by value* (identity is its text, so value and
/// reference coincide) — no id indirection. NameDate orders a person's Names by
/// recency (later is more recent) and is never displayed; NameOrder is the
/// fallback tiebreak, analogous to Person.BirthOrder. The holder is supplied by
/// context, so it is not a field here.
type NameHeld = { Name: Name; NameDate: DateOnly option; NameOrder: int option }

/// Everything we know about a person in the family tree.
type Person = {
    Id: PersonId
    ColonialName: string option
    Kinship: Kinship
    BirthWilp: Wilp option
    Shape: NodeShape
    BirthOrder: int
    DateOfBirth: DateOnly option
    DateOfDeath: DateOnly option
    DateOfBirthText: string option
    DateOfDeathText: string option
} with

    /// Used for situations where we need a prototypical instance of Person just to infer its type.
    static member Empty = {
        Id = PersonId 0
        ColonialName = None
        Kinship = NoneProvided None
        BirthWilp = None
        Shape = Sphere
        BirthOrder = 0
        DateOfBirth = None
        DateOfDeath = None
        DateOfBirthText = None
        DateOfDeathText = None
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
    /// Person with no descendants
    | Leaf of PersonId
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
        NamesHeldByPersonId: Map<int, NameHeld list>
    }

    /// The canonical most-recent-first order over a person's Name holdings: the
    /// head is the person's current (most recent) Name. Holdings fall into three
    /// groups (an earlier group is more recent and sorts first):
    ///
    ///   1. Holdings with a NameDate, ordered by that date descending (later is
    ///      more recent). Equal dates tiebreak by NameOrder descending — a present
    ///      order beats an absent one.
    ///   2. Holdings with no NameDate but a NameOrder, ordered by that order
    ///      descending (higher is more recent).
    ///   3. Holdings with neither a NameDate nor a NameOrder, ordered
    ///      alphabetically by Name text ascending.
    ///
    /// Group 1 wholly precedes group 2, which wholly precedes group 3. In every
    /// group the final tiebreak is alphabetical by Name text ascending. It defines
    /// a consistent, transitive total order.
    let private compareHoldingsMostRecentFirst (first: NameHeld) (second: NameHeld) =
        let groupOf (date: DateOnly option) nameOrder =
            match date, nameOrder with
            | Some _, _ -> 1
            | None, Some _ -> 2
            | None, None -> 3

        // Alphabetical by Name text, ascending — the final tiebreak in every group.
        // Ordinal comparison is locale-independent, so the .NET and Fable/browser
        // builds order identically (a culture-sensitive comparison could diverge
        // across runtimes).
        let byName () =
            String.CompareOrdinal(first.Name.AsString, second.Name.AsString)

        // NameOrder descending, a present order ahead of an absent one, then by name.
        let byOrderDescending () =
            match first.NameOrder, second.NameOrder with
            | Some firstOrder, Some secondOrder when firstOrder <> secondOrder -> compare secondOrder firstOrder
            | Some _, None -> -1
            | None, Some _ -> 1
            | _ -> byName ()

        match groupOf first.NameDate first.NameOrder, groupOf second.NameDate second.NameOrder with
        | firstGroup, secondGroup when firstGroup <> secondGroup -> compare firstGroup secondGroup
        | 1, _ ->
            match first.NameDate, second.NameDate with
            | Some earlierOrLater, Some other when earlierOrLater <> other -> compare other earlierOrLater
            | _ -> byOrderDescending ()
        | 2, _ -> byOrderDescending ()
        | _ -> byName ()

    let createFamilyGraph
        (people: seq<Person * CoupleId option>)
        (couples: seq<Couple>)
        (nameHoldings: seq<PersonId * NameHeld>)
        =
        let peopleList = people |> Seq.toList
        let couplesList = couples |> Seq.toList
        let nameHoldingsList = nameHoldings |> Seq.toList

        // Validate that every CoupleId in `couples` is unique. Map.ofList silently
        // overwrites duplicates, so we have to check before constructing the lookup.
        let duplicateCoupleId =
            couplesList
            |> List.countBy (fun c -> c.Id.AsInt)
            |> List.tryFind (fun (_, count) -> count > 1)

        match duplicateCoupleId with
        | Some(coupleId, count) ->
            failwith $"Duplicate CoupleId %d{coupleId} appears %d{count} times in the supplied couples."
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

        // Validate that every Name holding is held by a known person. Mirrors the
        // couple-member validation: fail-fast rather than silently dropping.
        for holderId, _ in nameHoldingsList do
            if not (Map.containsKey holderId.AsInt peopleMap) then
                failwith
                    $"Name holding references unknown PersonId %d{holderId.AsInt}; not present in the supplied people."

        // Group holdings by holder, each person's list sorted most-recent-first so
        // the head is the most recent Name and `namesHeldBy` needs no re-sorting.
        let namesHeldByPersonId =
            nameHoldingsList
            |> List.groupBy (fun (holderId, _) -> holderId.AsInt)
            |> List.map (fun (holderId, holdings) ->
                let sortedNames =
                    holdings |> List.map snd |> List.sortWith compareHoldingsMostRecentFirst

                holderId, sortedNames)
            |> Map.ofList

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
                | UnknownWilp _
                | NoneProvided _ -> None)
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
            NamesHeldByPersonId = namesHeldByPersonId
        }

    let allPeople graph =
        graph.People |> Map.values :> Person seq

    let couples graph : Couple seq =
        graph.Couples |> Map.values :> Couple seq

    let huwilp graph = graph.Huwilp

    /// Returns the forest of WilpTree roots for the given Wilp, or an empty sequence
    /// if the Wilp is not represented in this graph. Each root tree carries every
    /// person who participates in that Wilp's family lines, including from-outside
    /// partners that appear in Family nodes.
    let huwilpForest wilpName graph =
        graph.HuwilpForests |> Map.tryFind wilpName |> Option.defaultValue Seq.empty

    let findPerson (PersonId personId) graph = graph.People |> Map.find personId

    /// Returns the Names held by a person, already ordered most-recent-first (the
    /// head is the person's most recent Name). Returns the empty list for a person
    /// who holds no Names, or one absent from the graph.
    let namesHeldBy (PersonId personId) graph =
        Map.tryFind personId graph.NamesHeldByPersonId |> Option.defaultValue []

    /// Every Name holding in the graph as `(PersonId, NameHeld)` pairs. No ordering
    /// is guaranteed.
    let allNameHoldings graph : (PersonId * NameHeld) seq =
        graph.NamesHeldByPersonId
        |> Map.toSeq
        |> Seq.collect (fun (personId, holdings) -> holdings |> Seq.map (fun held -> PersonId personId, held))

    /// Returns the recorded children of a given Couple, in insertion order.
    /// Returns an empty list for a Couple that has no recorded children
    /// (i.e. nobody references its CoupleId via their parents pointer).
    let findChildrenOfCouple (couple: Couple) graph =
        Map.tryFind couple.Id.AsInt graph.ChildrenByCoupleId |> Option.defaultValue []

    /// Catamorphism for WilpTree forests, one per WilpName. Returns a sequence of 'R, one
    /// for each root in the forest. The visitLeaf, visitParent, visitPartner and visitFamily
    /// callbacks combine each visited node (or its constituent parts) into a result.
    /// visitPartner is passed both the partner's PersonId and the Couple that links the
    /// partner to the Wilp parent, so a partner can be identified per marriage.
    ///
    /// Sorting is delegated entirely to the caller through two predicates:
    ///   - compareTrees orders children within a single Couple's descendant group.
    ///   - compareGroups orders the groups themselves under a Wilp parent. Each group is
    ///     supplied as the Couple plus its already-sorted (per compareTrees) descendants.
    let visitWilpForest
        wilpName
        (visitLeaf: PersonId -> 'R)
        (visitParent: PersonId -> 'P)
        (visitPartner: PersonId -> Couple -> 'C)
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

                let visitGroup (partnerId, (couple, sortedTrees: WilpTree list)) =
                    visitPartner partnerId couple, sortedTrees |> List.map visit |> Seq.ofList

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

    let private wilp = { Name = WilpName "Wilp"; Pdeek = Giskaast }

    let private person id shape kinship birthOrder = {
        Person.Empty with
            Id = PersonId id
            Kinship = kinship
            Shape = shape
            BirthOrder = birthOrder
    }

    let private ts'iits' = person 0 Sphere (Wilp wilp) 0
    let private ye'e = person 1 Cube (UnknownWilp LaxSkiik) 0
    let private nixdaa = person 2 Sphere (Wilp wilp) 0
    let private nox = person 3 Sphere (Wilp wilp) 1
    let private biip = person 4 Cube (Wilp wilp) 2
    let private nigwoot = person 5 Cube (UnknownWilp Ganeda) 0
    let private gimxdii = person 6 Sphere (Wilp wilp) 0
    let private wek = person 7 Cube (Wilp wilp) 1
    let private wilaayasxw = person 8 Cube (UnknownWilp LaxGibuu) 0
    let private nii'y = person 9 Cube (Wilp wilp) 2

    let internal couples = [
        Couple.create (CoupleId 0) ts'iits'.Id ye'e.Id None
        Couple.create (CoupleId 1) nox.Id nigwoot.Id None
        Couple.create (CoupleId 2) gimxdii.Id wilaayasxw.Id None
    ]

    let private coupleIdByPair =
        couples
        |> List.map (fun couple ->
            let member1, member2 = couple.Members
            (member1.AsInt, member2.AsInt), couple.Id)
        |> Map.ofList

    let private parents (mother: Person) (father: Person) =
        let key = (min mother.Id.AsInt father.Id.AsInt, max mother.Id.AsInt father.Id.AsInt)
        Some(Map.find key coupleIdByPair)

    let internal peopleAndParents = [
        ts'iits', None
        ye'e, None
        nixdaa, parents ts'iits' ye'e
        nox, parents ts'iits' ye'e
        biip, parents ts'iits' ye'e
        nigwoot, None
        gimxdii, parents nox nigwoot
        wek, parents nox nigwoot
        wilaayasxw, None
        nii'y, parents nox nigwoot
    ]

    let private nameHeld text = { Name = Name text; NameDate = None; NameOrder = None }

    let internal nameHoldings: (PersonId * NameHeld) list = [
        ts'iits'.Id, nameHeld "Ts'iits'"
        ye'e.Id, nameHeld "Ye'e"
        nixdaa.Id, nameHeld "Nixdaa"
        nox.Id, nameHeld "Nox"
        biip.Id, nameHeld "Biip"
        nigwoot.Id, nameHeld "Nigwoot"
        gimxdii.Id, nameHeld "Gimxdii"
        wek.Id, nameHeld "Wek"
        wilaayasxw.Id, nameHeld "Wilaayasxw"
        nii'y.Id, nameHeld "'Nii'y"
    ]
