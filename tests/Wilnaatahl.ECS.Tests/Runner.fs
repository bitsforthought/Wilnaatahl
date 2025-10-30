module Wilnaatahl.Tests.ECS.Runner

#if FABLE_COMPILER
open Fable.Core
open Fable.Core.JsInterop
open Wilnaatahl.Tests.ECS.FableTestInfra

let private runTest (name: string) (test: unit -> unit) =
    try
        test ()
        true, name, ""
    with ex ->
        false, name, ex.Message

/// Fable encodes non-identifier characters in member names as $XXXX where XXXX is
/// the Unicode code point in hex. Decode them back to the original characters.
let private decodeName (key: string) (prefix: string) =
    System.Text.RegularExpressions.Regex.Replace(
        key.Replace(prefix, ""),
        @"\$([0-9A-Fa-f]{4})",
        fun m -> string (char (System.Convert.ToInt32(m.Groups[1].Value, 16)))
    )

let private runTestClass
    (testModule: obj)
    (prefix: string)
    (ctorName: string)
    (passed: int ref)
    (failed: int ref)
    =
    let keys: string[] = JS.Constructors.Object.keys testModule |> unbox

    for key in keys do
        if key.StartsWith(prefix) then
            let displayName = decodeName key prefix
            let fn: obj = testModule?(key)

            if jsTypeof fn = "function" then
                // Create a fresh instance per test (mirrors xUnit behavior)
                let freshTests: obj = emitJsExpr (testModule, ctorName) "$0[$1]()"
                let testFn () = emitJsExpr (fn, freshTests) "$0($1)"

                let ok, testName, msg = runTest displayName testFn

                // Destroy the world to release the world ID slot
                emitJsExpr freshTests "$0.world" |> disposeTestWorld

                if ok then
                    passed.Value <- passed.Value + 1
                    printfn "  PASS: %s" testName
                else
                    failed.Value <- failed.Value + 1
                    printfn "  FAIL: %s — %s" testName msg

[<Import("*", "./out/ECS/TraitTests.js")>]
let private traitTestModule: obj = jsNative

[<Import("*", "./out/ECS/EntityTests.js")>]
let private entityTestModule: obj = jsNative

[<Import("*", "./out/ECS/QueryTests.js")>]
let private queryTestModule: obj = jsNative

[<Import("*", "./out/ECS/RelationTests.js")>]
let private relationTestModule: obj = jsNative

[<Import("*", "./out/ECS/WorldTests.js")>]
let private worldTestModule: obj = jsNative

[<Import("*", "./out/ECS/TrackingTests.js")>]
let private trackingTestModule: obj = jsNative

[<EntryPoint>]
let main _ =
    let passed = ref 0
    let failed = ref 0

    printfn "=== Koota ECS Tests (Fable) ==="

    printfn "\n--- TraitTests ---"
    runTestClass traitTestModule "TraitTests__" "TraitTests_$ctor" passed failed

    printfn "\n--- EntityTests ---"
    runTestClass entityTestModule "EntityTests__" "EntityTests_$ctor" passed failed

    printfn "\n--- QueryTests ---"
    runTestClass queryTestModule "QueryTests__" "QueryTests_$ctor" passed failed

    printfn "\n--- RelationTests ---"
    runTestClass relationTestModule "RelationTests__" "RelationTests_$ctor" passed failed

    printfn "\n--- WorldTests ---"
    runTestClass worldTestModule "WorldTests__" "WorldTests_$ctor" passed failed

    printfn "\n--- TrackingTests ---"
    runTestClass trackingTestModule "TrackingTests__" "TrackingTests_$ctor" passed failed

    printfn "\n=== Results: %d passed, %d failed ===" passed.Value failed.Value
    if failed.Value > 0 then 1 else 0
#endif