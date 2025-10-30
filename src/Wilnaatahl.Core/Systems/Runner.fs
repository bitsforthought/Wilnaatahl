module Wilnaatahl.Systems.Runner

open Wilnaatahl.ECS
open Wilnaatahl.ECS.Tracking
open Wilnaatahl.Traits.Events
open Wilnaatahl.Systems.Animation
open Wilnaatahl.Systems.Dragging
open Wilnaatahl.Systems.Movement
open Wilnaatahl.Systems.Selection
open Wilnaatahl.Systems.UndoRedo

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
    world
    |> animate delta
    |> dragNodes
    |> handleUndoRedo
    |> selectNodes
    |> move movementTracker
    |> render
    |> cleanupEvents
    |> ignore

    ()
