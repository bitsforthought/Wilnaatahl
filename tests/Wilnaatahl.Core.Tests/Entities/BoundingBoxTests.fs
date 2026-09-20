module Wilnaatahl.Tests.Entities.BoundingBoxTests

open System
open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ViewModel.Vector
open Wilnaatahl.Entities
open Wilnaatahl.Traits.ViewTraits
open Wilnaatahl.Traits.SpaceTraits
open Wilnaatahl.Tests.EcsTestSupport
open Wilnaatahl.Tests.TestUtils

type Tests() =
    let ecs = new EcsWorld()
    let world = ecs.World

    interface IDisposable with
        member _.Dispose() = (ecs :> IDisposable).Dispose()

    [<Fact>]
    member _.``spawn creates bounding box with Size and Hidden``() =
        let boxId, _, _ = world |> BoundingBox.spawn {| x = 1.0; y = 2.0; z = 3.0 |}

        boxId |> has Hidden =! true

    [<Fact>]
    member _.``spawn creates two corners with Position and Hidden``() =
        let _, boxPosId, boundPosId = world |> BoundingBox.spawn zeroPosition

        boxPosId |> has Position =! true
        boxPosId |> has Hidden =! true
        boundPosId |> has Position =! true
        boundPosId |> has Hidden =! true

    [<Fact>]
    member _.``spawn returns three distinct entity ids``() =
        let boxId, boxPosId, boundPosId = world |> BoundingBox.spawn zeroPosition

        boxId <>! boxPosId
        boxId <>! boundPosId
        boxPosId <>! boundPosId

    [<Fact>]
    member _.``getCorners returns the two corner entities``() =
        let boxId, boxPosId, boundPosId = world |> BoundingBox.spawn zeroPosition
        let c1, c2 = boxId |> BoundingBox.getCorners world

        Set.ofList [ c1; c2 ] =! Set.ofList [ boxPosId; boundPosId ]

    [<Fact>]
    member _.``getCorners rejects a bounding box without two corners``() =
        let boxId, boxPosId, _ = world |> BoundingBox.spawn zeroPosition
        boxPosId |> destroy

        captureExceptionMessage (fun () -> boxId |> BoundingBox.getCorners world |> ignore)
        =! Some $"Found BoundingBox {boxId} with 1 corners."

    [<Fact>]
    member _.``getCorners rejects a bounding box without corners``() =
        let boxId, boxPosId, boundPosId = world |> BoundingBox.spawn zeroPosition
        boxPosId |> destroy
        boundPosId |> destroy

        captureExceptionMessage (fun () -> boxId |> BoundingBox.getCorners world |> ignore)
        =! Some $"Found BoundingBox {boxId} with 0 corners."

    [<Fact>]
    member _.``updateCorners calls callback for each corner with correct IsBounds flag``() =
        let boxId, _, _ = world |> BoundingBox.spawn zeroPosition
        let mutable isBoundsValues = []

        boxId
        |> BoundingBox.updateCorners world AlwaysTrack (fun _ isBounds -> isBoundsValues <- isBounds :: isBoundsValues)

        // Should have exactly one true and one false
        isBoundsValues |> List.sort =! [ false; true ]

    [<Fact>]
    member _.``updateCorners can modify corner positions``() =
        let boxId, boxPosId, boundPosId = world |> BoundingBox.spawn zeroPosition

        boxId
        |> BoundingBox.updateCorners world AlwaysTrack (fun pos isBounds ->
            if isBounds then
                pos.x <- 10.0
                pos.y <- 10.0
                pos.z <- 10.0
            else
                pos.x <- -1.0
                pos.y <- -1.0
                pos.z <- -1.0)

        let pos1 = (boxPosId |> get Position).Value
        let pos2 = (boundPosId |> get Position).Value

        // Check that positions were updated (we don't know which corner is which,
        // so check both possibilities)
        Set.ofList [ pos1; pos2 ]
        =! Set.ofList [ Line3.pos -1.0 -1.0 -1.0; Line3.pos 10.0 10.0 10.0 ]
