module Wilnaatahl.Tests.Systems.ViewModeTests

open System
open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.Model
open Wilnaatahl.Traits.Events
open Wilnaatahl.Traits.Intents
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

let private modeButtonIntent (world: IWorld) =
    (modeButton world |> get EmitsIntent).Value

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

    /// A click on the button in View mode must switch to Move, so the button's own declared
    /// intent has to name that target from the moment it is spawned.
    [<Fact>]
    member _.``spawnViewModeControls declares an intent to switch into Move mode``() =
        modeButtonIntent world =! [ ChangeMode Moving ]

    [<Fact>]
    member _.``app boots in View mode: button reads Move and the mode is Viewing``() =
        modeButtonLabel world =! "Move"
        world |> runWithIntents updateViewMode |> ignore
        world |> currentMode =! Viewing

    [<Fact>]
    member _.``clicking the mode button switches View to Move: label becomes View, mode becomes Moving``() =
        // Establish the boot mode.
        world |> runWithIntents updateViewMode |> ignore
        world |> currentMode =! Viewing

        modeButton world |> handleClick world
        world |> runWithIntents updateViewMode |> ignore

        modeButtonLabel world =! "View"
        world |> currentMode =! Moving

    /// The button's declared intent flips to name the opposite target only once the mode has
    /// actually changed, so the next click switches back.
    [<Fact>]
    member _.``clicking the mode button rewrites its declared intent to switch back``() =
        modeButton world |> handleClick world
        world |> runWithIntents updateViewMode |> ignore

        modeButtonIntent world =! [ ChangeMode Viewing ]

    [<Fact>]
    member _.``clicking the mode button twice returns to View mode``() =
        // View -> Move
        modeButton world |> handleClick world
        world |> runWithIntents updateViewMode |> ignore
        cleanupEvents world |> ignore
        modeButtonLabel world =! "View"
        world |> currentMode =! Moving

        // Move -> View
        modeButton world |> handleClick world
        world |> runWithIntents updateViewMode |> ignore
        modeButtonLabel world =! "Move"
        world |> currentMode =! Viewing

    /// Both clicks resolve from one pre-system snapshot, so the second target is already current
    /// and is a no-op rather than toggling the mode back.
    [<Fact>]
    member _.``clicking the mode button twice in the same frame switches mode only once``() =
        modeButton world |> handleClick world
        modeButton world |> handleClick world

        world |> runWithIntents updateViewMode |> ignore

        world |> currentMode =! Moving
        modeButtonLabel world =! "View"

    /// Selection owns clearing the selection on a mode switch — it reads the same `ChangeMode`
    /// intents `updateViewMode` reads — so `updateViewMode` on its own must never touch `Selected`,
    /// whether or not a switch happened this frame.
    [<Fact>]
    member _.``updateViewMode never writes Selected``() =
        let node = spawnSelectedNode world

        modeButton world |> handleClick world
        world |> runWithIntents updateViewMode |> ignore

        world |> currentMode =! Moving
        node |> has Selected =! true

    [<Fact>]
    member _.``a frame without a mode-button click leaves the mode and selection alone``() =
        let node = spawnSelectedNode world

        world |> runWithIntents updateViewMode |> ignore

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

    /// A mode switch leaves other input in the frame queue for the systems that own it.
    [<Fact>]
    member _.``Switching mode leaves every other click raised that frame``() =
        let node = spawnSelectedNode world
        let modeBtn = modeButton world

        let otherButton =
            world.Spawn(Button.Val {| sortOrder = 9; label = "Other"; disabled = false |})

        node |> handleClick world
        otherButton |> handleClick world
        modeBtn |> handleClick world

        world |> runWithIntents updateViewMode |> ignore

        world |> currentMode =! Moving
        world |> inputEvents |> Seq.contains (Clicked(node, Viewing)) =! true
        world |> inputEvents |> Seq.contains (Clicked(otherButton, Viewing)) =! true

    [<Fact>]
    member _.``a frame without a mode switch leaves other clicks untouched``() =
        let node = spawnSelectedNode world
        node |> handleClick world

        world |> runWithIntents updateViewMode |> ignore

        world |> inputEvents |> Seq.contains (Clicked(node, Viewing)) =! true
