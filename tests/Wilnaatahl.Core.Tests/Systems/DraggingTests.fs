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

    /// Runs the frame that starts a drag. The frame's events are cleared afterwards, as the real
    /// pipeline does, so the start event is gone before the next frame runs.
    let startDrag () =
        handleDragStart world
        dragNodes world |> ignore
        cleanupEvents world |> ignore

    /// Runs a frame carrying a drag that has travelled the given distance along x since it began.
    let dragBy x =
        handleDrag world x 0.0 0.0
        dragNodes world |> ignore
        cleanupEvents world |> ignore

    /// Runs the frame that releases the drag. The frame's commands are left in place so that a
    /// caller can check what the release committed.
    let endDrag () =
        handleDragEnd world
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

        // A Dragged event carries the distance travelled since the drag began, not since the
        // last frame.
        dragBy 3.0
        (node |> get Position).Value =! Line3.pos 8.0 0.0 0.0

        dragBy 7.0
        (node |> get Position).Value =! Line3.pos 12.0 0.0 0.0

    /// A drag moves a node on all three axes at once, and the positions it records keep all three.
    /// Every other drag test here moves along x only, with the node at y = 0 and z = 0, so a
    /// mistake that lost y or z would pass them.
    [<Fact>]
    member _.``A drag moves and records a node on all three axes``() =
        let start = Line3.pos 5.0 -2.0 7.0

        let node =
            world.Spawn(PersonRef.Val Person.Empty, Position.Val start, Selected.Tag())

        startDrag ()

        handleDrag world 3.0 11.0 -4.0
        dragNodes world |> ignore
        cleanupEvents world |> ignore

        endDrag ()

        let finish = Line3.pos 8.0 9.0 3.0
        (node |> get Position).Value =! finish

        world |> committedCommands |> List.map _.Moves
        =! [ [ { Entity = node; Before = start; After = finish } ] ]

    /// A whole drag can arrive in one frame, when the pointer is pressed and released between two
    /// frames. Each event is applied in turn, so the node ends up at the position the drag moved
    /// it to.
    [<Fact>]
    member _.``dragNodes applies a whole drag delivered in one frame``() =
        let node = spawnSelectedNode 5.0

        handleDragStart world
        handleDrag world 3.0 0.0 0.0
        handleDragEnd world
        dragNodes world |> ignore

        xOf node =! 8.0
        world.Has DragInFlight =! false
        world |> committedCommands |> List.length =! 1

    /// Input is applied in the order it arrived. A release raised before a drag started belongs
    /// to no drag, so it must not end the drag that starts after it.
    [<Fact>]
    member _.``dragNodes leaves a drag running when a release arrived before it``() =
        let node = spawnSelectedNode 5.0

        handleDragEnd world
        handleDragStart world
        dragNodes world |> ignore

        world.Has DragInFlight =! true
        node |> has DragOrigin =! true

    /// A release can arrive in a frame that carries no movement (the pointer paused before letting
    /// go), and it still ends the drag. Ignoring it would leave DragOrigin on the nodes forever,
    /// which the view layer reads as "a drag is still in progress".
    [<Fact>]
    member _.``Drag end without movement in the same frame still ends the drag``() =
        spawnSelectedNode 0.0 |> ignore

        startDrag ()
        world.Query(With DragOrigin) |> Seq.length =! 1

        endDrag ()

        world.Query(With DragOrigin) |> Seq.length =! 0

    /// Callers ask whether a drag is running far more often than they ask which nodes it moves,
    /// so the Dragging system records that once as a world tag instead of making every caller
    /// search for a node carrying DragOrigin.
    [<Fact>]
    member _.``A drag signals that it is in flight for as long as it holds its participants``() =
        spawnSelectedNode 0.0 |> ignore
        world.Has DragInFlight =! false

        startDrag ()
        world.Has DragInFlight =! true

        dragBy 3.0
        world.Has DragInFlight =! true

        endDrag ()
        world.Has DragInFlight =! false

    /// A drag that starts with nothing selected has no nodes to move and commits nothing. Adding
    /// DragInFlight for it would refuse every click until the user released the pointer, and no
    /// node could move in return.
    [<Fact>]
    member _.``A drag that grabbed nothing signals no drag in flight``() =
        world.Spawn(Position.Val {| x = 5.0; y = 0.0; z = 0.0 |}) |> ignore

        startDrag ()

        world.Has DragInFlight =! false

    [<Fact>]
    member _.``Drag moves multiple selected entities``() =
        let node1 = spawnSelectedNode 0.0
        let node2 = spawnSelectedNode 10.0

        startDrag ()
        dragBy 3.0

        xOf node1 =! 3.0
        xOf node2 =! 13.0

    /// A drag decides which nodes it moves once, when it starts. A node selected after that was
    /// not part of the drag, so it must not start moving.
    [<Fact>]
    member _.``A node selected after the drag started does not move with it``() =
        let node1 = spawnSelectedNode 0.0
        let node2 = world.Spawn(Position.Val {| x = 10.0; y = 0.0; z = 0.0 |})

        startDrag ()

        node2 |> add Selected
        dragBy 3.0

        xOf node1 =! 3.0
        xOf node2 =! 10.0

    /// The opposite case: deselecting a node cannot remove it from a drag that is already moving
    /// it, so a node can never stop part-way through a drag.
    [<Fact>]
    member _.``A node deselected after the drag started keeps moving with it``() =
        let node1 = spawnSelectedNode 0.0
        let node2 = spawnSelectedNode 10.0

        startDrag ()

        node2 |> remove Selected
        dragBy 3.0

        xOf node1 =! 3.0
        xOf node2 =! 13.0

    /// Each node is placed at its starting position plus the distance the drag has travelled,
    /// rather than moved by the change since the last frame, so it ends up in the right place
    /// even if something else moved it in the meantime.
    [<Fact>]
    member _.``A participant moved by something else is put back where the gesture says``() =
        let node = spawnSelectedNode 0.0

        startDrag ()

        node |> setValue Position {| x = 50.0; y = 0.0; z = 0.0 |}
        dragBy 3.0

        xOf node =! 3.0

    /// The command a drag commits is what undo and redo later apply. `Before` is the position the
    /// node had when the drag started, so undo returns it to where the drag began rather than to
    /// somewhere it passed through on the way.
    [<Fact>]
    member _.``A drag that moved a node records where it started and where it ended``() =
        let node = spawnSelectedNode 5.0

        startDrag ()
        dragBy 7.0
        endDrag ()

        world |> committedCommands |> List.map _.Moves
        =! [ [ moveAlongX node 5.0 12.0 ] ]

    /// A drag that never moved changes nothing, and neither does one that moves away and returns.
    /// Committing either would add an undo entry that does nothing when applied, and would also
    /// clear the redo stack.
    [<Fact>]
    member _.``A drag that ended where it began records nothing``() =
        spawnSelectedNode 5.0 |> ignore

        startDrag ()
        dragBy 7.0
        dragBy 0.0
        endDrag ()

        world |> committedCommands =! []

    /// A drag that starts with nothing selected has no nodes to move, so the release has nothing
    /// to commit. The drag still runs to completion; it just moves and commits nothing.
    [<Fact>]
    member _.``A drag with nothing selected records nothing``() =
        world.Spawn(Position.Val {| x = 5.0; y = 0.0; z = 0.0 |}) |> ignore

        startDrag ()
        dragBy 7.0
        endDrag ()

        world |> committedCommands =! []

    /// Dragging a node that is still animating commits nothing. Undo would move the node back to
    /// where it will come to rest, and a drag doesn't change that: it only writes Position, while
    /// the node is still headed for its TargetPosition. The test for "still animating" is whether
    /// the node has a TargetPosition at the moment the drag stores its starting position.
    [<Fact>]
    member _.``A drag of a node still animating when its origin is captured records nothing``() =
        let node = spawnSelectedNode 5.0
        node |> addWith TargetPosition {| x = 100.0; y = 0.0; z = 0.0 |}

        startDrag ()
        dragBy 7.0
        endDrag ()

        world |> committedCommands =! []

    /// A selection can contain both a node that has stopped and one that is still animating. The
    /// drag moves both, but only the stopped one has a change that undo can reverse.
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

    /// A drag of several nodes is one change covering every node it moved, so a single undo
    /// reverses all of them. Committing only the first node would leave the others where they
    /// were dropped. The moves are compared as a set because a command's moves are applied
    /// independently, so the order the query returned them in doesn't matter.
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

    /// A second gesture can be pressed and moved in the frame that the running drag is released.
    /// Its start is refused, so its movement belongs to no drag. Applied in order, that movement
    /// arrives after the release has already committed, so it moves nothing and the drag that was
    /// released records the position it actually ended at.
    [<Fact>]
    member _.``A move that arrived after a release does not move or record anything``() =
        let node = spawnSelectedNode 5.0

        startDrag ()
        dragBy 3.0

        handleDragEnd world
        handleDragStart world
        handleDrag world 99.0 0.0 0.0
        dragNodes world |> ignore

        xOf node =! 8.0

        world |> committedCommands |> List.map _.Moves
        =! [ [ moveAlongX node 5.0 8.0 ] ]

    interface IDisposable with
        member _.Dispose() = (ecs :> IDisposable).Dispose()
