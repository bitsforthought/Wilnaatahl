namespace Wilnaatahl.ViewModel

open Wilnaatahl.Model.FamilyGraph
open Wilnaatahl.Model.Initial

// Functionality to initialize the core ViewModel data structures in an interface for easier consumption from TypeScript.
type IGraphViewFactory =
    abstract LoadGraph: unit -> FamilyGraph

type GraphViewFactory() =
    interface IGraphViewFactory with
        member _.LoadGraph() = createFamilyGraph peopleAndParents
