/// Fable-specific test infrastructure for running F# tests against real Koota.
/// Provides a portable =! operator, do-nothing xUnit attributes, and world creation/disposal.
module Wilnaatahl.Tests.ECS.FableTestInfra

open Fable.Core
open Fable.Core.JsInterop

let inline (=!) (actual: 'T) (expected: 'T) =
    if actual <> expected then
        failwithf "Assertion failed: expected %A but got %A" expected actual

/// Do-nothing replacements for xUnit attributes so test code compiles without conditional compilation.
type FactAttribute() =
    inherit System.Attribute()

type CollectionAttribute(_name: string) =
    inherit System.Attribute()

[<Import("createWorld", "koota")>]
let private createKootaWorld: unit -> obj = jsNative

[<Import("fromKootaWorld", "../../src/ecs/koota/kootaWrapper.ts")>]
let private fromKootaWorld: obj -> Wilnaatahl.ECS.IWorld = jsNative

[<Import("toKootaWorld", "../../src/ecs/koota/kootaWrapper.ts")>]
let private toKootaWorld: Wilnaatahl.ECS.IWorld -> obj = jsNative

let createTestWorld () = createKootaWorld () |> fromKootaWorld

let disposeTestWorld (world: Wilnaatahl.ECS.IWorld) =
    try
        let kootaWorld = toKootaWorld world
        emitJsExpr kootaWorld "$0.destroy()"
    with _ ->
        ()
