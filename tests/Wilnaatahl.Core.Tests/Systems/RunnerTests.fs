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

    interface IDisposable with
        member _.Dispose() = (ecs :> IDisposable).Dispose()

    [<Fact>]
    member _.``runSystems with no events completes without error``() = runSystems world frameDelta

    [<Fact>]
    member _.``runSystems cleans up events at end of frame``() =
        handlePointerMissed world |> ignore
        handleDragStart world |> ignore
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
        modeButton |> handleClick
        runSystems world frameDelta
        world.Has InViewMode =! false
        selectModeButton |> has Hidden =! false

        // Move -> View: and meaningless again, still within the toggling frame.
        modeButton |> handleClick
        runSystems world frameDelta
        world.Has InViewMode =! true
        selectModeButton |> has Hidden =! true

    /// Switching mode starts the new mode clean, so a node click landing in the same frame as the
    /// mode toggle must not survive into the mode being entered.
    [<Fact>]
    member _.``runSystems clears a same-frame node click when the mode toggles``() =
        let modeButton = buttonWithLabel "Move"
        let node = world.Spawn(PersonRef.Val Person.Empty, Position.Val zeroPosition)

        node |> handleClick
        modeButton |> handleClick
        runSystems world frameDelta

        world.Has InViewMode =! false
        node |> has Selected =! false

    /// Pins the pipeline order, which the node-click case cannot. A click that reaches
    /// `handleUndoRedo` pops the undo stack and moves the node; only an undo the mode switch
    /// intercepted first leaves the history and the node's position both untouched.
    [<Fact>]
    member _.``runSystems discards a same-frame undo click when the mode toggles``() =
        // Leave View mode so dragging — and therefore undo history — is possible.
        buttonWithLabel "Move" |> handleClick
        runSystems world frameDelta

        // Build one undo entry by dragging a node from x = 0 to x = 4.
        let node =
            world.Spawn(PersonRef.Val Person.Empty, Position.Val zeroPosition, Selected.Tag())

        dragNodeTo node 4.0
        handleDragEnd world |> ignore
        runSystems world frameDelta

        let undoButton = buttonWithLabel "Undo"
        undoButton |> has Hidden =! false

        // Tap Undo and the mode button together: the switch wins, so no undo is applied.
        undoButton |> handleClick
        buttonWithLabel "View" |> handleClick
        runSystems world frameDelta

        world.Has InViewMode =! true
        node |> has TargetPosition =! false
        // The entry is still on the undo stack, so the click was intercepted rather than applied
        // and then discarded.
        isDisabled undoButton =! false

    /// TODO: Fix the stale redo history that this test pins.
    ///
    /// `UndoRedo.handleDragEnd` decides whether a release was a real move — and so whether to
    /// discard the redo history — by looking for a `Selected` node that is not animating, using
    /// `Selected` as a proxy for "what was just dragged". `updateViewMode` clears the selection
    /// and runs earlier in the frame, so a drag released in the same frame as a mode toggle looks
    /// like no drag at all: the redo history survives a move that should have invalidated it, and
    /// a later Redo walks the node onto a discarded timeline. The fix is to discard the redo
    /// history at drag *start*, where the undo entry is pushed and the dragged nodes are still
    /// known, instead of inferring the dragged nodes at the end.
    [<Fact>]
    member _.``KNOWN BUG: a drag released as the mode toggles leaves stale redo history``() =
        // Leave View mode so dragging — and therefore undo history — is possible.
        buttonWithLabel "Move" |> handleClick
        runSystems world frameDelta

        let node =
            world.Spawn(PersonRef.Val Person.Empty, Position.Val zeroPosition, Selected.Tag())

        // Drag 0 -> 4 and release it cleanly, then undo. Wait out the undo animation, because
        // only a node that has stopped animating is eligible to be snapshotted again.
        dragNodeTo node 4.0
        handleDragEnd world |> ignore
        runSystems world frameDelta

        buttonWithLabel "Undo" |> handleClick
        runSystems world frameDelta
        runUntilSettled ()
        isDisabled (buttonWithLabel "Redo") =! false

        // Drag 0 -> 7, this time releasing in the same frame as a tap on the mode button.
        dragNodeTo node 7.0
        handleDragEnd world |> ignore
        buttonWithLabel "View" |> handleClick
        runSystems world frameDelta
        world.Has InViewMode =! true

        // The second drag replaced the undone one, so its redo entry should be gone. It is not.
        runSystems world frameDelta
        isDisabled (buttonWithLabel "Redo") =! false

        // Back in Move mode the stale entry is reachable, and sends the node to x = 4 — where the
        // *undone* drag had left it, a position the second drag was supposed to have superseded.
        buttonWithLabel "Move" |> handleClick
        runSystems world frameDelta
        buttonWithLabel "Redo" |> handleClick
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

        modeBtn |> handleClick
        runSystems world frameDelta
        world.Has InViewMode =! false

        let nodeEntity = world.Query(With PersonRef) |> Seq.head
        let originalPos = (nodeEntity |> get Position).Value
        let origX = originalPos.x

        handlePointerDown nodeEntity
        nodeEntity |> handleClick
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

        buttonWithLabel "Undo" |> handleClick
        runSystems world frameDelta

        // Undo animates the node back toward its pre-drag position via TargetPosition.
        (nodeEntity |> has TargetPosition) =! true
        (nodeEntity |> get TargetPosition).Value =! originalPos
