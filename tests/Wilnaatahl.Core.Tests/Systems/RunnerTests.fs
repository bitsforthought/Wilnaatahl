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

    /// Drags the selection to the given x, giving each event its own frame. This is not quite how
    /// the browser delivers a drag — use-gesture raises the start and the first movement together
    /// — but it keeps frame boundaries explicit for the tests that care about them. Tests that
    /// turn on the real ordering spell it out themselves. The caller raises the drag end, so it
    /// can choose what else lands in that frame.
    let dragTo x =
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

    /// A node under the pointer is the drag's to place. Leaving it in the animation's hands as
    /// well would let it creep toward its target on every frame the pointer holds still, then
    /// snap back the moment the pointer moves again.
    [<Fact>]
    member _.``runSystems holds a dragged node still while its animation waits``() =
        enterMoveMode ()
        let node = spawnSelectedNode ()
        node |> addWith TargetPosition {| x = 100.0; y = 0.0; z = 0.0 |}

        dragTo 4.0

        // Not 4.0: `animate` runs before `dragNodes`, so on the frame that starts the drag the
        // node takes one more step toward its target before its origin is captured. It is
        // captured at the position that frame renders, so the grab is still faithful.
        let whereTheDragPutIt = (node |> get Position).Value.x

        // A frame with no pointer movement: nothing should move the node.
        runSystems world frameDelta
        (node |> get Position).Value.x =! whereTheDragPutIt

        // The animation is paused, not cancelled, so releasing the node resumes it.
        node |> has TargetPosition =! true
        endDrag ()
        runUntilSettled ()
        (node |> get Position).Value.x =! 100.0

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
        dragTo 4.0
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
        spawnSelectedNode () |> ignore
        dragTo 4.0

        world |> buttonWithLabel "View" |> handleClick world
        runSystems world frameDelta

        world.Has InViewMode =! false

    /// The first start fixes the participants and their origins. A second one arriving while the
    /// drag still runs would re-base the gesture, and the node would jump by the distance it had
    /// already travelled — and the release would record that shortened drag, so undo would take
    /// back only part of it.
    [<Fact>]
    member _.``runSystems ignores a drag start that arrives mid-drag``() =
        enterMoveMode ()
        let node = spawnSelectedNode ()

        dragTo 4.0

        // A second start, then the gesture carries on to 5.
        handleDragStart world
        runSystems world frameDelta
        handleDrag world 5.0 0.0 0.0
        runSystems world frameDelta
        endDrag ()

        (node |> get Position).Value.x =! 5.0

        // One undo takes the whole drag back, so only one entry was recorded.
        world |> buttonWithLabel "Undo" |> handleClick world
        runSystems world frameDelta
        (node |> get TargetPosition).Value.x =! 0.0

    /// The selection is what the view layer paints and makes draggable, so clearing it mid-drag
    /// would leave the app dragging nodes it no longer shows as selected.
    [<Fact>]
    member _.``runSystems keeps the selection when a background click lands mid-drag``() =
        enterMoveMode ()
        let node = spawnSelectedNode ()
        dragTo 4.0

        handlePointerMissed world
        runSystems world frameDelta

        node |> has Selected =! true

    /// A click can land on the dragged node during a drag — a drag short enough to also count as
    /// a tap raises one. Acting on it would deselect the node the user is dragging, so the view
    /// layer would stop painting it as selected mid-gesture.
    [<Fact>]
    member _.``runSystems keeps the selection when a node click lands mid-drag``() =
        enterMoveMode ()
        let node = spawnSelectedNode ()
        dragTo 4.0

        node |> handleClick world
        runSystems world frameDelta

        node |> has Selected =! true

    /// Refusing input must stop when the drag does, or the app would accept no input at all after
    /// the first drag.
    [<Fact>]
    member _.``runSystems accepts a toolbar tap once a drag has ended``() =
        enterMoveMode ()
        spawnSelectedNode () |> ignore
        dragTo 4.0
        endDrag ()

        world |> buttonWithLabel "View" |> handleClick world
        runSystems world frameDelta

        world.Has InViewMode =! true

    /// The browser can raise a drag end with no drag behind it, and `Events.handleDragEnd` passes
    /// every one of them straight through. It must not be mistaken for a completed change: no
    /// drag ran, so nothing was recorded, and the redo history stands.
    [<Fact>]
    member _.``runSystems keeps redo history when a stray drag end arrives``() =
        enterMoveMode ()
        let node = spawnSelectedNode ()

        // Build a redo entry: drag, release, undo, then wait out the undo animation so the node
        // is settled and a later drag of it would record something.
        dragTo 4.0
        endDrag ()
        world |> buttonWithLabel "Undo" |> handleClick world
        runSystems world frameDelta
        runUntilSettled ()

        let redoButton = world |> buttonWithLabel "Redo"
        isButtonDisabled redoButton =! false

        handleDragEnd world
        runSystems world frameDelta

        isButtonDisabled redoButton =! false

    /// A mode tap coincident with a release is refused at the door, so the release is processed
    /// as an ordinary one: the drag records what it moved, and that discards the redo history.
    [<Fact>]
    member _.``runSystems discards redo history for a drag released as the mode is tapped``() =
        enterMoveMode ()
        let node = spawnSelectedNode ()

        // Drag 0 -> 4 and release it cleanly, then undo. Wait out the undo animation, because a
        // node that is still animating when it is grabbed records nothing.
        dragTo 4.0
        endDrag ()

        world |> buttonWithLabel "Undo" |> handleClick world
        runSystems world frameDelta
        runUntilSettled ()
        world |> buttonWithLabel "Redo" |> isButtonDisabled =! false

        // Drag 0 -> 7, this time releasing in the same frame as a tap on the mode button.
        dragTo 7.0
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
        dragTo 4.0
        endDrag ()
        world |> buttonWithLabel "Undo" |> handleClick world
        runSystems world frameDelta
        runUntilSettled ()
        world |> buttonWithLabel "Redo" |> isButtonDisabled =! false

        // Tap the mode button, then move and release the node — all before a frame runs.
        world |> buttonWithLabel "View" |> handleClick world
        handleDragStart world
        handleDrag world 7.0 0.0 0.0
        handleDragEnd world
        runSystems world frameDelta

        // The drag superseded the tap, so the mode is unchanged and the node moved.
        world.Has InViewMode =! false
        (node |> get Position).Value.x =! 7.0

        // The completed drag invalidated the redo entry it superseded.
        world |> buttonWithLabel "Redo" |> isButtonDisabled =! true

    /// use-gesture calls `onDragStart` and then `onDrag` one after the other, in the same call, as
    /// soon as the pointer moves far enough to count as a drag (see `parser.ts` in
    /// `@use-gesture/core`). So a drag start and its first movement always reach the app together,
    /// and undo used to be off by that first movement on every single drag. The drag now captures
    /// each origin itself, before it moves anything, so the coalescing cannot be seen.
    [<Fact>]
    member _.``runSystems undoes a drag whose start and first movement share a frame``() =
        enterMoveMode ()
        let node = spawnSelectedNode ()

        // The start and the first movement arrive together, as use-gesture dispatches them.
        handleDragStart world
        handleDrag world 1.0 0.0 0.0
        runSystems world frameDelta

        // Later movements arrive in their own frames.
        handleDrag world 6.0 0.0 0.0
        runSystems world frameDelta
        endDrag ()

        (node |> get Position).Value.x =! 6.0

        // Undo sends the node back to x = 0, where the drag began — not to x = 1, where it stood
        // after the first movement.
        world |> buttonWithLabel "Undo" |> handleClick world
        runSystems world frameDelta
        (node |> get TargetPosition).Value.x =! 0.0

    /// The worst case of the same coalescing: a browser that stalls can deliver the press, every
    /// movement and the release together, so the node completes its whole journey in one frame.
    /// Undo used to record the place the drag ended, leaving an entry that moved the node to
    /// where it already was — a drag that could not be taken back at all.
    [<Fact>]
    member _.``runSystems undoes a drag delivered entirely in one frame``() =
        enterMoveMode ()
        let node = spawnSelectedNode ()

        // Press, move and release the node without ever yielding a frame between them, as a
        // stalled host would deliver them.
        handleDragStart world
        handleDrag world 4.0 0.0 0.0
        handleDragEnd world
        runSystems world frameDelta

        // The drag itself worked: the node moved, and an undo entry was recorded for it.
        (node |> get Position).Value.x =! 4.0
        let undoButton = world |> buttonWithLabel "Undo"
        isButtonDisabled undoButton =! false

        // The whole drag is one change, taken back in one click: the node returns to the x = 0 it
        // was dragged from, not to the x = 4 it already sits at.
        undoButton |> handleClick world
        runSystems world frameDelta
        (node |> get TargetPosition).Value.x =! 0.0

    /// The boundary of "still animating": `animate` runs before the drag captures its origins, so
    /// a node close enough to its target finishes the journey on the very frame the grab lands. It
    /// has genuinely arrived, and the drag then moves it away from a position it had settled at,
    /// so that is a real change and is recorded.
    [<Fact>]
    member _.``runSystems records a drag of a node whose animation lands on the grab frame``() =
        enterMoveMode ()
        let node = spawnSelectedNode ()

        // Inside animate's completion tolerance, so this frame ends the animation exactly on
        // target rather than partway.
        node |> addWith TargetPosition {| x = 0.001; y = 0.0; z = 0.0 |}

        handleDragStart world
        handleDrag world 4.0 0.0 0.0
        runSystems world frameDelta
        node |> has TargetPosition =! false

        endDrag ()

        // Undo returns it to the target it settled on, not to where it was mid-flight.
        world |> buttonWithLabel "Undo" |> handleClick world
        runSystems world frameDelta
        (node |> get TargetPosition).Value.x =! 0.001

    [<Fact>]
    member _.``Integration: select, drag, and undo on scene nodes``() =
        let graph = createFamilyGraph testPeopleAndParents testCouples []

        spawnScene world graph
        layoutNodes world graph
        runUntilSettled ()

        // Dragging requires Move mode, and the undo/redo buttons are hidden in View mode, so
        // leave the boot View mode before exercising a drag and undo.
        let modeBtn = world |> buttonWithLabel "Move"

        modeBtn |> handleClick world
        runSystems world frameDelta
        world.Has InViewMode =! false

        let nodeEntity = world.Query(With PersonRef) |> Seq.head
        let originalPos = (nodeEntity |> get Position).Value
        let origX = originalPos.x

        nodeEntity |> handleClick world
        runSystems world frameDelta
        (nodeEntity |> has Selected) =! true

        // Deliver the start and the first movement together, as the browser does.
        handleDragStart world
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
