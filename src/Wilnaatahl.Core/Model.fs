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
    Wilp: Wilp option
    Shape: NodeShape
    BirthOrder: int
    DateOfBirth: DateOnly option
    DateOfDeath: DateOnly option
} with

    /// Used for situations where we need a prototypical instance of Person just to infer its type.
    static member Empty = {
        Id = PersonId 0
        Label = None
        Wilp = None
        Shape = Sphere
        BirthOrder = 0
        DateOfBirth = None
        DateOfDeath = None
    }

/// Represents a parent-child relationship. For every Person with recorded parents,
/// there will be two ParentChildRelationships, one for each parent.
type ParentChildRelationship = { Parent: PersonId; Child: PersonId }

/// Represents a pair of co-parents. If a child is missing one of the two recorded parents,
/// the missing parent is modeled as a Person with no extra non-identifying information.
type CoParentRelationship = { Mother: PersonId; Father: PersonId }

/// A family tree centered around one Wilp, including coparents from outside that Wilp.
/// If a Wilp has mutiple roots, then it will have more than one such tree.
type WilpTree =
    | Leaf of PersonId // Person with no descendants
    | Family of Family

    /// Gets the root of a WilpTree, which is either the Wilp member parent of a
    /// descendant sub-tree, or a Wilp member leaf.
    member this.Root =
        match this with
        | Leaf personId -> personId
        | Family { WilpParent = personId; CoParentsAndDescendants = _ } -> personId

/// A Wilp member with one or more coparents and their descendant sub-trees.
and Family = {
    WilpParent: PersonId
    CoParentsAndDescendants: Map<PersonId, WilpTree seq>
}

module FamilyGraph =

    type FamilyGraph = private {
        People: Map<int, Person>
        ParentChildRelationshipsByParent: Map<int, ParentChildRelationship list>
        CoParentRelationships: Set<CoParentRelationship>
        Huwilp: Set<WilpName>
        HuwilpForests: Map<WilpName, WilpTree seq>
    }

    let createFamilyGraph (peopleAndParents: seq<Person * CoParentRelationship option>) =
        let peopleMap =
            peopleAndParents |> Seq.map (fun (p, _) -> p.Id.AsInt, p) |> Map.ofSeq

        let coParents =
            peopleAndParents |> Seq.choose (fun (_, parents) -> parents) |> Set.ofSeq

        let parentChildMap =
            seq {
                for person, maybeParents in peopleAndParents do
                    match maybeParents with
                    | Some parents ->
                        yield { Parent = parents.Mother; Child = person.Id }
                        yield { Parent = parents.Father; Child = person.Id }
                    | None -> () // Person has no recorded parents so they are a "root" in the family multi-graph.
            }
            |> Seq.groupBy (fun rel -> rel.Parent.AsInt)
            |> Seq.map (fun (parent, children) -> parent, children |> List.ofSeq)
            |> Map.ofSeq

        let huwilp =
            peopleAndParents
            |> Seq.choose (fun (p, _) -> p.Wilp |> Option.map (fun w -> w.Name))
            |> Set.ofSeq

        // Helper to build WilpTree recursively using coparent relationships.
        let rec buildWilpTree person =
            // Find all coparent relationships where this person is a parent
            let coparentRels =
                coParents
                |> Set.filter (fun rel -> rel.Mother = person.Id || rel.Father = person.Id)
                |> Set.toList

            if List.isEmpty coparentRels then
                Leaf person.Id
            else
                // For each coparent, find all children for this pair, and build a forest (seq) of their WilpTrees
                let coParentsAndDescendants =
                    coparentRels
                    |> List.map (fun rel ->
                        let coparentId = if rel.Mother = person.Id then rel.Father else rel.Mother
                        // Find all children for this coparent pair
                        let children =
                            peopleAndParents
                            |> Seq.choose (fun (p, maybeParents) ->
                                match maybeParents with
                                | Some rel' when rel' = rel -> Some p
                                | _ -> None)
                            |> Seq.toList
                        // For each child, build their WilpTree
                        let descendantTrees = children |> List.map buildWilpTree |> Seq.ofList
                        // If no children, yield an empty sequence
                        coparentId, descendantTrees)
                    |> Map.ofList

                Family {
                    WilpParent = person.Id
                    CoParentsAndDescendants = coParentsAndDescendants
                }

        // For each Wilp, find root persons (with that Wilp and no parents).
        let huwilpForests =
            huwilp
            |> Seq.map (fun w ->
                let roots =
                    peopleAndParents
                    |> Seq.choose (fun (p, maybeParents) ->
                        match p.Wilp, maybeParents with
                        | Some w', None when w'.Name = w -> Some p
                        | _ -> None)

                let trees = roots |> Seq.map buildWilpTree
                w, trees)
            |> Map.ofSeq

        {
            People = peopleMap
            ParentChildRelationshipsByParent = parentChildMap
            CoParentRelationships = coParents
            Huwilp = huwilp
            HuwilpForests = huwilpForests
        }

    let allPeople graph =
        graph.People |> Map.values :> Person seq

    let coparents graph = graph.CoParentRelationships

    let huwilp graph = graph.Huwilp

    let findPerson (PersonId personId) graph = graph.People |> Map.find personId

    let findChildren (PersonId personId) graph =
        match graph.ParentChildRelationshipsByParent |> Map.tryFind personId with
        | Some rels -> rels |> List.map (fun r -> r.Child) |> Set.ofList
        | None -> Set.empty

    /// Catamorphism for WilpTree forests, one per WilpName. Returns a sequence of 'R, one for each root in the forest.
    /// Uses the given callbacks to process leaves, parents, co-parents, and families, and to sort each level of the tree.
    /// The visitLeaf callback takes a PersonId and returns a result value of type 'R. The visitParent and visitCoParent
    /// callbacks each take a PersonId and returns a result value of type 'P or 'C, respectively. The visitFamily callback
    /// takes the result for the Wilp parent and a sorted array of results for each co-parent and their descendants and
    /// combines it all into a single result value.
    let visitWilpForest
        wilpName
        (visitLeaf: PersonId -> 'R)
        (visitParent: PersonId -> 'P)
        (visitCoParent: PersonId -> 'C)
        (visitFamily: 'P -> ('C * ('R seq))[] -> 'R)
        (compare: Person -> Person -> int)
        graph
        : seq<'R> =
        let rec visit tree =
            match tree with
            | Leaf personId -> visitLeaf personId
            | Family family ->
                let compareByPersonId personId1 personId2 =
                    let person1, person2 = graph |> findPerson personId1, graph |> findPerson personId2
                    compare person1 person2

                let compareTreesByPersonId (tree1: WilpTree) (tree2: WilpTree) = compareByPersonId tree1.Root tree2.Root

                let sortAndVisitChildGroupForest _ (trees: WilpTree seq) =
                    trees |> Seq.sortWith compareTreesByPersonId |> Seq.map (fun t -> t, visit t)

                let sortAndProcessSortedChildGroups sortedChildGroups =
                    // We ignore the co-parent ID and results since we don't need them to sort.
                    let compareSortedChildGroups (_, childGroup1) (_, childGroup2) =
                        compareTreesByPersonId (childGroup1 |> Seq.head |> fst) (childGroup2 |> Seq.head |> fst)

                    let stripTreesAndVisitCoParent (coParentId, childGroups) =
                        visitCoParent coParentId, childGroups |> Seq.map snd

                    sortedChildGroups
                    |> Seq.sortWith compareSortedChildGroups
                    |> Seq.map stripTreesAndVisitCoParent

                // We sort each sub-tree and sort the sub-trees by first element before recursing downward.
                let sortedCoParentResultsPairs =
                    family.CoParentsAndDescendants
                    |> Map.map sortAndVisitChildGroupForest
                    |> Map.toSeq
                    |> sortAndProcessSortedChildGroups
                    |> Array.ofSeq

                let parentResult = visitParent family.WilpParent
                visitFamily parentResult sortedCoParentResultsPairs

        match graph.HuwilpForests |> Map.tryFind wilpName with
        | Some forest -> Seq.map visit forest
        | None -> Seq.empty

module Initial =

    // Wilp A is the primary (matriline) Wilp; B, C, and D are used only for in-marrying husbands.
    // Some husbands have Wilp = None to represent unknown / unaffiliated affiliation. The matrilineal
    // invariant — every internal mother is Sphere/Wilp A, every internal father is a Cube whose Wilp
    // is non-A or None — holds throughout this dataset.
    //
    // Each Wilp belongs to exactly one Pdeek (Clan). We assign all four Pdeek so the visualization
    // exercises the full color palette. Wilp A is Giskaast (red) so the bulk of the visible nodes
    // remain red, matching the prior visual impression of the test data.
    let private wilpA = Some { Name = WilpName "A"; Pdeek = Giskaast }
    let private wilpB = Some { Name = WilpName "B"; Pdeek = Ganeda }
    let private wilpC = Some { Name = WilpName "C"; Pdeek = LaxSkiik }
    let private wilpD = Some { Name = WilpName "D"; Pdeek = LaxGibuu }

    let private person id label shape wilp = {
        Person.Empty with
            Id = PersonId id
            Label = Some label
            Wilp = wilp
            Shape = shape
    }

    let private withDob (year, month, day) p = { p with DateOfBirth = Some(DateOnly(year, month, day)) }

    let private withBirthOrder n p = { p with BirthOrder = n }

    let private parents (mother: Person) (father: Person) =
        Some { Mother = mother.Id; Father = father.Id }

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
    let private henry = person 8 "Henry Lee" Cube None // unaffiliated; Anne's husband
    let private richard = person 9 "Richard Cromwell" Cube wilpD // Elizabeth spouse #1
    let private charles = person 10 "Charles Davenport" Cube wilpB // Elizabeth spouse #2
    let private frederick = person 11 "Frederick Easton" Cube wilpC // Margaret spouse #1
    let private albert = person 12 "Albert Fitzgerald" Cube None // unaffiliated; Margaret spouse #2
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
    let private peter = person 23 "Peter Ng" Cube None // unaffiliated; Jane's husband

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

        // Gen 2: Elizabeth + Charles (1 child, second coparent of Elizabeth)
        thomas, parents elizabeth charles

        // Gen 2: Margaret + Frederick (1 child, first of three coparents of Margaret)
        sarah, parents margaret frederick

        // Gen 2: Margaret + Albert (2 children, middle coparent of Margaret)
        william, parents margaret albert
        emily, parents margaret albert

        // Gen 2: Margaret + Samuel (1 child, third coparent of Margaret)
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
    ]
