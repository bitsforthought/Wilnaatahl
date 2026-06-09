namespace Wilnaatahl.ViewModel

open Wilnaatahl.Model
open Wilnaatahl.Model.FamilyGraph
open Wilnaatahl.ViewModel.LayoutBox

/// Represents information needed to render a family member in the context of their Wilp.
type IFamilyMemberInfo =
    abstract Person: Person
    abstract RenderedInWilp: WilpName

/// This is a handy data structure for creating the connectors between members of an
/// immediate family.
type RenderedFamily<'T when 'T :> IFamilyMemberInfo> =
    // NOTE: Because this indirectly tracks EntityIds, it must be ephemeral and not persist between frames.
    // This type is only meant to be used while initializing connectors.
    {
        Parents: 'T * 'T
        Children: 'T list
    }

/// Defines constants that determine the default appearance (size, layout, movement rate) in the family tree scene.
module SceneConstants =

    /// Default horizontal spacing between nodes in the family tree layout, measured in world units.
    let defaultXSpacing = 1.95<w>

    /// Default vertical spacing between nodes in the family tree layout, measured in world units.
    let defaultYSpacing = 2.0<w>

    /// Default depth spacing between nodes in the family tree layout, measured in world units.
    let defaultZSpacing = 0.0<w>

    /// Distance between the two parallel lines that represent a parent-to-partner connector.
    let parentConnectorOffset = 0.2

    /// Vertical distance between a child node and the "elbow" junction where the child connector meets
    /// the "branch" connector for the family.
    let childToJunctionOffset = 0.65

    /// Default radius of a spherical person node in the family tree layout.
    let defaultSphereRadius = 0.4

    /// Default size of each edge of a cubic person node in the family tree layout.
    let defaultCubeSize = 0.6

    /// Rate at which animated entities move toward their target position. Lower values result in slower movement.
    let animationDampRate = 5.0

module Scene =
    open SceneConstants

    let private origin = LayoutVector<w>.Zero

    // Used to sort people for layout by comparing Date of Birth (DoB), or birth order if DoB is missing.
    let private comparePeople person1 person2 =
        match person1.DateOfBirth, person2.DateOfBirth with
        | Some dob1, Some dob2 ->
            // Fable doesn't appear to support DateOnly.CompareTo.
            if dob1 < dob2 then -1
            elif dob1 > dob2 then 1
            else 0

        // If either person is missing a birth date, fall back on birth order.
        | Some _, None
        | None, Some _
        | None, None -> person1.BirthOrder - person2.BirthOrder

    /// Compares two `(Couple, sorted descendants)` groups for layout placement under a
    /// shared Wilp parent. The result orders Couples left-to-right by their *effective
    /// date of union*, defined as:
    ///   - For a procreative Couple: the eldest recorded child's DateOfBirth.
    ///   - For a childless Couple: the Couple's DateOfUnion.
    ///
    /// In either case the effective date may be missing (a procreative Couple whose
    /// eldest child has no DateOfBirth, or a childless Couple with `DateOfUnion = None`).
    /// When both Couples have an effective date, they are compared by date with
    /// CoupleId as a tie-break on equal dates. When *either* Couple lacks an effective
    /// date, the comparison falls through immediately to CoupleId. This keeps the
    /// sort order deterministic for incomplete data without inventing a position for
    /// undated Couples relative to dated ones.
    ///
    /// `descendants` must already be sorted (by `compareTrees` in `visitWilpForest`) —
    /// the function looks at the head to identify the eldest child.
    let internal compareCouplesByEffectiveDate
        (graph: FamilyGraph)
        ((c1, descendants1): Couple * WilpTree list)
        ((c2, descendants2): Couple * WilpTree list)
        =
        let effectiveDate (couple: Couple) (descendants: WilpTree list) =
            match descendants with
            | [] -> couple.DateOfUnion
            | eldest :: _ -> (findPerson eldest.Root graph).DateOfBirth

        match effectiveDate c1 descendants1, effectiveDate c2 descendants2 with
        | Some d1, Some d2 ->
            match compare d1 d2 with
            | 0 -> compare c1.Id.AsInt c2.Id.AsInt
            | n -> n
        | _ -> compare c1.Id.AsInt c2.Id.AsInt

    let private leafBox<[<Measure>] 'u> (spacing: LayoutVector<'u>) height personId =
        let leafWidth = spacing.X
        let connectX = leafWidth / 2.0

        // When height = 0, this is effectively a 1-dimensional line in 3D space, but that's ok and
        // it makes the layout math easier. With any height, the Person shape centerpoint lies on the top edge.
        let offset = { X = connectX; Y = height; Z = 0.0<_> }
        let outerBoxSize = { X = leafWidth; Y = height; Z = 0.0<_> }
        createLeaf outerBoxSize connectX personId offset

    let private attachParentsToDescendants
        (spacing: LayoutVector<w>)
        (parentLeafBox: LayoutBox<u>)
        (partnerAndChildGroupBoxes: (LayoutBox<u> * LayoutBox<w> seq)[])
        : LayoutBox<w> =

        let familyCount = partnerAndChildGroupBoxes.Length

        let attachPartnerAndChildGroupBoxes i (partnerLeafBox: LayoutBox<u>, unattachedChildBoxes: LayoutBox<w> seq) =
            let partnerWidth = spacing.X * w2l
            let childBoxArray = unattachedChildBoxes |> Array.ofSeq

            let childGroupBox =
                if Array.isEmpty childBoxArray then
                    // Childless Couple: there are no descendants to lay out, so synthesize
                    // a zero-width placeholder that the partner can stack above. Per the
                    // childless-marriages spec (Q7), the partner ends up at the spacing
                    // slot adjacent to the Wilp parent.
                    createComposite { X = 0.0<l>; Y = 0.0<l>; Z = 0.0<l> } 0.0<l> {
                        TopLeftWidth = 0.0<l>
                        TopRightWidth = 0.0<l>
                        Followers = []
                    }
                else
                    childBoxArray |> attachHorizontally |> reframe Reframe.w2l

            let direction = if i < familyCount / 2 then -1.0 else 1.0

            // We need to account for the horizontal size of the partner box in these calculations.
            let partnerOffsetX =
                childGroupBox.ConnectX + direction * partnerWidth / 2.0
                - u2l * partnerLeafBox.Size.X / 2.0

            partnerLeafBox
            |> attachAbove childGroupBox { UseUpperConnectX = false; UpperOffset = partnerOffsetX }

        let descendantsBox =
            partnerAndChildGroupBoxes
            |> Array.mapi attachPartnerAndChildGroupBoxes
            |> attachHorizontally
            |> reframe Reframe.w2l

        let parentConnectXOffset =
            let partnerWidth = spacing.X * w2l

            if familyCount % 2 = 0 then 0.0<l> else -partnerWidth / 2.0

        // This could be negative if the descendants box is narrow (e.g. -- if there
        // is only one spouse and one child). In that case, the width of the resulting
        // box will be expanded accordingly by attachAbove.
        let parentLeftEdge =
            descendantsBox.ConnectX + parentConnectXOffset
            - u2l * parentLeafBox.Size.X / 2.0

        parentLeafBox
        |> attachAbove descendantsBox { UseUpperConnectX = true; UpperOffset = parentLeftEdge }

    /// Produces a map from WilpName to the people that will render along with that Wilp. Since people can
    /// play roles in different huwilp, the same Person may appear under different WilpName keys in the result.
    let enumerateHuwilpToRender familyGraph =
        // TODO: Extend to support multilple huwilp.
        let wilp = familyGraph |> huwilp |> Seq.head // ASSUMPTION: At least one Wilp is represented in the input data.
        let people = familyGraph |> allPeople
        [ (wilp, people) ] |> Map.ofList

    /// Produces a LayoutBox and initial position for the given Wilp. The LayoutBox, along with its nested boxes,
    /// specifies relative offsets that determine the position of every node in the Wilp family tree. The caller
    /// can process the returned LayoutBox using the LayoutBox.visit function.
    let layoutGraph wilp familyGraph =
        let spacing = { X = defaultXSpacing; Y = defaultYSpacing; Z = defaultZSpacing }
        let upperSpacing = spacing |> LayoutVector.reframe Reframe.w2u

        let visitLeaf = leafBox spacing 0.0<w>
        let visitParent = leafBox upperSpacing 0.0<u>
        let visitPartner = leafBox upperSpacing upperSpacing.Y
        let visitFamilies = attachParentsToDescendants spacing

        let compareTrees (t1: WilpTree) (t2: WilpTree) =
            comparePeople (familyGraph |> findPerson t1.Root) (familyGraph |> findPerson t2.Root)

        let compareGroups = compareCouplesByEffectiveDate familyGraph

        let rootBox =
            familyGraph
            |> visitWilpForest wilp visitLeaf visitParent visitPartner visitFamilies compareTrees compareGroups
            |> Array.ofSeq
            |> attachHorizontally

        let partnerWidth = spacing.X

        let rootOffset = {
            origin with
                X = -rootBox.ConnectX - partnerWidth / 2.0
                Y = -rootBox.Size.Y
        }

        rootOffset, rootBox

    /// Organizes the given sequence of family member info into a data structure useful in spawning
    /// connectors between immediate family members in the tree scene.
    let extractFamilies<'T when 'T :> IFamilyMemberInfo> familyGraph (nodes: 'T seq) =
        // TODO: Make this a recursive depth-first traversal so that leaf-most families are returned before root-most.
        // It needs to produce one tree traversal per Wilp and return the Wilp info with each Family.
        let nodesByPersonInWilp =
            nodes
            |> Seq.map (fun f -> (f.Person.Id.AsInt, f.RenderedInWilp), f)
            |> Map.ofSeq

        // Each Person appears at most once in a rendered Wilp, so this mapping is guaranteed to be unique.
        let personIdToNode wilp (personId: PersonId) =
            nodesByPersonInWilp |> Map.tryFind (personId.AsInt, wilp)

        let huwilpToRender = nodes |> Seq.map _.RenderedInWilp |> Seq.distinct

        seq {
            for couple in couples familyGraph do
                let m1, m2 = couple.Members
                let childrenOfCouple = findChildrenOfCouple couple familyGraph

                for wilp in huwilpToRender do
                    let getNode = personIdToNode wilp

                    match getNode m1, getNode m2 with
                    | Some parent1, Some parent2 ->
                        let renderedChildren = childrenOfCouple |> List.choose getNode
                        yield { Parents = parent1, parent2; Children = renderedChildren }
                    | _ -> () // Need both parents to be in this Wilp's render set.
        }
