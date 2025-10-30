module Wilnaatahl.Systems.Movement

open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ECS.Relation
open Wilnaatahl.ECS.Tracking
open Wilnaatahl.Entities
open Wilnaatahl.ViewModel
open Wilnaatahl.ViewModel.Vector
open Wilnaatahl.Traits.ConnectorTraits
open Wilnaatahl.Traits.SpaceTraits

let private moveSnappedPoints (newPos: {| x: float; y: float; z: float |}) changedEntityId (world: IWorld) =
    world.QueryTraits(Position, SnapToX => changedEntityId).UpdateEachWith AlwaysTrack
    <| fun ((pos, distance), _) -> pos.x <- newPos.x + distance.x

    world.QueryTraits(Position, SnapToY => changedEntityId).UpdateEachWith AlwaysTrack
    <| fun ((pos, distance), _) -> pos.y <- newPos.y + distance.y

    world.QueryTraits(Position, SnapToZ => changedEntityId).UpdateEachWith AlwaysTrack
    <| fun ((pos, distance), _) -> pos.z <- newPos.z + distance.z

    world

let private moveBoundingBoxes changedEntityId (world: IWorld) =
    world.QueryTrait(Size, With(BoundingBoxOn => changedEntityId)).ForEach
    <| fun (margins, boxId) ->
        let marginsV = Vector3.FromPosition margins

        let updateBoxCorner (pos: MutableVector3) isBounds =
            // We can't adjust the whole box based on the movement of only one entity inside it,
            // because what if that entity is in the middle of a cluster of other entities?
            // Instead, we have to start by getting all the entities in the box and finding the
            // corners of the natural box that they form, and then adjust the actual box to match.

            // TODO: This is going to be a perf nightmare, especially when using BoundingBoxes for the entire Wilp
            let targetPoints =
                boxId
                |> targetsFor BoundingBoxOn
                |> Array.choose (get Position)
                |> Array.map Vector3.FromPosition

            let minPos = targetPoints |> Array.reduce min
            let maxBounds = targetPoints |> Array.reduce max

            let posV = pos.ToVector3()

            if isBounds then
                // Move the top-right-front corner of the box if needed.
                let maxBoundsWithMargin = maxBounds + marginsV
                let adjustedBounds = min (max posV maxBoundsWithMargin) maxBoundsWithMargin
                pos.x <- adjustedBounds.x
                pos.y <- adjustedBounds.y
                pos.z <- adjustedBounds.z
            else
                // Move the bottom-left-back corner of the box if needed.
                let minPosWithMargin = minPos - marginsV
                let adjustedPos = max (min posV minPosWithMargin) minPosWithMargin
                pos.x <- adjustedPos.x
                pos.y <- adjustedPos.y
                pos.z <- adjustedPos.z

        boxId |> BoundingBox.updateCorners world AlwaysTrack updateBoxCorner

    world

let private verticallyPerpendicularUnitVector v1 v2 =
    let dir = v2 - v1 |> normalize
    let up = Vector3.FromComponents(0.0, 1.0, 0.0)

    // Project up onto plane perpendicular to dir.
    let perpUp = up - up .* dir * dir

    // Normalize, with fallback for vertical lines.
    let len = perpUp |> length

    if len > nearZero then
        perpUp / len
    else
        // Line is vertical; choose a horizontal perpendicular.
        let alt =
            if abs dir.x < abs dir.z then
                Vector3.FromComponents(1.0, 0.0, 0.0)
            else
                Vector3.FromComponents(0.0, 0.0, 0.1)

        alt - alt .* dir * dir |> normalize

let private moveLineDependants changedEntityId (world: IWorld) =
    match changedEntityId |> targetFor EndpointOf with
    | Some lineId ->
        let v1, v2 = lineId |> Line3.getPositions world
        let midpoint = lerp v1 v2 0.5 // For Bisect

        world.QueryTrait(Position, With(Bisects => lineId)).UpdateEachWith AlwaysTrack
        <| fun (pos, _) ->
            pos.x <- midpoint.x
            pos.y <- midpoint.y
            pos.z <- midpoint.z

        // Lazily compute this once for Parallels because it's expensive.
        let n = lazy verticallyPerpendicularUnitVector v1 v2

        world.QueryTrait(Parallels => lineId).ForEach
        <| fun (parallels, parallelLineId) ->
            let distance = parallels.offset
            let offset = n.Value * abs distance

            let update trackedLinePos pos =
                let newPos =
                    if distance > 0.0 then
                        trackedLinePos + offset
                    else
                        trackedLinePos - offset

                pos.x <- newPos.x
                pos.y <- newPos.y
                pos.z <- newPos.z

            parallelLineId
            |> Line3.updateEndpoints world AlwaysTrack (update v1) (update v2)

    | None -> () // The changed entity isn't a line endpoint, so there's nothing to do.

// ASSUMPTION: The Movement system runs after anything else that changes position: Specifically,
// Animation and Dragging.
let move movementTracker (world: IWorld) =
    // There can be complex kinematic chains of entities following other entities around, so we need to
    // keep going until nothing moves. To avoid unbounded loops, we allow a max number of iterations.
    let maxIterations = 100
    let mutable i = 0
    let Changed = movementTracker

    // ASSUMPTION: Movement runs last among the systems that can change Position (Animation, Dragging, and Undo/Redo).
    // The change tracker passed in can be used to detect changes from those systems before it resets, and then can be
    // used to track changing Positions in the loop below.
    let mutable results =
        world.QueryTrait(Position, Changed <=> [| Position |]).ToSequence()

    while not (results |> Seq.isEmpty) && i < maxIterations do
        i <- i + 1

        for newPos, changedEntityId in results do
            world
            |> moveSnappedPoints newPos changedEntityId
            |> moveBoundingBoxes changedEntityId
            |> moveLineDependants changedEntityId

        results <- world.QueryTrait(Position, Changed <=> [| Position |]).ToSequence()

    if not (results |> Seq.isEmpty) then
        failwith "Error in Movement system: Unbounded kinematic chain found."

    world
