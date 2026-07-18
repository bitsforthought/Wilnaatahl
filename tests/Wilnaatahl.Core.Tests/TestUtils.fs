module Wilnaatahl.Tests.TestUtils

open System.Diagnostics
open System.Threading
open Wilnaatahl.Model
open Wilnaatahl.ViewModel.LayoutBox

/// Pauses a test run to allow attaching a debugger to the test host.
let debugBreak () =
    if not Debugger.IsAttached then
        let pid = Process.GetCurrentProcess().Id
        printfn $"Please attach a debugger to process ID: {pid}"

    while not Debugger.IsAttached do
        Thread.Sleep 100

    Debugger.Break()

/// Exercise LayoutBox.visit by calculating the offsets of every node in the tree,
/// keyed by the PersonId each node renders. (Distinct nodes of the same person —
/// an outside spouse's per-marriage partner nodes — collapse to one key here; the
/// system-level layout tests distinguish them by NodeKey instead.)
let setPositions (initialPosition, rootBox) =
    let visitLeaf pos (nodeKey: NodeKey) offset =
        (nodeKey.PersonId, pos + offset) |> Seq.singleton

    let visitComposite pos results =
        results
        |> Seq.concat
        |> Seq.map (fun (personId, offset) -> personId, pos + offset)

    rootBox |> visit visitLeaf visitComposite initialPosition
