namespace Wilnaatahl.Tests.ECS

open System
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ECS.Trait
open Wilnaatahl.Tests.ECS.TestInfra

#if FABLE_COMPILER
open Wilnaatahl.Tests.ECS.FableTestInfra
#else
open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS.Mocks
#endif

type private MyClass() =
    member val Y = 0 with get, set

[<Collection("ECS")>]
type TraitTests() =
    let wrapper = new TestWorldWrapper()
    let world = wrapper.World

    interface IDisposable with
        member _.Dispose() = (wrapper :> IDisposable).Dispose()

    [<Fact>]
    member _.``Can create and use tag trait``() =
        let tag = tagTrait ()
        tag.IsTag =! true

    [<Fact>]
    member _.``Can create and use value trait``() =
        let Name = valueTrait {| name = "foo" |}
        Name.IsTag =! false
        let entity = world.Spawn [| Name.Val {| name = "bar" |} |]
        entity |> get Name =! Some {| name = "bar" |}

    [<Fact>]
    member _.``Only tag traits have IsTag set to true``() =
        let tag = tagTrait ()
        let value = valueTrait {| x = 0 |}
        let mutableValue = mutableTrait {| X = 0 |} { X = 0 }
        let refTrait = refTrait MyClass

        tag.IsTag =! true
        value.IsTag =! false
        mutableValue.IsTag =! false
        refTrait.IsTag =! false

    [<Fact>]
    member _.``Can add refTrait to entity``() =
        let Ref = refTrait MyClass
        let entity = world.Spawn [||]
        entity |> add Ref
        entity |> has Ref =! true

    [<Fact>]
    member _.``Adding a refTrait without a value calls the factory to build one``() =
        let Ref = refTrait (fun () -> ResizeArray [ 7 ])
        let entity = world.Spawn [||]
        entity |> add Ref
        entity |> get Ref |> Option.map List.ofSeq =! Some [ 7 ]

    [<Fact>]
    member _.``Each entity adding a refTrait without a value gets its own value``() =
        let Ref = refTrait (fun () -> ResizeArray<int>())
        let first = world.Spawn [||]
        let second = world.Spawn [||]
        first |> add Ref
        second |> add Ref

        first |> get Ref |> Option.iter (fun list -> list.Add 7)

        second |> get Ref |> Option.map List.ofSeq =! Some []
