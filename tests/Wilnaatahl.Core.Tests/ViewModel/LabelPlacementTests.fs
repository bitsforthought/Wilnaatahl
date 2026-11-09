module Wilnaatahl.Tests.ViewModel.LabelPlacementTests

open Swensen.Unquote
open Xunit
open global.FsCheck
open global.FsCheck.FSharp
open Wilnaatahl.ViewModel

let private cssPixels value =
    LanguagePrimitives.FloatWithMeasure<cssPixel> value

let private worldUnits value =
    LanguagePrimitives.FloatWithMeasure<worldUnit> value

let private asPixelsPerWorldUnit value =
    LanguagePrimitives.FloatWithMeasure<cssPixel / worldUnit> value

let private nodeBounds left right top bottom : ProjectedNodeBounds = {
    Left = cssPixels left
    Right = cssPixels right
    Top = cssPixels top
    Bottom = cssPixels bottom
}

let private placement x y scale : LabelPlacement = { X = cssPixels x; Y = cssPixels y; Scale = scale }

let private projectedNode bounds viewSpaceDepth pixelsPerWorldUnitValue labelSize : ProjectedNode = {
    Bounds = bounds
    ViewSpaceDepth = worldUnits viewSpaceDepth
    PixelsPerWorldUnit = asPixelsPerWorldUnit pixelsPerWorldUnitValue
    LabelSize = labelSize
}

let private measuredLabel width height =
    Some { Width = cssPixels width; Height = cssPixels height }

let private layout node projectedNodes =
    node |> LabelPlacement.layoutLabel projectedNodes

let touchingNearerBounds =
    TheoryData<ProjectedNodeBounds>(
        nodeBounds -20.0 0.0 28.0 48.0,
        nodeBounds 20.0 40.0 28.0 48.0,
        nodeBounds 0.0 20.0 20.0 28.0,
        nodeBounds 0.0 20.0 48.0 68.0
    )

[<Fact>]
let ``place centres the label below the projected node`` () =
    let bounds = nodeBounds 120.0 280.0 200.0 360.0
    let pixelsPerWorldUnit = 2.0

    LabelPlacement.place bounds (asPixelsPerWorldUnit pixelsPerWorldUnit)
    =! placement 200.0 368.0 0.025

[<Fact>]
let ``place keeps the label below a node whose bottom reaches the canvas edge`` () =
    let bounds = nodeBounds 0.0 80.0 560.0 600.0
    let pixelsPerWorldUnit = 1.5
    let scaleRoundingTolerance = 1e-12

    let actual = LabelPlacement.place bounds (asPixelsPerWorldUnit pixelsPerWorldUnit)

    test
        <@
            actual.X = cssPixels 40.0
            && actual.Y = cssPixels 608.0
            && abs (actual.Scale - 0.01875) < scaleRoundingTolerance
        @>

[<Fact>]
let ``layoutLabel returns measured placement and visibility for a front node`` () =
    let bounds = nodeBounds 120.0 280.0 200.0 360.0
    let node = projectedNode bounds 2.0 2.0 (measuredLabel 80.0 20.0)

    layout node [| node |]
    =! { Placement = placement 200.0 368.0 0.025; Visible = true }

[<Fact>]
let ``layoutLabel uses scaled label dimensions for overlap`` () =
    let nearerBounds = nodeBounds 0.0 80.0 80.0 100.0
    let targetBounds = nodeBounds 0.0 80.0 0.0 80.0
    let nearer = projectedNode nearerBounds 2.0 2.0 None
    let target = projectedNode targetBounds 4.0 2.0 (measuredLabel 400.0 100.0)

    layout target [| nearer; target |]
    =! { Placement = placement 40.0 88.0 0.025; Visible = false }

[<Fact>]
let ``layoutLabel applies the nearer-node centre rule`` () =
    let nearer = projectedNode (nodeBounds 100.0 300.0 100.0 270.0) 2.0 80.0 None

    let target =
        projectedNode (nodeBounds 250.0 270.0 250.0 268.0) 4.0 80.0 (measuredLabel 20.0 20.0)

    layout target [| nearer; target |]
    =! { Placement = placement 260.0 276.0 1.0; Visible = false }

[<Fact>]
let ``layoutLabel lets an unmeasured node occlude a measured label`` () =
    let nearer = projectedNode (nodeBounds 100.0 200.0 100.0 200.0) 2.0 2.0 None

    let target =
        projectedNode (nodeBounds 150.0 250.0 150.0 250.0) 4.0 2.0 (measuredLabel 20.0 20.0)

    layout target [| nearer; target |]
    =! { Placement = placement 200.0 258.0 0.025; Visible = false }

[<Fact>]
let ``layoutLabel hides unmeasured and non-front labels`` () =
    let front =
        projectedNode (nodeBounds 0.0 40.0 0.0 40.0) 1.0 2.0 (measuredLabel 20.0 20.0)

    let atCamera =
        projectedNode (nodeBounds 50.0 90.0 0.0 40.0) 0.0 2.0 (measuredLabel 20.0 20.0)

    let behind =
        projectedNode (nodeBounds 100.0 140.0 0.0 40.0) System.Double.PositiveInfinity 2.0 (measuredLabel 20.0 20.0)

    let unmeasured = projectedNode (nodeBounds 150.0 190.0 0.0 40.0) 1.0 2.0 None

    let zeroSized =
        projectedNode (nodeBounds 200.0 240.0 0.0 40.0) 1.0 2.0 (measuredLabel 0.0 20.0)

    let zeroHeight =
        projectedNode (nodeBounds 250.0 290.0 0.0 40.0) 1.0 2.0 (measuredLabel 20.0 0.0)

    [| front; atCamera; behind; unmeasured; zeroSized; zeroHeight |]
    |> Array.map (fun node -> layout node [| node |] |> _.Visible)
    =! [| true; false; false; false; false; false |]

[<Fact>]
let ``layoutLabel hides a measured label when projection scale is zero`` () =
    let node =
        projectedNode (nodeBounds 0.0 40.0 0.0 40.0) 1.0 0.0 (measuredLabel 20.0 20.0)

    layout node [| node |]
    =! { Placement = placement 20.0 48.0 0.0; Visible = false }

[<Fact>]
let ``layoutLabel derives a visible layout from an empty projected-node snapshot`` () =
    let measuredFrontNode =
        projectedNode (nodeBounds 120.0 280.0 200.0 360.0) 2.0 2.0 (measuredLabel 80.0 20.0)

    LabelPlacement.layoutLabel [||] measuredFrontNode
    =! { Placement = placement 200.0 368.0 0.025; Visible = true }

[<Fact>]
let ``layoutLabel applies a new measurement on the next frame`` () =
    let nearer = projectedNode (nodeBounds 42.0 50.0 87.0 90.0) 2.0 2.0 None

    let targetBounds = nodeBounds 0.0 80.0 0.0 80.0
    let smallTarget = projectedNode targetBounds 4.0 2.0 (measuredLabel 20.0 20.0)
    let largeTarget = projectedNode targetBounds 4.0 2.0 (measuredLabel 400.0 100.0)

    layout smallTarget [| nearer; smallTarget |] |> _.Visible =! true
    layout largeTarget [| nearer; largeTarget |] |> _.Visible =! false

[<Fact>]
let ``layoutLabel does not hide a label when node depths are equal`` () =
    let nearer = projectedNode (nodeBounds 100.0 200.0 100.0 200.0) 2.0 2.0 None

    let equalDepthNode =
        projectedNode (nodeBounds 150.0 250.0 150.0 250.0) 2.0 2.0 (measuredLabel 20.0 20.0)

    layout equalDepthNode [| nearer; equalDepthNode |] |> _.Visible =! true

[<Theory>]
[<MemberData(nameof touchingNearerBounds)>]
let ``layoutLabel keeps a label visible when its rectangle only touches a nearer node``
    (nearerBounds: ProjectedNodeBounds)
    =
    let target =
        projectedNode (nodeBounds 0.0 20.0 0.0 20.0) 4.0 80.0 (measuredLabel 20.0 20.0)

    let nearer = projectedNode nearerBounds 2.0 80.0 None

    layout target [| nearer; target |]
    =! { Placement = placement 10.0 28.0 1.0; Visible = true }

[<Fact>]
let ``layoutLabel does not let a farther node hide a nearer label`` () =
    let target =
        projectedNode (nodeBounds 100.0 200.0 100.0 200.0) 2.0 2.0 (measuredLabel 20.0 20.0)

    let farther = projectedNode (nodeBounds 150.0 250.0 150.0 250.0) 4.0 2.0 None

    layout target [| target; farther |] |> _.Visible =! true

[<Fact>]
let ``layoutLabel makes a label visible again when the nearer node no longer overlaps it`` () =
    let target =
        projectedNode (nodeBounds 100.0 180.0 100.0 180.0) 4.0 2.0 (measuredLabel 4000.0 100.0)

    let nearerBounds = nodeBounds 100.0 200.0 100.0 200.0
    let nearer = projectedNode nearerBounds 2.0 2.0 None
    layout target [| nearer; target |] |> _.Visible =! false

    let movedNearer = projectedNode (nodeBounds 300.0 380.0 300.0 380.0) 2.0 2.0 None

    layout target [| movedNearer; target |] |> _.Visible =! true

[<FsCheck.Xunit.Property>]
let ``layoutLabel lets a nearer node hide a farther label`` () =
    let depthPairs =
        gen {
            let! nearerDepth = Gen.choose (1, 100)
            let! depthDifference = Gen.choose (1, 100)
            return float nearerDepth, float (nearerDepth + depthDifference)
        }

    Prop.forAll (Arb.fromGen depthPairs) (fun (nearerDepth, fartherDepth) ->
        let nearer = projectedNode (nodeBounds 100.0 200.0 100.0 200.0) nearerDepth 2.0 None

        let farther =
            projectedNode (nodeBounds 150.0 250.0 150.0 250.0) fartherDepth 2.0 (measuredLabel 20.0 20.0)

        layout farther [| nearer; farther |] |> _.Visible =! false)
