module Wilnaatahl.Tests.Systems.RunnerTests

open System
open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.Model
open Wilnaatahl.Model.FamilyGraph
open Wilnaatahl.ViewModel.Vector
open Wilnaatahl.System.Layout
open Wilnaatahl.Traits.Events
open Wilnaatahl.Traits.PeopleTraits
open Wilnaatahl.Traits.SpaceTraits
open Wilnaatahl.Traits.ViewTraits
open Wilnaatahl.Systems.LifeCycle
open Wilnaatahl.Systems.Runner
open Wilnaatahl.Tests.EcsTestSupport
open Wilnaatahl.Tests.TestData

type Tests() =
    let ecs = new EcsWorld()
    let world = ecs.World
    do spawnControls world

    /// Simulates one frame at 60 FPS.
    let frameDelta = 0.016

    /// Runs frames until nothing is animating. Waits on the observable end state rather than a
    /// frame count chosen to be big enough. Requires something to be animating on entry, so it
    /// can't report success for a scene that never started moving.
    let runUntilSettled () =
        let stillAnimating () =
            world.Query(With TargetPosition) |> Seq.isEmpty |> not

        let rec run framesLeft =
            if stillAnimating () && framesLeft > 0 then
                runSystems world 0.1
                run (framesLeft - 1)

        stillAnimating () =! true
        run 1000
        stillAnimating () =! false

    /// Drags a node to the given x, giving each event its own frame. This is not quite how the
    /// browser delivers a drag — use-gesture raises the start and the first movement together —
    /// but it keeps frame boundaries explicit for the tests that care about them. Tests that turn
    /// on the real ordering spell it out themselves. The caller raises the drag end, so it can
    /// choose what else lands in that frame.
    let dragNodeTo node x =
        node |> handlePointerDown
        runSystems world frameDelta
        handleDragStart world
        runSystems world frameDelta
        handleDrag world x 0.0 0.0
        runSystems world frameDelta

    /// Spawns a selected tree node at the origin, which is what a drag moves.
    let spawnSelectedNode () =
        world.Spawn(PersonRef.Val Person.Empty, Position.Val zeroPosition, Selected.Tag())

    /// Leaves the boot View mode for Move mode, where dragging and undo history are possible.
    let enterMoveMode () =
        world |> buttonWithLabel "Move" |> handleClick world
        runSystems world frameDelta

    /// Ends the drag in flight and runs the frame that processes the release.
    let endDrag () =
        handleDragEnd world
        runSystems world frameDelta

    interface IDisposable with
        member _.Dispose() = (ecs :> IDisposable).Dispose()

    [<Fact>]
    member _.``runSystems with no events completes without error``() = runSystems world frameDelta

    [<Fact>]
    member _.``runSystems cleans up events at end of frame``() =
        // The traits are set directly rather than through the handlers, which refuse and discard
        // input around a drag and so cannot leave both events standing at once.
        world.Add PointerMissedEvent
        world.Add DragStartEvent
        world.Has PointerMissedEvent =! true
        world.Has DragStartEvent =! true
        runSystems world frameDelta
        world.Has PointerMissedEvent =! false
        world.Has DragStartEvent =! false

    [<Fact>]
    member _.``runSystems animates entities toward target``() =
        let entity =
            world.Spawn(Position.Val zeroPosition, TargetPosition.Val {| x = 10.0; y = 0.0; z = 0.0 |})

        // Position starts at origin (0,0,0), so any movement toward target is progress.
        let posBefore = (entity |> get Position).Value.x
        runSystems world 0.5
        let posAfter = (entity |> get Position).Value.x
        posAfter >! posBefore

    /// Which controls are available is derived from the mode, so toggling the mode must leave the
    /// modal controls consistent within that same frame rather than a frame later.
    [<Fact>]
    member _.``runSystems syncs the Move-mode-only controls within the frame that toggles the mode``() =
        let modeButton = world |> buttonWithLabel "Move"
        let selectModeButton = world |> buttonWithLabel "Multi-select"

        // Boot is View mode, where the select-mode button is meaningless and so hidden.
        world.Has InViewMode =! true
        selectModeButton |> has Hidden =! true

        // View -> Move: the button becomes meaningful the moment the mode changes.
        modeButton |> handleClick world
        runSystems world frameDelta
        world.Has InViewMode =! false
        selectModeButton |> has Hidden =! false

        // Move -> View: and meaningless again, still within the toggling frame.
        modeButton |> handleClick world
        runSystems world frameDelta
        world.Has InViewMode =! true
        selectModeButton |> has Hidden =! true

    /// Switching mode starts the new mode clean, so a node click landing in the same frame as the
    /// mode toggle must not survive into the mode being entered.
    [<Fact>]
    member _.``runSystems clears a same-frame node click when the mode toggles``() =
        let modeButton = world |> buttonWithLabel "Move"
        let node = world.Spawn(PersonRef.Val Person.Empty, Position.Val zeroPosition)

        node |> handleClick world
        modeButton |> handleClick world
        runSystems world frameDelta

        world.Has InViewMode =! false
        node |> has Selected =! false

    /// Checks the pipeline order, which the node-click test cannot. If undo ran first it would
    /// take an entry off the stack and move the node. Because the mode switch runs first and
    /// drops the click, both the stack and the node are left alone.
    [<Fact>]
    member _.``runSystems discards a same-frame undo click when the mode toggles``() =
        enterMoveMode ()

        // Build one undo entry by dragging a node from x = 0 to x = 4.
        let node = spawnSelectedNode ()
        dragNodeTo node 4.0
        endDrag ()

        let undoButton = world |> buttonWithLabel "Undo"
        undoButton |> has Hidden =! false

        // Tap Undo and the mode button together: the switch wins, so no undo is applied.
        undoButton |> handleClick world
        world |> buttonWithLabel "View" |> handleClick world
        runSystems world frameDelta

        world.Has InViewMode =! true
        node |> has TargetPosition =! false
        // The entry is still on the undo stack, so the click was intercepted rather than applied
        // and then discarded.
        isButtonDisabled undoButton =! false

    /// A second finger can tap a control while the first one drags. The tap is deliberate, but it
    /// has no coherent meaning: the app would have to carry out a toolbar command and a drag in
    /// the same frame. The drag wins, and the tap is dropped.
    [<Fact>]
    member _.``runSystems ignores a toolbar tap while a drag is in flight``() =
        enterMoveMode ()
        dragNodeTo (spawnSelectedNode ()) 4.0

        world |> buttonWithLabel "View" |> handleClick world
        runSystems world frameDelta

        world.Has InViewMode =! false

    /// A background miss during a drag would clear the selection, dropping the node being dragged.
    [<Fact>]
    member _.``runSystems keeps the selection when a background click lands mid-drag``() =
        enterMoveMode ()
        let node = spawnSelectedNode ()
        dragNodeTo node 4.0

        handlePointerMissed world
        runSystems world frameDelta

        node |> has Selected =! true

    /// A click can land on the dragged node during a drag — a drag short enough to also count as
    /// a tap raises one. Acting on it would deselect the node the user is dragging.
    [<Fact>]
    member _.``runSystems keeps the selection when a node click lands mid-drag``() =
        enterMoveMode ()
        let node = spawnSelectedNode ()
        dragNodeTo node 4.0

        node |> handleClick world
        runSystems world frameDelta

        node |> has Selected =! true

    /// Refusing input must stop when the drag does, or the app would accept no input at all after
    /// the first drag.
    [<Fact>]
    member _.``runSystems accepts a toolbar tap once a drag has ended``() =
        enterMoveMode ()
        dragNodeTo (spawnSelectedNode ()) 4.0
        endDrag ()

        world |> buttonWithLabel "View" |> handleClick world
        runSystems world frameDelta

        world.Has InViewMode =! true

    /// The browser can raise a drag end with no drag behind it. Nothing is in flight at that
    /// moment, so `Events` has nothing to refuse input against and the stray event reaches the
    /// frame. Undo/redo would read it as a real release and throw away the redo history, so
    /// `Dragging` removes it first — which is why `Dragging` runs before undo/redo.
    [<Fact>]
    member _.``runSystems keeps redo history when a stray drag end arrives``() =
        enterMoveMode ()
        let node = spawnSelectedNode ()

        // Build a redo entry: drag, release, undo, then wait out the undo animation so the node
        // is settled and would count as a dragged node.
        dragNodeTo node 4.0
        endDrag ()
        world |> buttonWithLabel "Undo" |> handleClick world
        runSystems world frameDelta
        runUntilSettled ()

        let redoButton = world |> buttonWithLabel "Redo"
        isButtonDisabled redoButton =! false

        handleDragEnd world
        runSystems world frameDelta

        isButtonDisabled redoButton =! false

    /// When a drag is released, undo works out which nodes were dragged by looking at which ones
    /// are selected. Anything that clears the selection in that frame hides the drag from it. All
    /// such clearing comes from input, and input is refused during a drag, so a release always
    /// sees what it dragged.
    [<Fact>]
    member _.``runSystems discards redo history for a drag released as the mode is tapped``() =
        enterMoveMode ()
        let node = spawnSelectedNode ()

        // Drag 0 -> 4 and release it cleanly, then undo. Wait out the undo animation, because
        // only a node that has stopped animating is eligible to be snapshotted again.
        dragNodeTo node 4.0
        endDrag ()

        world |> buttonWithLabel "Undo" |> handleClick world
        runSystems world frameDelta
        runUntilSettled ()
        world |> buttonWithLabel "Redo" |> isButtonDisabled =! false

        // Drag 0 -> 7, this time releasing in the same frame as a tap on the mode button.
        dragNodeTo node 7.0
        handleDragEnd world
        world |> buttonWithLabel "View" |> handleClick world
        runSystems world frameDelta

        // The tap belonged to the drag, so the mode is unchanged, and the completed drag
        // invalidated the redo entry it superseded.
        world.Has InViewMode =! false
        world |> buttonWithLabel "Redo" |> isButtonDisabled =! true

    /// Input is refused from the moment a drag start arrives, but a tap accepted just before that
    /// lands in the same frame. Dropping it stops one frame from applying both a tap and a drag.
    [<Fact>]
    member _.``runSystems discards redo history for a drag coalesced with an earlier mode tap``() =
        enterMoveMode ()
        let node = spawnSelectedNode ()

        // Build a redo entry: drag 0 -> 4, release, undo, and wait out the undo animation.
        dragNodeTo node 4.0
        endDrag ()
        world |> buttonWithLabel "Undo" |> handleClick world
        runSystems world frameDelta
        runUntilSettled ()
        world |> buttonWithLabel "Redo" |> isButtonDisabled =! false

        // Tap the mode button, then press, move and release the node — all before a frame runs.
        world |> buttonWithLabel "View" |> handleClick world
        node |> handlePointerDown
        handleDragStart world
        handleDrag world 7.0 0.0 0.0
        handleDragEnd world
        runSystems world frameDelta

        // The drag superseded the tap, so the mode is unchanged and the node moved.
        world.Has InViewMode =! false
        (node |> get Position).Value.x =! 7.0

        // The completed drag invalidated the redo entry it superseded.
        world |> buttonWithLabel "Redo" |> isButtonDisabled =! true

    /// TODO: Fix the wrong undo position that this test pins.
    ///
    /// Undo saves a node's starting position when a drag begins, so it can put the node back. But
    /// `runSystems` moves the node before it saves that position, so what gets saved is where the
    /// node was after moving, not where it started.
    ///
    /// This happens on every drag. use-gesture calls `onDragStart` and then `onDrag` one after the
    /// other, in the same call, as soon as the pointer moves far enough to count as a drag (see
    /// `parser.ts` in `@use-gesture/core`). So a drag start and its first movement always reach
    /// the app together, and undo is always off by that first movement.
    ///
    /// How far off depends on how far the first movement carries the node. The coalesced-frame
    /// test below is the worst case, where the first movement is the whole drag.
    ///
    /// Fixing it means saving positions before anything moves the node, which is a change to the
    /// order of the pipeline rather than to a single system.
    [<Fact>]
    member _.``KNOWN BUG: undo after a drag returns the node to just after its first movement``() =
        enterMoveMode ()
        let node = spawnSelectedNode ()

        // The start and the first movement arrive together, as use-gesture dispatches them.
        node |> handlePointerDown
        runSystems world frameDelta
        handleDragStart world
        handleDrag world 1.0 0.0 0.0
        runSystems world frameDelta

        // Later movements arrive in their own frames.
        handleDrag world 6.0 0.0 0.0
        runSystems world frameDelta
        endDrag ()

        (node |> get Position).Value.x =! 6.0

        // Undo should send the node back to x = 0, where the drag began. It sends it to x = 1,
        // where the node stood after the first movement.
        world |> buttonWithLabel "Undo" |> handleClick world
        runSystems world frameDelta
        (node |> get TargetPosition).Value.x =! 1.0

    /// TODO: Fix the useless undo entry that this test pins.
    ///
    /// The same bug as the test above, at its worst. A browser that stalls can deliver the press,
    /// every movement and the release together. The node then completes its whole journey before
    /// undo saves its position, so undo saves the place the drag ended.
    ///
    /// Clicking Undo then moves the node to where it already is, so the drag cannot be taken back
    /// at all. Above, the node at least returns most of the way.
    [<Fact>]
    member _.``KNOWN BUG: a drag coalesced into one frame records an undo entry that cannot undo it``() =
        enterMoveMode ()
        let node = spawnSelectedNode ()

        // Press, move and release the node without ever yielding a frame between them, as a
        // stalled host would deliver them.
        node |> handlePointerDown
        handleDragStart world
        handleDrag world 4.0 0.0 0.0
        handleDragEnd world
        runSystems world frameDelta

        // The drag itself worked: the node moved, and an undo entry was recorded for it.
        (node |> get Position).Value.x =! 4.0
        let undoButton = world |> buttonWithLabel "Undo"
        isButtonDisabled undoButton =! false

        // But the entry saved the position the drag *ended* at, so undoing it targets x = 4 —
        // where the node already is — instead of the x = 0 it was dragged from. The undo is
        // spent, and the move is unrecoverable.
        undoButton |> handleClick world
        runSystems world frameDelta
        (node |> get TargetPosition).Value.x =! 4.0

    [<Fact>]
    member _.``integration: select, drag, and undo on scene nodes``() =
        let graph = createFamilyGraph testPeopleAndParents testCouples []

        spawnScene world graph
        layoutNodes world graph
        runUntilSettled ()

        // Dragging requires Move mode, and the undo/redo buttons are hidden there, so leave the
        // boot View mode before exercising a drag and undo.
        let modeBtn = world |> buttonWithLabel "Move"

        modeBtn |> handleClick world
        runSystems world frameDelta
        world.Has InViewMode =! false

        let nodeEntity = world.Query(With PersonRef) |> Seq.head
        let originalPos = (nodeEntity |> get Position).Value
        let origX = originalPos.x

        handlePointerDown nodeEntity
        nodeEntity |> handleClick world
        runSystems world frameDelta
        (nodeEntity |> has Selected) =! true

        // Drag with each event in its own frame. The browser raises the start and the first
        // movement together, but doing that here would trip the known undo bug the tests above
        // pin, and this test is about the scene as a whole rather than that bug.
        handleDragStart world
        runSystems world frameDelta

        handleDrag world (origX + 2.0) originalPos.y originalPos.z
        runSystems world frameDelta

        handleDragEnd world
        runSystems world frameDelta

        let movedPos = (nodeEntity |> get Position).Value
        movedPos.x <>! origX

        world |> buttonWithLabel "Undo" |> handleClick world
        runSystems world frameDelta

        // Undo animates the node back toward its pre-drag position via TargetPosition.
        (nodeEntity |> has TargetPosition) =! true
        (nodeEntity |> get TargetPosition).Value =! originalPos
