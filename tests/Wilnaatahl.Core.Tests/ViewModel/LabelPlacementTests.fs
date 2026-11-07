module Wilnaatahl.Tests.ViewModel.LabelPlacementTests

open Swensen.Unquote
open Xunit
open global.FsCheck
open global.FsCheck.FSharp
open Wilnaatahl.ViewModel

let private occlusionNode id bounds viewSpaceDepth labelRect : LabelOcclusionInput = {
    Id = id
    Bounds = bounds
    ViewSpaceDepth = viewSpaceDepth
    LabelRect = labelRect
}

let private renderInput id bounds viewSpaceDepth pixelsPerWorldUnit labelSize : LabelRenderInput = {
    Id = id
    Bounds = bounds
    ViewSpaceDepth = viewSpaceDepth
    PixelsPerWorldUnit = pixelsPerWorldUnit
    LabelSize = labelSize
}

let private measuredLabel width height = Some { Width = width; Height = height }

[<Fact>]
let ``place centres the label below the projected node`` () =
    let bounds: ProjectedNodeBounds = { Left = 120.0; Right = 280.0; Top = 200.0; Bottom = 360.0 }
    let pixelsPerWorldUnit = 2.0

    LabelPlacement.place bounds pixelsPerWorldUnit
    =! { X = 200.0; Y = 368.0; Scale = 0.025 }

[<Fact>]
let ``place keeps the label below a node whose bottom reaches the canvas edge`` () =
    let bounds: ProjectedNodeBounds = { Left = 0.0; Right = 80.0; Top = 560.0; Bottom = 600.0 }
    let pixelsPerWorldUnit = 1.5
    let scaleRoundingTolerance = 1e-12

    let actual = LabelPlacement.place bounds pixelsPerWorldUnit

    test
        <@
            actual.X = 40.0
            && actual.Y = 608.0
            && abs (actual.Scale - 0.01875) < scaleRoundingTolerance
        @>

[<Fact>]
let ``layoutLabels returns measured placement and visibility for a front node`` () =
    let bounds: ProjectedNodeBounds = { Left = 120.0; Right = 280.0; Top = 200.0; Bottom = 360.0 }
    let input = renderInput 1 bounds 2.0 2.0 (measuredLabel 80.0 20.0)

    LabelPlacement.layoutLabels [| input |]
    =! [|
        {
            Id = 1
            Placement = { X = 200.0; Y = 368.0; Scale = 0.025 }
            Visible = true
        }
    |]

[<Fact>]
let ``layoutLabels uses scaled label dimensions for overlap`` () =
    let nearerBounds: ProjectedNodeBounds = { Left = 45.0; Right = 50.0; Top = 87.0; Bottom = 90.0 }
    let targetBounds: ProjectedNodeBounds = { Left = 0.0; Right = 80.0; Top = 0.0; Bottom = 80.0 }
    let nearer = renderInput 11 nearerBounds 2.0 2.0 None
    let target = renderInput 12 targetBounds 4.0 2.0 (measuredLabel 400.0 100.0)

    LabelPlacement.layoutLabels [| nearer; target |]
    =! [|
        {
            Id = 11
            Placement = { X = 47.5; Y = 98.0; Scale = 0.025 }
            Visible = false
        }
        {
            Id = 12
            Placement = { X = 40.0; Y = 88.0; Scale = 0.025 }
            Visible = true
        }
    |]

[<Fact>]
let ``layoutLabels applies the nearer-node centre rule`` () =
    let nearer =
        renderInput 21 { Left = 100.0; Right = 220.0; Top = 100.0; Bottom = 220.0 } 2.0 2.0 (measuredLabel 20.0 20.0)

    let target =
        renderInput 22 { Left = 150.0; Right = 250.0; Top = 150.0; Bottom = 250.0 } 4.0 2.0 (measuredLabel 20.0 20.0)

    LabelPlacement.layoutLabels [| nearer; target |]
    =! [|
        {
            Id = 21
            Placement = { X = 160.0; Y = 228.0; Scale = 0.025 }
            Visible = true
        }
        {
            Id = 22
            Placement = { X = 200.0; Y = 258.0; Scale = 0.025 }
            Visible = false
        }
    |]

[<Fact>]
let ``layoutLabels lets an unmeasured node occlude a measured label`` () =
    let nearer =
        renderInput 31 { Left = 100.0; Right = 200.0; Top = 100.0; Bottom = 200.0 } 2.0 2.0 None

    let target =
        renderInput 32 { Left = 150.0; Right = 250.0; Top = 150.0; Bottom = 250.0 } 4.0 2.0 (measuredLabel 20.0 20.0)

    LabelPlacement.layoutLabels [| nearer; target |]
    =! [|
        {
            Id = 31
            Placement = { X = 150.0; Y = 208.0; Scale = 0.025 }
            Visible = false
        }
        {
            Id = 32
            Placement = { X = 200.0; Y = 258.0; Scale = 0.025 }
            Visible = false
        }
    |]

[<Fact>]
let ``layoutLabels hides unmeasured and non-front labels`` () =
    let front =
        renderInput 41 { Left = 0.0; Right = 40.0; Top = 0.0; Bottom = 40.0 } 1.0 2.0 (measuredLabel 20.0 20.0)

    let atCamera =
        renderInput 42 { Left = 50.0; Right = 90.0; Top = 0.0; Bottom = 40.0 } 0.0 2.0 (measuredLabel 20.0 20.0)

    let behind =
        renderInput
            43
            { Left = 100.0; Right = 140.0; Top = 0.0; Bottom = 40.0 }
            System.Double.PositiveInfinity
            2.0
            (measuredLabel 20.0 20.0)

    let unmeasured =
        renderInput 44 { Left = 150.0; Right = 190.0; Top = 0.0; Bottom = 40.0 } 1.0 2.0 None

    let zeroSized =
        renderInput 45 { Left = 200.0; Right = 240.0; Top = 0.0; Bottom = 40.0 } 1.0 2.0 (measuredLabel 0.0 20.0)

    let zeroHeight =
        renderInput 47 { Left = 250.0; Right = 290.0; Top = 0.0; Bottom = 40.0 } 1.0 2.0 (measuredLabel 20.0 0.0)

    LabelPlacement.layoutLabels [| front; atCamera; behind; unmeasured; zeroSized; zeroHeight |]
    |> Array.map _.Visible
    =! [| true; false; false; false; false; false |]

[<Fact>]
let ``layoutLabels hides a measured label when projection scale is zero`` () =
    let input =
        renderInput 46 { Left = 0.0; Right = 40.0; Top = 0.0; Bottom = 40.0 } 1.0 0.0 (measuredLabel 20.0 20.0)

    LabelPlacement.layoutLabels [| input |]
    =! [|
        {
            Id = 46
            Placement = { X = 20.0; Y = 48.0; Scale = 0.0 }
            Visible = false
        }
    |]

[<Fact>]
let ``layoutLabels preserves an empty frame`` () =
    LabelPlacement.layoutLabels [||] =! [||]

[<Fact>]
let ``layoutLabels applies a new measurement on the next frame`` () =
    let nearer =
        renderInput 51 { Left = 42.0; Right = 50.0; Top = 87.0; Bottom = 90.0 } 2.0 2.0 None

    let targetBounds: ProjectedNodeBounds = { Left = 0.0; Right = 80.0; Top = 0.0; Bottom = 80.0 }
    let smallTarget = renderInput 52 targetBounds 4.0 2.0 (measuredLabel 20.0 20.0)
    let largeTarget = renderInput 52 targetBounds 4.0 2.0 (measuredLabel 400.0 100.0)

    LabelPlacement.layoutLabels [| nearer; smallTarget |] |> Array.map _.Visible
    =! [| false; true |]

    LabelPlacement.layoutLabels [| nearer; largeTarget |] |> Array.map _.Visible
    =! [| false; false |]

[<Fact>]
let ``hiddenLabels hides a label whose measured rectangle overlaps a nearer node`` () =
    let nearerBounds: ProjectedNodeBounds = { Left = 100.0; Right = 200.0; Top = 100.0; Bottom = 200.0 }
    let fartherBounds: ProjectedNodeBounds = { Left = 0.0; Right = 80.0; Top = 0.0; Bottom = 80.0 }
    let nearerNode = occlusionNode 101 nearerBounds 2.0 None

    let fartherNode =
        occlusionNode 102 fartherBounds 4.0 (Some { Left = 150.0; Right = 250.0; Top = 150.0; Bottom = 250.0 })

    LabelPlacement.hiddenLabels [| nearerNode; fartherNode |] =! [| 102 |]

[<Fact>]
let ``hiddenLabels hides an unmeasured label when its node centre is inside a nearer node`` () =
    let nearerNode =
        occlusionNode 201 { Left = 100.0; Right = 200.0; Top = 100.0; Bottom = 200.0 } 2.0 None

    let fartherNode =
        occlusionNode 202 { Left = 150.0; Right = 250.0; Top = 150.0; Bottom = 250.0 } 4.0 None

    LabelPlacement.hiddenLabels [| nearerNode; fartherNode |] =! [| 202 |]

[<Fact>]
let ``hiddenLabels uses the farther node centre when the nearer bounds are larger`` () =
    let nearerNode =
        occlusionNode 251 { Left = 0.0; Right = 300.0; Top = 0.0; Bottom = 300.0 } 2.0 None

    let fartherNode =
        occlusionNode 252 { Left = 20.0; Right = 40.0; Top = 20.0; Bottom = 40.0 } 4.0 None

    LabelPlacement.hiddenLabels [| nearerNode; fartherNode |] =! [| 252 |]

[<Fact>]
let ``hiddenLabels does not hide a label when node depths are equal`` () =
    let nearerNode =
        occlusionNode 261 { Left = 100.0; Right = 200.0; Top = 100.0; Bottom = 200.0 } 2.0 None

    let equalDepthNode =
        occlusionNode
            262
            { Left = 150.0; Right = 250.0; Top = 150.0; Bottom = 250.0 }
            2.0
            (Some { Left = 150.0; Right = 250.0; Top = 150.0; Bottom = 250.0 })

    LabelPlacement.hiddenLabels [| nearerNode; equalDepthNode |] =! [||]

[<Fact>]
let ``hiddenLabels does not hide labels for touching edges or farther nodes`` () =
    let nearerNode =
        occlusionNode
            301
            { Left = 100.0; Right = 200.0; Top = 100.0; Bottom = 200.0 }
            2.0
            (Some { Left = 300.0; Right = 350.0; Top = 300.0; Bottom = 350.0 })

    let touchingLabelNode =
        occlusionNode
            302
            { Left = 0.0; Right = 50.0; Top = 0.0; Bottom = 50.0 }
            4.0
            (Some { Left = 200.0; Right = 260.0; Top = 120.0; Bottom = 180.0 })

    let fartherNode =
        occlusionNode 303 { Left = 310.0; Right = 360.0; Top = 310.0; Bottom = 360.0 } 4.0 None

    LabelPlacement.hiddenLabels [| nearerNode; touchingLabelNode; fartherNode |]
    =! [||]

[<Fact>]
let ``hiddenLabels treats every touching label edge as visible`` () =
    let nearerNode =
        occlusionNode 351 { Left = 100.0; Right = 200.0; Top = 100.0; Bottom = 200.0 } 2.0 None

    let touchingLabelNodes = [|
        occlusionNode
            352
            { Left = 0.0; Right = 50.0; Top = 0.0; Bottom = 50.0 }
            4.0
            (Some { Left = 200.0; Right = 240.0; Top = 120.0; Bottom = 180.0 })
        occlusionNode
            353
            { Left = 0.0; Right = 50.0; Top = 0.0; Bottom = 50.0 }
            4.0
            (Some { Left = 60.0; Right = 100.0; Top = 120.0; Bottom = 180.0 })
        occlusionNode
            354
            { Left = 0.0; Right = 50.0; Top = 0.0; Bottom = 50.0 }
            4.0
            (Some { Left = 120.0; Right = 180.0; Top = 200.0; Bottom = 240.0 })
        occlusionNode
            355
            { Left = 0.0; Right = 50.0; Top = 0.0; Bottom = 50.0 }
            4.0
            (Some { Left = 120.0; Right = 180.0; Top = 60.0; Bottom = 100.0 })
    |]

    touchingLabelNodes
    |> Array.append [| nearerNode |]
    |> LabelPlacement.hiddenLabels
    =! [||]

[<Fact>]
let ``hiddenLabels makes a label visible again when the nearer node no longer overlaps it`` () =
    let nearerBounds: ProjectedNodeBounds = { Left = 100.0; Right = 200.0; Top = 100.0; Bottom = 200.0 }
    let labelRect = Some { Left = 150.0; Right = 250.0; Top = 150.0; Bottom = 250.0 }
    let nearerNode = occlusionNode 401 nearerBounds 2.0 None

    let fartherNode =
        occlusionNode 402 { Left = 0.0; Right = 80.0; Top = 0.0; Bottom = 80.0 } 4.0 labelRect

    LabelPlacement.hiddenLabels [| nearerNode; fartherNode |] =! [| 402 |]

    let movedNearerNode =
        occlusionNode 401 { Left = 300.0; Right = 380.0; Top = 300.0; Bottom = 380.0 } 2.0 None

    LabelPlacement.hiddenLabels [| movedNearerNode; fartherNode |] =! [||]

[<Fact>]
let ``hiddenLabels returns entity ids so equal person occurrences remain distinct`` () =
    let nearerNode =
        occlusionNode 501 { Left = 100.0; Right = 200.0; Top = 100.0; Bottom = 200.0 } 2.0 None

    let firstOccurrence =
        occlusionNode 601 { Left = 150.0; Right = 250.0; Top = 150.0; Bottom = 250.0 } 4.0 None

    let secondOccurrence =
        occlusionNode 602 { Left = 300.0; Right = 400.0; Top = 300.0; Bottom = 400.0 } 4.0 None

    LabelPlacement.hiddenLabels [| nearerNode; firstOccurrence; secondOccurrence |]
    =! [| 601 |]

[<FsCheck.Xunit.Property>]
let ``hiddenLabels never lets a farther node hide a nearer label`` () =
    let depthPairs =
        gen {
            let! nearerDepth = Gen.choose (1, 100)
            let! depthDifference = Gen.choose (1, 100)
            return float nearerDepth, float (nearerDepth + depthDifference)
        }

    Prop.forAll (Arb.fromGen depthPairs) (fun (nearerDepth, fartherDepth) ->
        let nearerNode =
            occlusionNode 701 { Left = 100.0; Right = 200.0; Top = 100.0; Bottom = 200.0 } nearerDepth None

        let fartherNode =
            occlusionNode 702 { Left = 150.0; Right = 250.0; Top = 150.0; Bottom = 250.0 } fartherDepth None

        LabelPlacement.hiddenLabels [| nearerNode; fartherNode |] = [| 702 |])
