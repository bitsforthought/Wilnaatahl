module Wilnaatahl.Tests.Entities.LineTests

open System
open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ECS.Relation
open Wilnaatahl.ViewModel
open Wilnaatahl.ViewModel.Vector
open Wilnaatahl.Entities
open Wilnaatahl.Traits.ConnectorTraits
open Wilnaatahl.Traits.ViewTraits
open Wilnaatahl.Traits.SpaceTraits
open Wilnaatahl.Tests.EcsTestSupport
open Wilnaatahl.Tests.TestUtils

type Tests() =
    let ecs = new EcsWorld()
    let world = ecs.World

    let spawnOriginLine () =
        world |> Line3.spawn zeroPosition zeroPosition

    interface IDisposable with
        member _.Dispose() = (ecs :> IDisposable).Dispose()

    [<Fact>]
    member _.``spawn creates line entity with the Line trait``() =
        let lineId = spawnOriginLine ()

        lineId |> has Line =! true

    [<Fact>]
    member _.``spawn creates lines that are not Hidden by default``() =
        let lineId = spawnOriginLine ()

        lineId |> has Hidden =! false

    [<Fact>]
    member _.``spawn creates two endpoints with Position and Hidden``() =
        let lineId = spawnOriginLine ()
        let ep1, ep2 = lineId |> Line3.getEndpoints world

        ep1 |> has Position =! true
        ep1 |> has Hidden =! true
        ep2 |> has Position =! true
        ep2 |> has Hidden =! true

    [<Fact>]
    member _.``spawn sets endpoint positions correctly``() =
        let lineId = world |> Line3.spawn (Line3.pos 1.0 2.0 3.0) (Line3.pos 4.0 5.0 6.0)
        let v1, v2 = lineId |> Line3.getPositions world

        v1 =! Vector3.FromComponents(1.0, 2.0, 3.0)
        v2 =! Vector3.FromComponents(4.0, 5.0, 6.0)

    [<Fact>]
    member _.``spawnAtOrigin creates line at origin``() =
        let lineId = world |> Line3.spawnAtOrigin
        let v1, v2 = lineId |> Line3.getPositions world

        v1 =! Vector3.FromComponents(0.0, 0.0, 0.0)
        v2 =! Vector3.FromComponents(0.0, 0.0, 0.0)

    [<Fact>]
    member _.``spawnHidden creates line with Hidden trait``() =
        let lineId = world |> Line3.spawnHidden zeroPosition zeroPosition

        lineId |> has Hidden =! true
        lineId |> has Line =! true

    [<Fact>]
    member _.``getEndpoints returns exactly two endpoints``() =
        let lineId = spawnOriginLine ()
        let ep1, ep2 = lineId |> Line3.getEndpoints world

        ep1 <>! ep2

    [<Fact>]
    member _.``getEndpoints rejects a line without two endpoints``() =
        let lineId = world.Spawn(Line.Tag())

        captureExceptionMessage (fun () -> lineId |> Line3.getEndpoints world |> ignore)
        =! Some $"Found Line {lineId} with 0 endpoints."

    [<Fact>]
    member _.``getEndpoints rejects a line with only one endpoint``() =
        let lineId = spawnOriginLine ()
        let firstEndpointId, _ = lineId |> Line3.getEndpoints world
        firstEndpointId |> destroy

        captureExceptionMessage (fun () -> lineId |> Line3.getEndpoints world |> ignore)
        =! Some $"Found Line {lineId} with 1 endpoints."

    [<Theory>]
    [<InlineData(true, false)>]
    [<InlineData(false, true)>]
    [<InlineData(false, false)>]
    member _.``getPositions rejects endpoints without positions`` firstEndpointHasPosition secondEndpointHasPosition =
        let lineId = spawnOriginLine ()
        let firstEndpointId, secondEndpointId = lineId |> Line3.getEndpoints world

        if not firstEndpointHasPosition then
            firstEndpointId |> remove Position

        if not secondEndpointHasPosition then
            secondEndpointId |> remove Position

        captureExceptionMessage (fun () -> lineId |> Line3.getPositions world |> ignore)
        =! Some $"Found Line {lineId} with endpoint(s) that have no Position."

    [<Fact>]
    member _.``snapToWithOffset adds SnapTo relations``() =
        let target = world.Spawn(Position.Val zeroPosition)
        let subject = world.Spawn(Position.Val zeroPosition)

        Line3.snapToWithOffset target (1.0, 2.0, 3.0) subject

        subject |> targetFor SnapToX =! Some target
        subject |> targetFor SnapToY =! Some target
        subject |> targetFor SnapToZ =! Some target
        (subject |> getRelationValue SnapToX target).Value =! {| x = 1.0 |}
        (subject |> getRelationValue SnapToY target).Value =! {| y = 2.0 |}
        (subject |> getRelationValue SnapToZ target).Value =! {| z = 3.0 |}

    [<Fact>]
    member _.``snapTo snaps both endpoints to targets with zero offset``() =
        let target1 = world.Spawn(Position.Val(Line3.pos 10.0 0.0 0.0))
        let target2 = world.Spawn(Position.Val(Line3.pos 20.0 0.0 0.0))
        let lineId = spawnOriginLine ()

        lineId |> Line3.snapTo world target1 target2 |> ignore

        let ep1, ep2 = lineId |> Line3.getEndpoints world
        ep1 |> targetFor SnapToX =! Some target1
        ep2 |> targetFor SnapToX =! Some target2

    [<Fact>]
    member _.``updateEndpoints calls functions on each endpoint position``() =
        let lineId = world |> Line3.spawn (Line3.pos 1.0 0.0 0.0) (Line3.pos 2.0 0.0 0.0)
        let mutable calls = 0

        lineId
        |> Line3.updateEndpoints
            world
            AlwaysTrack
            (fun pos ->
                pos.x <- 100.0
                calls <- calls + 1)
            (fun pos ->
                pos.x <- 200.0
                calls <- calls + 1)

        calls =! 2
        let v1, v2 = lineId |> Line3.getPositions world
        // One endpoint should be 100, the other 200 (order depends on internal iteration)
        Set.ofList [ v1.x; v2.x ] =! Set.ofList [ 100.0; 200.0 ]
