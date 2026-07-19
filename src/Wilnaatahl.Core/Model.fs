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

    // Wilp A is the primary (matriline) Wilp. B, C, and D belong to in-marrying husbands,
    // and Wilp B doubles as one member's birth Wilp (Catherine's — see adoption below).
    // The husbands' Kinship varies to exercise every Kinship case:
    //   - `Wilp w` for husbands whose specific Wilp is recorded (most husbands).
    //   - `UnknownWilp pdeek` for Henry Lee, whose Pdeek (Ganeda) is recorded but
    //     whose specific Wilp is not.
    //   - `NoneProvided (Some note)` for Albert Fitzgerald, who married in with an
    //     unrecorded Wilp but a free-form note about it.
    //   - `NoneProvided None` for husbands with no recorded Kinship at all.
    // Both `NoneProvided` variants are seeded so the detail overlay's two Kinship rows
    // ("Kinship: <note>" and "Kinship: Unknown") are exercised on first paint.
    // The matrilineal invariant — every internal mother is Sphere/Wilp A, every internal
    // father is a Cube whose Kinship is not `Wilp A` — holds throughout this dataset.
    //
    // Each Wilp belongs to exactly one Pdeek (Clan). We assign all four Pdeek so the visualization
    // exercises the full color palette. Wilp A is Giskaast (red) so the bulk of the visible nodes
    // remain red, matching the prior visual impression of the test data.
    //
    // Adoption is seeded via Catherine Lee, whose current Kinship is Wilp A but whose
    // `BirthWilp` is Wilp B — the two differ, so the overlay's Birth Wilp / Birth Pdeeḵ
    // rows are exercised. (The node label draws only on the current Kinship and colonial/
    // held names, so an adoption surfaces in the detail overlay, not the label.)
    //
    // Gitxsan Names are seeded via `nameHoldings` (below): a few people hold several Names
    // (exercising most-recent selection and the overlay's "other names held" list), one Name
    // is handed down across generations (held by both Mary and her great-grandson Michael),
    // and James is recorded by his held Name alone, with no colonial name.
    //
    // Two Couples in the seed exercise the childless-marriages path:
    //   - Susan + Frank: Susan is a Wilp leaf with no recorded children, so this Couple
    //     turns her from a Leaf into a Family with empty descendants.
    //   - Margaret + Roy: an extra childless Couple under Margaret (who already has three
    //     procreative Couples) exercises the layout sort that interleaves childless and
    //     procreative Couples by effective date of union.
    // Victor's two Couples below (to Lucy and Rachel) are also childless; they exercise a
    // separate scenario — one from-outside spouse married to two different Wilp A members.

    // The named Wilp records. Each backs one `Kinship` `Wilp` case (below); `wilpRecordB`
    // additionally serves as Catherine's `BirthWilp` (a `Wilp option` — the record itself,
    // not the `Kinship` case), which is why the records are extracted as their own bindings.
    let private wilpRecordA = { Name = WilpName "A"; Pdeek = Giskaast }
    let private wilpRecordB = { Name = WilpName "B"; Pdeek = Ganeda }
    let private wilpRecordC = { Name = WilpName "C"; Pdeek = LaxSkiik }
    let private wilpRecordD = { Name = WilpName "D"; Pdeek = LaxGibuu }

    let private wilpA = Wilp wilpRecordA
    let private wilpB = Wilp wilpRecordB
    let private wilpC = Wilp wilpRecordC
    let private wilpD = Wilp wilpRecordD

    let private person id colonialName shape kinship = {
        Person.Empty with
            Id = PersonId id
            ColonialName = Some colonialName
            Kinship = kinship
            Shape = shape
    }

    /// Builds a seed Person recorded by their Gitxsan Name(s) alone — no colonial name.
    /// (The person reaches their Names only through the graph's holdings.)
    let private nameOnlyPerson id shape kinship = {
        Person.Empty with
            Id = PersonId id
            Kinship = kinship
            Shape = shape
    }

    let private withDob (year, month, day) p = { p with DateOfBirth = Some(DateOnly(year, month, day)) }

    let private withBirthOrder n p = { p with BirthOrder = n }

    /// Records the Wilp a person was born into, marking them as adopted when it differs
    /// from their current Kinship Wilp.
    let private withBirthWilp wilpRecord p = { p with BirthWilp = Some wilpRecord }

    // ----- Forest root #1: Mary's matriline -----

    let private mary = person 0 "Mary Whitfield" Sphere wilpA
    let private george = person 1 "George Ashford" Cube wilpB

    // Gen 1 — six children of (Mary, George). DOB tie between Elizabeth and John exercises the
    // equal-DOB branch of the comparator; Susan has no DOB so her ordering falls back to BirthOrder.
    let private anne = person 2 "Anne Ashford" Sphere wilpA |> withDob (1925, 3, 10)

    // James is recorded by his Gitxsan Name ("The Scholar", seeded below) alone — no colonial name.
    let private james = nameOnlyPerson 3 Cube wilpA |> withDob (1927, 7, 22)

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
    // `NoneProvided (Some note)`: married in with an unrecorded Wilp but a note about it.
    let private albert =
        person 12 "Albert Fitzgerald" Cube (NoneProvided(Some "Married in; Wilp not recorded")) // Margaret spouse #2

    let private samuel = person 13 "Samuel Greenwood" Cube wilpD // Margaret spouse #3

    // Gen 2 children.
    // Catherine was adopted into Wilp A (her current Kinship) from Wilp B (her BirthWilp).
    let private catherine =
        person 14 "Catherine Lee" Sphere wilpA
        |> withDob (1950, 5, 1)
        |> withBirthWilp wilpRecordB

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
    let private peter = person 23 "Peter Ng" Cube (NoneProvided None) // unaffiliated; Jane's husband

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
    let private frank = person 32 "Frank Hollister" Cube (NoneProvided None) // Susan's husband (childless)
    let private roy = person 33 "Roy Pemberton" Cube (NoneProvided None) // Margaret's fourth partner (childless)

    // A from-outside husband (Wilp B) married to two different Wilp A members — Lucy and
    // Rachel, who sit in separate branches of Mary's matriline. This exercises the case
    // where one outside spouse renders as a distinct node per marriage; without that, his
    // two spouse-bars would cross at a single shared node.
    let private victor = person 34 "Victor Ashby" Cube wilpB

    // Couples for the seed. CoupleIds are assigned sequentially so the seed remains
    // hand-readable; their numeric values do not carry meaning beyond uniqueness.
    let internal couples = [
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
        // Victor is a from-outside spouse married to two Wilp A members in different
        // branches, so he renders as a separate node per marriage (both childless).
        Couple.create (CoupleId 12) lucy.Id victor.Id None
        Couple.create (CoupleId 13) rachel.Id victor.Id None
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

    let internal peopleAndParents = [
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

        // From-outside spouse married to two Wilp A members (Lucy and Rachel).
        victor, None
    ]

    let private nameHeld text nameDate nameOrder = { Name = Name text; NameDate = nameDate; NameOrder = nameOrder }
    let private on year month day = Some(DateOnly(year, month, day))

    // Gitxsan Name holdings, passed to `createFamilyGraph` so the labels and detail
    // overlay have data to render on first paint. A few people hold multiple Names:
    //   - Mary holds three dated Names, so the most recent ("The Captain") heads her label
    //     and the overlay lists the other two ("The Steward", "Cook").
    //   - Margaret holds two undated Names, ordered by NameOrder alone — exercising the
    //     fallback ordering when no NameDate is present.
    // "The Captain" is handed down: Mary held it, and later her great-grandson Michael
    // received the same Name — the same text denotes the same heritable Name.
    // James holds a single Name and has no colonial name, so it is his only label line.
    // The Names are ordinary English nicknames — deliberately not attempts at real
    // Gitxsan names — used purely to exercise the rendering.
    let internal nameHoldings: (PersonId * NameHeld) list = [
        mary.Id, nameHeld "Cook" (on 1918 6 1) (Some 1)
        mary.Id, nameHeld "The Steward" (on 1950 2 1) (Some 2)
        mary.Id, nameHeld "The Captain" (on 1975 3 1) (Some 3)

        margaret.Id, nameHeld "Smith" None (Some 1)
        margaret.Id, nameHeld "The Mayor" None (Some 2)

        james.Id, nameHeld "The Scholar" (on 1945 9 1) (Some 1)

        catherine.Id, nameHeld "Doc" (on 1950 5 1) (Some 1)

        // Handed down from Mary — the same Name text, received by a later generation.
        michael.Id, nameHeld "The Captain" (on 1995 1 1) (Some 1)
    ]
