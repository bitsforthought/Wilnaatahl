module Wilnaatahl.Tests.ViewModel.GraphViewFactoryTests

open Xunit
open Swensen.Unquote
open Wilnaatahl.ViewModel
open Wilnaatahl.Model
open Wilnaatahl.Model.FamilyGraph

[<Fact>]
let ``LoadGraph returns graph with expected people count`` () =
    let factory = GraphViewFactory() :> IGraphViewFactory
    let graph = factory.LoadGraph()
    graph |> allPeople |> Seq.length =! 5

[<Fact>]
let ``LoadGraph returns graph with expected huwilp`` () =
    let factory = GraphViewFactory() :> IGraphViewFactory
    let graph = factory.LoadGraph()
    graph |> huwilp =! Set.ofList [ WilpName "H" ]
