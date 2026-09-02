module Wilnaatahl.Systems.Runner

open Wilnaatahl.ECS
open Wilnaatahl.ECS.Tracking
open Wilnaatahl.Traits.Events
open Wilnaatahl.Traits.Intents
open Wilnaatahl.Systems.Animation
open Wilnaatahl.Systems.Dragging
open Wilnaatahl.Systems.FileCommands
open Wilnaatahl.Systems.Movement
open Wilnaatahl.Systems.Selection
open Wilnaatahl.Systems.UndoRedo
open Wilnaatahl.Systems.ViewMode

/// Exposes systems that are implemented in TypeScript so we can include them in runSystems.
[<AutoOpen>]
module private TypeScriptSystems =
#if FABLE_COMPILER
    open Fable.Core

    /// Calls the rendering system; Must be called on each frame.
    [<Import("render", "../../ecs/rendering.ts")>]
    let render: IWorld -> IWorld = nativeOnly

#else

    /// Unit test stub for rendering that does nothing.
    let render (world: IWorld) = world

#endif

/// Change tracker used to detect changing Positions by the Movement system.
/// This has to be global because otherwise they allocate in an unbounded fashion, which is very bad.
let private movementTracker = createChanged ()

/// Runs all systems in the correct order for a single frame.
let runSystems (world: IWorld) delta =
    // Proven ordering constraints:
    //   - animate before dragNodes: a grab during animation captures the post-animation position,
    //     and an animation that finishes on the grab frame completes before drag origin capture.
    //   - dragNodes before handleUndoRedo: a completed drag commits the same-frame command that
    //     undo/redo must see.
    //
    // Input is raised between frames. Resolve each target's EmitsIntent once before systems run;
    // every system shares this immutable list, and declaration mutations affect later snapshots.
    let intents = world |> derivedIntents

    world
    |> animate delta
    |> updateViewMode intents
    |> dragNodes
    |> selectNodes intents
    |> handleUndoRedo intents
    |> handleFileCommands intents
    |> move movementTracker
    |> render
    |> cleanupEvents
    |> ignore

    ()
