module Wilnaatahl.Entities.Connectors

open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ECS.Relation
open Wilnaatahl.Model
open Wilnaatahl.ViewModel
open Wilnaatahl.ViewModel.SceneConstants
open Wilnaatahl.ViewModel.Vector
open Wilnaatahl.Traits.ConnectorTraits
open Wilnaatahl.Traits.PeopleTraits
open Wilnaatahl.Traits.SpaceTraits

/// Holds trait data for a node representing a person in the family tree.
type private FamilyMember(entity, position, person, wilp) =
    // NOTE: Because this tracks an EntityId, it must be ephemeral and not persist between frames.
    // This type is only meant to be used while initializing connectors.
    member _.Entity: EntityId = entity
    member _.Position: {| x: float; y: float; z: float |} = position

    interface IFamilyMemberInfo with
        member _.Person = person
        member _.RenderedInWilp = wilp

let private queryFamilies familyGraph (world: IWorld) =
    let createFamilyNode ((person, position), entity) =
        let maybeWilpId = entity |> targetFor RenderedIn

        match maybeWilpId with
        | Some wilpId ->
            match wilpId |> get Wilp with
            | Some wilp -> FamilyMember(entity, position, person, WilpName wilp.wilpName)
            | None -> failwith $"Found Wilp {wilpId} without a name."
        | None -> failwith $"Found tree node {entity} with no Wilp."

    world.QueryTraits(PersonRef, Position, With(RenderedIn.Wildcard())).ToSequence()
    |> Seq.map createFamilyNode
    |> Scene.extractFamilies familyGraph


let destroyAllConnectors (world: IWorld) =
    for entity in world.Query(With Connector) do
        entity |> destroy

    world

let spawnAllConnectors familyGraph (world: IWorld) =
    let families = world |> queryFamilies familyGraph

    // TODO: Extend this to support multilple huwilp, which will require tagging all Connectors
    // with the RendersIn relation.
    for family in families do
        // There are many components that go into a family's connectors. Let's create them
        // one-by-one:
        //
        // 1. A Hidden Line that Follows the two co-parent nodes.
        let parent1, parent2 = family.Parents

        // The first entity represents the line itself.
        let hiddenLineId =
            world
            |> Line3.spawnHidden parent1.Position parent2.Position
            |> Line3.snapTo world parent1.Entity parent2.Entity

        // 2. Two Lines, each of which Parallels the Hidden line, one with positive offset
        //    and one with negative offset.
        let topLineId = world |> Line3.spawn parent1.Position parent2.Position
        let bottomLineId = world |> Line3.spawn parent1.Position parent2.Position

        topLineId
        |> addWith (Parallels => hiddenLineId) {| offset = parentConnectorOffset / 2.0 |}

        bottomLineId
        |> addWith (Parallels => hiddenLineId) {| offset = -(parentConnectorOffset / 2.0) |}

        // 3. A Hidden entity with Position that Bisects the bottom line, which will stay below
        //    the other line even when the parent nodes have been dragged due to how the Parallels
        //    relation is implemented. Our definition of "bottom" is that it's the Line that Parallels the
        //    Hidden Line with a negative offset.
        let bisectingEntityId =
            world.Spawn(Position.Val zeroPosition, Hidden.Tag(), Connector.Tag())

        bisectingEntityId |> add (Bisects => bottomLineId)

        // 4. A Hidden Bounding Box that includes all child nodes.
        // The margins are chosen based on what looks good (see SceneConstants).
        let boundingBoxId, _, boxBoundId =
            world |> BoundingBox.spawn {| x = 0.0; y = childToJunctionOffset; z = 0 |}

        // We'll add the children to the bounding box later, so we can do all
        // child processing in one loop.

        // 5. A visible Elbow that FollowsX the Bisects Node and FollowsY the Bounding Box.
        let branchNodeId =
            world.Spawn(Position.Val zeroPosition, Elbow.Tag(), Connector.Tag())

        branchNodeId |> addWith (SnapToX => bisectingEntityId) {| x = 0.0 |}
        branchNodeId |> addWith (SnapToY => boxBoundId) {| y = 0.0 |}

        // 6. A visible Line that follows the Bisects Node and the Branch Node
        world
        |> Line3.spawnDynamic
        |> Line3.snapTo world bisectingEntityId branchNodeId
        |> ignore

        for child in family.Children do
            // Finish step 4 above by adding each child to the bounding box.
            boundingBoxId |> add (BoundingBoxOn => child.Entity)

            // 7. A visible Elbow for each child node that FollowsX the corresponding child node
            //    and FollowsY the Bounding Box.
            let junctionId =
                world.Spawn(Position.Val zeroPosition, Elbow.Tag(), Connector.Tag())

            junctionId |> addWith (SnapToX => child.Entity) {| x = 0.0 |}
            junctionId |> addWith (SnapToY => boxBoundId) {| y = 0.0 |}

            // 8. A visible Line for each child that follows the Branch Node and that child's Junction Node
            world
            |> Line3.spawnDynamic
            |> Line3.snapTo world junctionId branchNodeId
            |> ignore

            // 9. A visible Line for each child that follows that child's Junction Node and the Child Node itself.
            world
            |> Line3.spawnDynamic
            |> Line3.snapTo world junctionId child.Entity
            |> ignore

// TODO: Create a "family bounding box" and add all bounding boxes of the children to it.
// This includes the child bounding boxes (for leaves) and the family bounding boxes (when the
// children have descendants). Call this step 10.
//
// If the parents are both roots in the current Wilp, add their newly-created family bounding box
// to the corresponding Wilp box. Call this step 11.
