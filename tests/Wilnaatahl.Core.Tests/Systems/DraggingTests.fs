module Wilnaatahl.Tests.Systems.DraggingTests

open System
open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.Model
open Wilnaatahl.ViewModel.Vector
open Wilnaatahl.Entities
open Wilnaatahl.Traits.Events
open Wilnaatahl.Traits.PeopleTraits
open Wilnaatahl.Traits.SpaceTraits
open Wilnaatahl.Traits.ViewTraits
open Wilnaatahl.Systems.Dragging
open Wilnaatahl.Tests.EcsTestSupport

type Tests() =
    let ecs = new EcsWorld()
    let world = ecs.World

    /// Spawns a selected tree node at the given x, which is what a drag moves.
    let spawnSelectedNode x =
        world.Spawn(PersonRef.Val Person.Empty, Position.Val {| x = x; y = 0.0; z = 0.0 |}, Selected.Tag())

    /// Runs the frame that starts a drag. The frame's events are swept afterwards, as the real
    /// pipeline does, so the start is not still standing when the next frame runs.
    let startDrag () =
        world.Add DragStartEvent
        dragNodes world |> ignore
        cleanupEvents world |> ignore

    /// Runs a frame carrying a drag that has travelled the given distance along x since it began.
    let dragBy x =
        world.AddWith DragEvent {| x = x; y = 0.0; z = 0.0 |}
        dragNodes world |> ignore
        cleanupEvents world |> ignore

    let xOf node = (node |> get Position).Value.x

    [<Fact>]
    member _.``dragNodes with no events returns world unchanged``() =
        let entity = world.Spawn(Position.Val {| x = 5.0; y = 0.0; z = 0.0 |})

        dragNodes world |> ignore

        xOf entity =! 5.0

    [<Fact>]
    member _.``full drag flow moves selected entity position``() =
        let node = spawnSelectedNode 5.0

        startDrag ()
        dragBy 7.0

        (node |> get Position).Value =! Line3.pos 12.0 0.0 0.0

    [<Fact>]
    member _.``sequential drag events accumulate correctly``() =
        let node = spawnSelectedNode 5.0

        startDrag ()

        // The gesture reports the distance travelled since it began, not since the last frame.
        dragBy 3.0
        (node |> get Position).Value =! Line3.pos 8.0 0.0 0.0

        dragBy 7.0
        (node |> get Position).Value =! Line3.pos 12.0 0.0 0.0

    /// A release can arrive in a frame carrying no movement (the pointer paused before letting
    /// go), and that still ends the drag. Treating it as spurious would leave the participants
    /// marked forever, which the view layer reads as "a drag is still in progress".
    [<Fact>]
    member _.``drag end without movement in the same frame still ends the drag``() =
        spawnSelectedNode 0.0 |> ignore

        startDrag ()
        world.Query(With DragOrigin) |> Seq.length =! 1

        // The release frame carries no DragEvent, and startDrag already swept the frame before it.
        world.Add DragEndEvent
        dragNodes world |> ignore

        world.Query(With DragOrigin) |> Seq.length =! 0
        // The event is left in place so Undo/Redo can still finalize the drag.
        world.Has DragEndEvent =! true

    [<Fact>]
    member _.``spurious drag end without active drag removes DragEndEvent``() =
        world.Add DragEndEvent
        world.Has DragEndEvent =! true

        dragNodes world |> ignore

        world.Has DragEndEvent =! false

    [<Fact>]
    member _.``drag moves multiple selected entities``() =
        let node1 = spawnSelectedNode 0.0
        let node2 = spawnSelectedNode 10.0

        startDrag ()
        dragBy 3.0

        xOf node1 =! 3.0
        xOf node2 =! 13.0

    /// The gesture takes its participants from the selection once, when it starts. A node
    /// selected while the drag is running was never grabbed, so it must not start moving.
    [<Fact>]
    member _.``a node selected after the drag started does not move with it``() =
        let node1 = spawnSelectedNode 0.0
        let node2 = world.Spawn(Position.Val {| x = 10.0; y = 0.0; z = 0.0 |})

        startDrag ()

        node2 |> add Selected
        dragBy 3.0

        xOf node1 =! 3.0
        xOf node2 =! 10.0

    /// The mirror case: losing the selection cannot take a node out of a drag it is already part
    /// of, so nothing can strand a moving node partway.
    [<Fact>]
    member _.``a node deselected after the drag started keeps moving with it``() =
        let node1 = spawnSelectedNode 0.0
        let node2 = spawnSelectedNode 10.0

        startDrag ()

        node2 |> remove Selected
        dragBy 3.0

        xOf node1 =! 3.0
        xOf node2 =! 13.0

    /// Each participant is placed at where it started plus how far the gesture has travelled,
    /// rather than nudged by the change since the last frame, so it lands where the gesture says
    /// even if something else moved it in the meantime.
    [<Fact>]
    member _.``a participant moved by something else is put back where the gesture says``() =
        let node = spawnSelectedNode 0.0

        startDrag ()

        node |> setValue Position {| x = 50.0; y = 0.0; z = 0.0 |}
        dragBy 3.0

        xOf node =! 3.0

    interface IDisposable with
        member _.Dispose() = (ecs :> IDisposable).Dispose()
