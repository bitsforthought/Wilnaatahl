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
open Wilnaatahl.Traits.Intents
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
    /// — but it keeps the frame boundaries explicit for the tests that care about them. Tests
    /// about the real ordering set it up themselves. The caller raises the drag end, so it can
    /// choose what else happens in that frame.
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
        world |> handlePointerMissed
        world |> handleDragEnd
        world |> inputEvents |> Seq.isEmpty =! false
        runSystems world frameDelta
        world |> inputEvents |> Seq.isEmpty =! true

    [<Fact>]
    member _.``runSystems animates entities toward target``() =
        let entity =
            world.Spawn(Position.Val zeroPosition, TargetPosition.Val {| x = 10.0; y = 0.0; z = 0.0 |})

        // Position starts at origin (0,0,0), so any movement toward target is progress.
        let posBefore = (entity |> get Position).Value.x
        runSystems world 0.5
        let posAfter = (entity |> get Position).Value.x
        posAfter >! posBefore

    /// While a drag is running, only the drag positions the node it is moving. If `animate` also
    /// moved it, the node would drift toward its target on every frame the pointer held still,
    /// then jump back as soon as the pointer moved again.
    [<Fact>]
    member _.``runSystems holds a dragged node still while its animation waits``() =
        enterMoveMode ()
        let node = spawnSelectedNode ()
        node |> addWith TargetPosition {| x = 100.0; y = 0.0; z = 0.0 |}

        dragTo 4.0

        // Not 4.0: `animate` runs before `dragNodes`, so on the frame the drag starts, the node
        // takes one more step toward its target before the drag stores its starting position.
        // That position is the one the frame renders, so it is the right one to store.
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
        world |> currentMode =! Viewing
        selectModeButton |> has Hidden =! true

        // View -> Move: the button becomes meaningful the moment the mode changes.
        modeButton |> handleClick world
        runSystems world frameDelta
        world |> currentMode =! Moving
        selectModeButton |> has Hidden =! false

        // Move -> View: and meaningless again, still within the toggling frame.
        modeButton |> handleClick world
        runSystems world frameDelta
        world |> currentMode =! Viewing
        selectModeButton |> has Hidden =! true

    /// The frame's true click order was node-then-mode-toggle: the node click happened while
    /// still in View mode, so it selects the node; the mode toggle that came after it, later in
    /// the same frame, still clears the selection it made. A fixed system-pass order that ran
    /// ViewMode's whole pass before Selection's, regardless of the clicks' real order, used to
    /// leave the node selected instead.
    [<Fact>]
    member _.``runSystems clears a same-frame node click that a later mode toggle supersedes``() =
        let modeButton = world |> buttonWithLabel "Move"
        let node = world.Spawn(PersonRef.Val Person.Empty, Position.Val zeroPosition)
        node |> addWith EmitsIntent [ ToggleNodeSelection node ]

        node |> handleClick world
        modeButton |> handleClick world
        runSystems world frameDelta

        world |> currentMode =! Moving
        node |> has Selected =! false

    /// The opposite true order: the mode toggle happens first, so the node click that follows it
    /// in the same frame is interpreted under the new mode and its selection survives.
    [<Fact>]
    member _.``runSystems keeps a same-frame node click that follows an earlier mode toggle``() =
        let modeButton = world |> buttonWithLabel "Move"
        let node = world.Spawn(PersonRef.Val Person.Empty, Position.Val zeroPosition)
        node |> addWith EmitsIntent [ ToggleNodeSelection node ]

        modeButton |> handleClick world
        node |> handleClick world
        runSystems world frameDelta

        world |> currentMode =! Moving
        node |> has Selected =! true

    /// Both mode clicks resolve from one snapshot, and the second explicit target is a no-op.
    [<Fact>]
    member _.``runSystems keeps selection when an idempotent second mode-button click follows a node click``() =
        let modeButton = world |> buttonWithLabel "Move"
        let node = world.Spawn(PersonRef.Val Person.Empty, Position.Val zeroPosition)
        node |> addWith EmitsIntent [ ToggleNodeSelection node ]

        modeButton |> handleClick world
        node |> handleClick world
        modeButton |> handleClick world
        runSystems world frameDelta

        world |> currentMode =! Moving
        node |> has Selected =! true

    /// Checks that a mode switch does not discard a command click raised while the command
    /// button was still available.
    [<Fact>]
    member _.``runSystems applies a same-frame undo click when the mode toggles``() =
        enterMoveMode ()

        // Build one undo entry by dragging a node from x = 0 to x = 4.
        let node = spawnSelectedNode ()
        dragTo 4.0
        endDrag ()

        let undoButton = world |> buttonWithLabel "Undo"
        undoButton |> has Hidden =! false

        // Tap Undo and the mode button together. Both clicks were raised in Move mode.
        undoButton |> handleClick world
        world |> buttonWithLabel "View" |> handleClick world
        runSystems world frameDelta

        world |> currentMode =! Viewing
        node |> get TargetPosition =! Some zeroPosition
        isButtonDisabled undoButton =! true

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

        world |> currentMode =! Moving

    /// The first drag start decides which nodes the drag moves and where each of them began. A
    /// second start arriving while the drag still runs would store those positions again, so the
    /// node would jump by the distance it had already travelled, and the release would commit only
    /// that shortened move — leaving undo able to reverse only part of the drag.
    [<Fact>]
    member _.``runSystems ignores a drag start that arrives mid-drag``() =
        enterMoveMode ()
        let node = spawnSelectedNode ()

        dragTo 4.0

        // A second start, then the drag carries on to 5.
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
    /// layer would stop drawing it as selected part-way through the drag.
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

        world |> currentMode =! Viewing

    /// The browser can raise a drag end when no drag is running, and `Events.handleDragEnd` passes
    /// every one of them through. It must not be treated as a completed drag: no drag ran, so
    /// nothing is committed and the redo stack is left alone.
    [<Fact>]
    member _.``runSystems keeps redo history when a stray drag end arrives``() =
        enterMoveMode ()
        let node = spawnSelectedNode ()

        // Build a redo entry: drag, release, undo, then let the undo animation finish so the node
        // has stopped and a later drag of it would commit something.
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

    /// A tap on the mode button in the same frame as a release is refused by `Events.handleClick`,
    /// so the release is handled as an ordinary one: the drag commits what it moved, and that
    /// clears the redo stack.
    [<Fact>]
    member _.``runSystems discards redo history for a drag released as the mode is tapped``() =
        enterMoveMode ()
        let node = spawnSelectedNode ()

        // Drag 0 -> 4 and release it cleanly, then undo. Let the undo animation finish, because a
        // node that is still animating when a drag starts on it commits nothing.
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

        // The tap was refused because a drag was running, so the mode is unchanged, and the
        // completed drag cleared the redo entry it replaced.
        world |> currentMode =! Moving
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
        world |> currentMode =! Moving
        (node |> get Position).Value.x =! 7.0

        // The completed drag invalidated the redo entry it superseded.
        world |> buttonWithLabel "Redo" |> isButtonDisabled =! true

    /// use-gesture calls `onDragStart` and then `onDrag` one after the other, in the same call, as
    /// soon as the pointer moves far enough to count as a drag (see `parser.ts` in
    /// `@use-gesture/core`). So a drag start and its first movement always reach the app in the
    /// same frame, and undo used to be wrong by that first movement on every drag. The drag now
    /// stores each starting position itself, before it moves anything, so this no longer matters.
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

        // Undo moves the node back to x = 0, where the drag began — not to x = 1, where it was
        // after the first movement.
        world |> buttonWithLabel "Undo" |> handleClick world
        runSystems world frameDelta
        (node |> get TargetPosition).Value.x =! 0.0

    /// The worst case of the same problem: a browser that stalls can deliver the press, every
    /// movement and the release in one frame, so the node makes its whole move at once. Undo used
    /// to store the position the drag ended at, giving an entry that moved the node to where it
    /// already was, so the drag could not be undone at all.
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

        // The drag itself worked: the node moved, and an undo entry was committed for it.
        (node |> get Position).Value.x =! 4.0
        let undoButton = world |> buttonWithLabel "Undo"
        isButtonDisabled undoButton =! false

        // The whole drag is one change, undone by one click: the node goes back to the x = 0 it
        // was dragged from, not to the x = 4 it is already at.
        undoButton |> handleClick world
        runSystems world frameDelta
        (node |> get TargetPosition).Value.x =! 0.0

    /// The boundary case for "still animating": `animate` runs before the drag stores its starting
    /// positions, so a node close enough to its target finishes animating on the very frame the
    /// drag starts. It has genuinely arrived, so when the drag then moves it away, that is a real
    /// change and is committed.
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

        // Undo moves it back to the target it stopped at, not to where it was part-way there.
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
        world |> currentMode =! Moving

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
