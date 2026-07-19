namespace Wilnaatahl.Persistence

open Wilnaatahl.Model.FamilyGraph
open Wilnaatahl.Model.Initial

/// Successful end-to-end import: a constructed FamilyGraph plus any non-fatal
/// warnings emitted by the parser/transformer.
type ImportSuccess = { Graph: FamilyGraph; Warnings: ImportWarning list }

/// Top-level service used by the React UI for loading family graph data: builds
/// the built-in sample graph for the initial view (`loadSampleGraph`) and imports
/// user JSON files into a ready-to-render FamilyGraph (`importJsonText`).
module ImportService =

    /// Builds a FamilyGraph from the hardcoded sample data in `Initial`. Used
    /// as a demo/exploration affordance until the user imports their own data.
    let loadSampleGraph () : FamilyGraph =
        createFamilyGraph peopleAndParents couples nameHoldings

    /// End-to-end import: parses JSON, transforms, and constructs the FamilyGraph.
    /// Returns either an ImportSuccess (graph + warnings) or an ImportError
    /// describing why the import could not complete.
    let importJsonText (json: string) : Result<ImportSuccess, ImportError> =
        Transform.fromJson json
        |> Result.map (fun result -> {
            Graph = createFamilyGraph result.PeopleAndCoupleIds result.Couples result.NameHoldings
            Warnings = result.Warnings
        })
