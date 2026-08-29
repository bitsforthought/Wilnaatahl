module Wilnaatahl.Tests.Systems.ViewModeTests

open System
open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.Model
open Wilnaatahl.Traits.Events
open Wilnaatahl.Traits.PeopleTraits
open Wilnaatahl.Traits.SpaceTraits
open Wilnaatahl.Traits.ViewTraits
open Wilnaatahl.Systems.ViewMode
open Wilnaatahl.Tests.EcsTestSupport

/// Spawns a tree node that is already selected — every test here cares about what happens to
/// a selection, never about an unselected node.
let private spawnSelectedNode (world: IWorld) =
    world.Spawn(PersonRef.Val Person.Empty, Position.Val {| x = 0.0; y = 0.0; z = 0.0 |}, Selected.Tag())

let private modeButton (world: IWorld) =
    world.Query(With Button) |> Seq.exactlyOne

let private modeButtonLabel (world: IWorld) =
    (modeButton world |> get Button).Value.label

type Tests() =
    let ecs = new EcsWorld()
    let world = ecs.World
    let sortOrder, _ = spawnViewModeControls (0, world)

    interface IDisposable with
        member _.Dispose() = (ecs :> IDisposable).Dispose()

    [<Fact>]
    member _.``spawnViewModeControls creates a single mode button and advances sort order``() =
        sortOrder =! 1
        world.Query(With Button) |> Seq.length =! 1

    [<Fact>]
    member _.``spawnViewModeControls puts the world in View mode before any system runs``() =
        // Boot mode must be observable immediately after spawning, without waiting for the
        // first updateViewMode frame, so a consumer reading world state at startup sees View.
        world |> currentMode =! Viewing

    [<Fact>]
    member _.``app boots in View mode: button reads Move and the mode is Viewing``() =
        modeButtonLabel world =! "Move"
        updateViewMode world |> ignore
        world |> currentMode =! Viewing

    [<Fact>]
    member _.``clicking the mode button switches View to Move: label becomes View, mode becomes Moving``() =
        // Establish the boot mode.
        updateViewMode world |> ignore
        world |> currentMode =! Viewing

        modeButton world |> handleClick world
        updateViewMode world |> ignore

        modeButtonLabel world =! "View"
        world |> currentMode =! Moving

    [<Fact>]
    member _.``clicking the mode button twice returns to View mode``() =
        // View -> Move
        modeButton world |> handleClick world
        updateViewMode world |> ignore
        cleanupEvents world |> ignore
        modeButtonLabel world =! "View"
        world |> currentMode =! Moving

        // Move -> View
        modeButton world |> handleClick world
        updateViewMode world |> ignore
        modeButtonLabel world =! "Move"
        world |> currentMode =! Viewing

    [<Fact>]
    member _.``switching mode clears the selection``() =
        let node = spawnSelectedNode world
        updateViewMode world |> ignore

        // Switch to Move mode.
        modeButton world |> handleClick world
        updateViewMode world |> ignore

        node |> has Selected =! false

    [<Fact>]
    member _.``a frame without a mode-button click leaves the mode and selection alone``() =
        let node = spawnSelectedNode world

        updateViewMode world |> ignore

        world |> currentMode =! Viewing
        node |> has Selected =! true
        modeButtonLabel world =! "Move"

    /// The mode determines which controls are available, so ViewMode owns hiding them. It knows
    /// only the MoveModeOnly marker, never which system owns the button or what it does.
    [<Fact>]
    member _.``syncModalControls hides Move-mode-only controls in View mode and reveals them in Move mode``() =
        let modal =
            world.Spawn(Button.Val {| sortOrder = 9; label = "Modal"; disabled = false |}, MoveModeOnly.Tag())

        // Boot is View mode, so the control is hidden.
        syncModalControls world |> ignore
        modal |> has Hidden =! true

        // Move mode reveals it.
        world |> enterMode Moving
        syncModalControls world |> ignore
        modal |> has Hidden =! false

    [<Fact>]
    member _.``syncModalControls leaves controls without the marker alone``() =
        // The mode button itself is available in both modes, so it must never be hidden.
        syncModalControls world |> ignore
        modeButton world |> has Hidden =! false

    /// A tap landing in the same frame as a mode switch cannot have been meant in both the mode
    /// being left and the mode being entered, so the switch wins and the other click is dropped.
    /// This is what lets every later system read one consistent mode with no stale input.
    [<Fact>]
    member _.``switching mode discards every other click raised that frame``() =
        let node = spawnSelectedNode world
        let modeBtn = modeButton world

        let otherButton =
            world.Spawn(Button.Val {| sortOrder = 9; label = "Other"; disabled = false |})

        node |> handleClick world
        otherButton |> handleClick world
        modeBtn |> handleClick world

        updateViewMode world |> ignore

        world |> currentMode =! Moving
        node |> wasClicked world =! false
        otherButton |> wasClicked world =! false

    [<Fact>]
    member _.``a frame without a mode switch leaves other clicks untouched``() =
        let node = spawnSelectedNode world
        node |> handleClick world

        updateViewMode world |> ignore

        node |> wasClicked world =! true
