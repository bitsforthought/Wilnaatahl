module Wilnaatahl.Tests.Entities.BoundingBoxTests

open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ViewModel.Vector
open Wilnaatahl.Entities
open Wilnaatahl.Traits.ConnectorTraits
open Wilnaatahl.Traits.SpaceTraits
open Wilnaatahl.Tests.EcsTestSupport

[<Fact>]
let ``spawn creates bounding box with Size Hidden and Connector`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    let boxId, _, _ = world |> BoundingBox.spawn {| x = 1.0; y = 2.0; z = 3.0 |}

    boxId |> has Hidden =! true
    boxId |> has Connector =! true

[<Fact>]
let ``spawn creates two corners with Position Hidden and Connector`` () =
    use ecs = new EcsWorld()
    let world = ecs.World

    let _, boxPosId, boundPosId = world |> BoundingBox.spawn zeroPosition

    boxPosId |> has Position =! true
    boxPosId |> has Hidden =! true
    boxPosId |> has Connector =! true
    boundPosId |> has Position =! true
    boundPosId |> has Hidden =! true
    boundPosId |> has Connector =! true

[<Fact>]
let ``spawn returns three distinct entity ids`` () =
    use ecs = new EcsWorld()
    let world = ecs.World

    let boxId, boxPosId, boundPosId = world |> BoundingBox.spawn zeroPosition

    boxId <>! boxPosId
    boxId <>! boundPosId
    boxPosId <>! boundPosId

[<Fact>]
let ``getCorners returns the two corner entities`` () =
    use ecs = new EcsWorld()
    let world = ecs.World

    let boxId, boxPosId, boundPosId = world |> BoundingBox.spawn zeroPosition
    let c1, c2 = boxId |> BoundingBox.getCorners world

    Set.ofList [ c1; c2 ] =! Set.ofList [ boxPosId; boundPosId ]

[<Fact>]
let ``updateCorners calls callback for each corner with correct IsBounds flag`` () =
    use ecs = new EcsWorld()
    let world = ecs.World

    let boxId, _, _ = world |> BoundingBox.spawn zeroPosition
    let mutable isBoundsValues = []

    boxId |> BoundingBox.updateCorners world AlwaysTrack (fun _ isBounds ->
        isBoundsValues <- isBounds :: isBoundsValues)

    // Should have exactly one true and one false
    isBoundsValues |> List.sort =! [ false; true ]

[<Fact>]
let ``updateCorners can modify corner positions`` () =
    use ecs = new EcsWorld()
    let world = ecs.World

    let boxId, boxPosId, boundPosId = world |> BoundingBox.spawn zeroPosition

    boxId |> BoundingBox.updateCorners world AlwaysTrack (fun pos isBounds ->
        if isBounds then
            pos.x <- 10.0; pos.y <- 10.0; pos.z <- 10.0
        else
            pos.x <- -1.0; pos.y <- -1.0; pos.z <- -1.0)

    // Check that positions were updated (we don't know which corner is which,
    // so check both possibilities)
    let pos1 = (boxPosId |> get Position).Value
    let pos2 = (boundPosId |> get Position).Value
    Set.ofList [ pos1; pos2 ]
    =! Set.ofList [ Line3.pos -1.0 -1.0 -1.0; Line3.pos 10.0 10.0 10.0 ]
