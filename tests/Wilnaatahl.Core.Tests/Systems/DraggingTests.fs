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
open Wilnaatahl.Traits.History
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

    /// Runs the frame that releases the drag. The frame's events are left standing so a caller
    /// can inspect what the release recorded.
    let endDrag () =
        world.Add DragEndEvent
        dragNodes world |> ignore

    let xOf node = (node |> get Position).Value.x

    [<Fact>]
    member _.``dragNodes with no events returns world unchanged``() =
        let entity = world.Spawn(Position.Val {| x = 5.0; y = 0.0; z = 0.0 |})

        dragNodes world |> ignore

        xOf entity =! 5.0

    [<Fact>]
    member _.``Full drag flow moves selected entity position``() =
        let node = spawnSelectedNode 5.0

        startDrag ()
        dragBy 7.0

        (node |> get Position).Value =! Line3.pos 12.0 0.0 0.0

    [<Fact>]
    member _.``Sequential drag events accumulate correctly``() =
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
    member _.``Drag end without movement in the same frame still ends the drag``() =
        spawnSelectedNode 0.0 |> ignore

        startDrag ()
        world.Query(With DragOrigin) |> Seq.length =! 1

        endDrag ()

        world.Query(With DragOrigin) |> Seq.length =! 0

    [<Fact>]
    member _.``Drag moves multiple selected entities``() =
        let node1 = spawnSelectedNode 0.0
        let node2 = spawnSelectedNode 10.0

        startDrag ()
        dragBy 3.0

        xOf node1 =! 3.0
        xOf node2 =! 13.0

    /// The gesture takes its participants from the selection once, when it starts. A node
    /// selected while the drag is running was never grabbed, so it must not start moving.
    [<Fact>]
    member _.``A node selected after the drag started does not move with it``() =
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
    member _.``A node deselected after the drag started keeps moving with it``() =
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
    member _.``A participant moved by something else is put back where the gesture says``() =
        let node = spawnSelectedNode 0.0

        startDrag ()

        node |> setValue Position {| x = 50.0; y = 0.0; z = 0.0 |}
        dragBy 3.0

        xOf node =! 3.0

    /// A drag is the change; the history entry it leaves behind is what replays that change. The
    /// origin is what the node had when it was grabbed, so undo returns it to where the drag
    /// began rather than to anywhere it passed through on the way.
    [<Fact>]
    member _.``A drag that moved a node records where it started and where it ended``() =
        let node = spawnSelectedNode 5.0

        startDrag ()
        dragBy 7.0
        endDrag ()

        world |> committedCommands |> List.map _.Moves
        =! [ [ moveAlongX node 5.0 12.0 ] ]

    /// A grab with no movement changes nothing, and neither does one that wanders and comes back.
    /// Recording either would put an entry on the undo stack that undoes nothing and, worse,
    /// throw away the redo history in exchange.
    [<Fact>]
    member _.``A drag that ended where it began records nothing``() =
        spawnSelectedNode 5.0 |> ignore

        startDrag ()
        dragBy 7.0
        dragBy 0.0
        endDrag ()

        world |> committedCommands =! []

    /// Grabbing with nothing selected takes hold of nothing, so the release has nothing to
    /// record. The gesture still runs to completion; it simply moves and commits nothing.
    [<Fact>]
    member _.``A drag with nothing selected records nothing``() =
        world.Spawn(Position.Val {| x = 5.0; y = 0.0; z = 0.0 |}) |> ignore

        startDrag ()
        dragBy 7.0
        endDrag ()

        world |> committedCommands =! []

    /// Dragging a node that is still animating commits nothing: the drag does not change where the
    /// node is headed, so there is nothing to take back. Its position is only borrowed for the
    /// gesture. The line is drawn where the system can see it — whether the node still carries a
    /// TargetPosition when its origin is captured.
    [<Fact>]
    member _.``A drag of a node still animating when its origin is captured records nothing``() =
        let node = spawnSelectedNode 5.0
        node |> addWith TargetPosition {| x = 100.0; y = 0.0; z = 0.0 |}

        startDrag ()
        dragBy 7.0
        endDrag ()

        world |> committedCommands =! []

    /// A multi-select can hold both a settled node and an animating one. The gesture moves both,
    /// but only the settled one has a change worth taking back.
    [<Fact>]
    member _.``A drag records only the participants that had a change to take back``() =
        let settled = spawnSelectedNode 5.0
        let animating = spawnSelectedNode 20.0
        animating |> addWith TargetPosition {| x = 100.0; y = 0.0; z = 0.0 |}

        startDrag ()
        dragBy 7.0
        endDrag ()

        world |> committedCommands |> List.map _.Moves
        =! [ [ moveAlongX settled 5.0 12.0 ] ]

    /// A multi-select drag is one change covering every node it moved, so undo takes the whole
    /// thing back at once. Recording only the first participant would leave the rest stranded.
    /// The moves are compared as a set: a command's moves are applied independently, so the order
    /// the query happened to yield them in carries no meaning.
    [<Fact>]
    member _.``A drag of several nodes records them all in one command``() =
        let left = spawnSelectedNode 5.0
        let right = spawnSelectedNode 20.0

        startDrag ()
        dragBy 7.0
        endDrag ()

        let committed = world |> committedCommands |> List.exactlyOne

        committed.Moves |> Set.ofList
        =! Set.ofList [ moveAlongX left 5.0 12.0; moveAlongX right 20.0 27.0 ]

    interface IDisposable with
        member _.Dispose() = (ecs :> IDisposable).Dispose()
