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

    let buttonWithLabel label =
        world.QueryTrait(Button).ToSequence()
        |> Seq.find (fun (buttonData, _) -> buttonData.label = label)
        |> snd

    let isDisabled entity = (entity |> get Button).Value.disabled

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

    /// Drags a node to the given x in realistic event order — begin, move, each in its own frame,
    /// as the host dispatches them. The caller raises the drag end, so it can choose what else
    /// lands in that frame.
    let dragNodeTo node x =
        node |> handlePointerDown
        runSystems world frameDelta
        handleDragStart world |> ignore
        runSystems world frameDelta
        handleDrag world x 0.0 0.0 |> ignore
        runSystems world frameDelta

    /// Spawns a selected tree node at the origin, which is what a drag moves.
    let spawnSelectedNode () =
        world.Spawn(PersonRef.Val Person.Empty, Position.Val zeroPosition, Selected.Tag())

    /// Leaves the boot View mode for Move mode, where dragging and undo history are possible.
    let enterMoveMode () =
        buttonWithLabel "Move" |> handleClick world
        runSystems world frameDelta

    /// Ends the drag in flight and runs the frame that processes the release.
    let endDrag () =
        handleDragEnd world |> ignore
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
        let modeButton = buttonWithLabel "Move"
        let selectModeButton = buttonWithLabel "Multi-select"

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
        let modeButton = buttonWithLabel "Move"
        let node = world.Spawn(PersonRef.Val Person.Empty, Position.Val zeroPosition)

        node |> handleClick world
        modeButton |> handleClick world
        runSystems world frameDelta

        world.Has InViewMode =! false
        node |> has Selected =! false

    /// Pins the pipeline order, which the node-click case cannot. A click that reaches
    /// `handleUndoRedo` pops the undo stack and moves the node; only an undo the mode switch
    /// intercepted first leaves the history and the node's position both untouched.
    [<Fact>]
    member _.``runSystems discards a same-frame undo click when the mode toggles``() =
        enterMoveMode ()

        // Build one undo entry by dragging a node from x = 0 to x = 4.
        let node = spawnSelectedNode ()
        dragNodeTo node 4.0
        endDrag ()

        let undoButton = buttonWithLabel "Undo"
        undoButton |> has Hidden =! false

        // Tap Undo and the mode button together: the switch wins, so no undo is applied.
        undoButton |> handleClick world
        buttonWithLabel "View" |> handleClick world
        runSystems world frameDelta

        world.Has InViewMode =! true
        node |> has TargetPosition =! false
        // The entry is still on the undo stack, so the click was intercepted rather than applied
        // and then discarded.
        isDisabled undoButton =! false

    /// A pointer committed to a drag is not also pressing a control, so a toolbar tap landing
    /// mid-drag is fallout from the drag rather than input in its own right.
    [<Fact>]
    member _.``runSystems ignores a toolbar tap while a drag is in flight``() =
        enterMoveMode ()
        dragNodeTo (spawnSelectedNode ()) 4.0

        buttonWithLabel "View" |> handleClick world
        runSystems world frameDelta

        world.Has InViewMode =! false

    /// A drag released over empty space raises a background miss, which would otherwise read as a
    /// deliberate click away from the selection and drop what was just dragged.
    [<Fact>]
    member _.``runSystems keeps the selection when a background click lands mid-drag``() =
        enterMoveMode ()
        let node = spawnSelectedNode ()
        dragNodeTo node 4.0

        handlePointerMissed world |> ignore
        runSystems world frameDelta

        node |> has Selected =! true

    /// Releasing a drag raises a click on the node it was dragging, which would otherwise
    /// deselect the node the moment the user let go of it.
    [<Fact>]
    member _.``runSystems keeps the selection when a node click lands mid-drag``() =
        enterMoveMode ()
        let node = spawnSelectedNode ()
        dragNodeTo node 4.0

        node |> handleClick world
        runSystems world frameDelta

        node |> has Selected =! true

    /// Refusing input during a drag must not outlast the drag, or the app would take no input at
    /// all after the first one.
    [<Fact>]
    member _.``runSystems accepts a toolbar tap once a drag has ended``() =
        enterMoveMode ()
        dragNodeTo (spawnSelectedNode ()) 4.0
        endDrag ()

        buttonWithLabel "View" |> handleClick world
        runSystems world frameDelta

        world.Has InViewMode =! true

    /// The redo history is discarded by inferring what was dragged from the `Selected` nodes at
    /// release, so anything clearing the selection in that frame hides the drag from it. Every
    /// route that clears a selection is driven by input, and input is refused while a drag is in
    /// flight, so a release now always sees what it dragged.
    [<Fact>]
    member _.``runSystems discards redo history for a drag released as the mode is tapped``() =
        enterMoveMode ()
        let node = spawnSelectedNode ()

        // Drag 0 -> 4 and release it cleanly, then undo. Wait out the undo animation, because
        // only a node that has stopped animating is eligible to be snapshotted again.
        dragNodeTo node 4.0
        endDrag ()

        buttonWithLabel "Undo" |> handleClick world
        runSystems world frameDelta
        runUntilSettled ()
        isDisabled (buttonWithLabel "Redo") =! false

        // Drag 0 -> 7, this time releasing in the same frame as a tap on the mode button.
        dragNodeTo node 7.0
        handleDragEnd world |> ignore
        buttonWithLabel "View" |> handleClick world
        runSystems world frameDelta

        // The tap belonged to the drag, so the mode is unchanged, and the completed drag
        // invalidated the redo entry it superseded.
        world.Has InViewMode =! false
        isDisabled (buttonWithLabel "Redo") =! true

    /// A drag refuses input from the moment its start is raised, but input accepted just *before*
    /// that lands in the same frame and would otherwise still take effect. Discarding it when the
    /// drag starts is what keeps a coalesced frame from applying both.
    [<Fact>]
    member _.``runSystems discards redo history for a drag coalesced with an earlier mode tap``() =
        enterMoveMode ()
        let node = spawnSelectedNode ()

        // Build a redo entry: drag 0 -> 4, release, undo, and wait out the undo animation.
        dragNodeTo node 4.0
        endDrag ()
        buttonWithLabel "Undo" |> handleClick world
        runSystems world frameDelta
        runUntilSettled ()
        isDisabled (buttonWithLabel "Redo") =! false

        // Tap the mode button, then press, move and release the node — all before a frame runs.
        buttonWithLabel "View" |> handleClick world
        node |> handlePointerDown
        handleDragStart world |> ignore
        handleDrag world 7.0 0.0 0.0 |> ignore
        handleDragEnd world |> ignore
        runSystems world frameDelta

        // The drag superseded the tap, so the mode is unchanged and the node moved.
        world.Has InViewMode =! false
        (node |> get Position).Value.x =! 7.0

        // The completed drag invalidated the redo entry it superseded.
        isDisabled (buttonWithLabel "Redo") =! true

    /// TODO: Fix the useless undo entry that this test pins.
    ///
    /// `runSystems` runs `dragNodes` before `handleUndoRedo`, so `handleDrag` has already moved
    /// the nodes by the time `UndoRedo.handleDragStart` snapshots their positions. Normally that
    /// is harmless, because a drag start reaches the pipeline a frame before any movement, so the
    /// captured position really is the pre-drag one. But the host can coalesce press, move and
    /// release into a single frame — a stalled or dropped browser frame does exactly that — and
    /// then the snapshot records the drag's *destination* as if it were its origin. The resulting
    /// undo entry sends the node to where it already is, so the move cannot be taken back.
    ///
    /// This is distinct from the precedence bug in `UndoRedo.handleUndoRedo`: no button is
    /// involved here, and the stacks are not corrupted — the entry is merely useless. The fix is
    /// to capture starting positions before `dragNodes` moves anything, which is a change to the
    /// pipeline rather than to a single system.
    ///
    /// Nothing else drives a coalesced frame: the `dragNodeTo` helper above deliberately gives
    /// each event its own frame, as a responsive host would.
    [<Fact>]
    member _.``KNOWN BUG: a drag coalesced into one frame records an undo entry that cannot undo it``() =
        enterMoveMode ()
        let node = spawnSelectedNode ()

        // Press, move and release the node without ever yielding a frame between them, as a
        // stalled host would deliver them.
        node |> handlePointerDown
        handleDragStart world |> ignore
        handleDrag world 4.0 0.0 0.0 |> ignore
        handleDragEnd world |> ignore
        runSystems world frameDelta

        // The drag itself worked: the node moved, and an undo entry was recorded for it.
        (node |> get Position).Value.x =! 4.0
        let undoButton = buttonWithLabel "Undo"
        isDisabled undoButton =! false

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
        let modeBtn = buttonWithLabel "Move"

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

        // Drag in realistic event order: begin (captures the pre-drag position), then move,
        // then end — each in its own frame, as the host dispatches them.
        handleDragStart world |> ignore
        runSystems world frameDelta

        handleDrag world (origX + 2.0) originalPos.y originalPos.z |> ignore
        runSystems world frameDelta

        handleDragEnd world |> ignore
        runSystems world frameDelta

        let movedPos = (nodeEntity |> get Position).Value
        movedPos.x <>! origX

        buttonWithLabel "Undo" |> handleClick world
        runSystems world frameDelta

        // Undo animates the node back toward its pre-drag position via TargetPosition.
        (nodeEntity |> has TargetPosition) =! true
        (nodeEntity |> get TargetPosition).Value =! originalPos
