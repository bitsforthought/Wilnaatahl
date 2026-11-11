module Wilnaatahl.Tests.ViewModel.LabelPlacementTests

open Swensen.Unquote
open Xunit
open FsCheck
open FsCheck.FSharp
open FsCheck.Xunit
open Wilnaatahl.ViewModel

let touchingNearerBounds =
    TheoryData<ProjectedNodeBounds>(
        {
            Left = -20.0<cssPixel>
            Right = 0.0<cssPixel>
            Top = 28.0<cssPixel>
            Bottom = 48.0<cssPixel>
        },
        {
            Left = 20.0<cssPixel>
            Right = 40.0<cssPixel>
            Top = 28.0<cssPixel>
            Bottom = 48.0<cssPixel>
        },
        {
            Left = 0.0<cssPixel>
            Right = 20.0<cssPixel>
            Top = 20.0<cssPixel>
            Bottom = 28.0<cssPixel>
        },
        {
            Left = 0.0<cssPixel>
            Right = 20.0<cssPixel>
            Top = 48.0<cssPixel>
            Bottom = 68.0<cssPixel>
        }
    )

[<Fact>]
let ``place centres the label below the projected node`` () =
    let bounds: ProjectedNodeBounds = {
        Left = 120.0<cssPixel>
        Right = 280.0<cssPixel>
        Top = 200.0<cssPixel>
        Bottom = 360.0<cssPixel>
    }

    let pixelsPerWorldUnit = 2.0

    LabelPlacement.place bounds (pixelsPerWorldUnit * 1.0<cssPixel / worldUnit>)
    =! { X = 200.0<cssPixel>; Y = 368.0<cssPixel>; Scale = 0.025 }

[<Fact>]
let ``place keeps the label below a node whose bottom reaches the canvas edge`` () =
    let bounds: ProjectedNodeBounds = {
        Left = 0.0<cssPixel>
        Right = 80.0<cssPixel>
        Top = 560.0<cssPixel>
        Bottom = 600.0<cssPixel>
    }

    let pixelsPerWorldUnit = 1.5
    let scaleRoundingTolerance = 1e-12

    let actual =
        LabelPlacement.place bounds (pixelsPerWorldUnit * 1.0<cssPixel / worldUnit>)

    test
        <@
            actual.X = 40.0<cssPixel>
            && actual.Y = 608.0<cssPixel>
            && abs (actual.Scale - 0.01875) < scaleRoundingTolerance
        @>

[<Fact>]
let ``layoutLabel returns measured placement and visibility for a front node`` () =
    let node: ProjectedNode = {
        Bounds = {
            Left = 120.0<cssPixel>
            Right = 280.0<cssPixel>
            Top = 200.0<cssPixel>
            Bottom = 360.0<cssPixel>
        }
        ViewSpaceDepth = 2.0<worldUnit>
        PixelsPerWorldUnit = 2.0<cssPixel / worldUnit>
        LabelSize = Some { Width = 80.0<cssPixel>; Height = 20.0<cssPixel> }
    }

    node |> LabelPlacement.layoutLabel [| node |]
    =! {
           Placement = { X = 200.0<cssPixel>; Y = 368.0<cssPixel>; Scale = 0.025 }
           Visible = true
       }

[<Fact>]
let ``layoutLabel uses scaled label dimensions for overlap`` () =
    let nearer: ProjectedNode = {
        Bounds = {
            Left = 0.0<cssPixel>
            Right = 80.0<cssPixel>
            Top = 80.0<cssPixel>
            Bottom = 100.0<cssPixel>
        }
        ViewSpaceDepth = 2.0<worldUnit>
        PixelsPerWorldUnit = 2.0<cssPixel / worldUnit>
        LabelSize = None
    }

    let target: ProjectedNode = {
        Bounds = {
            Left = 0.0<cssPixel>
            Right = 80.0<cssPixel>
            Top = 0.0<cssPixel>
            Bottom = 80.0<cssPixel>
        }
        ViewSpaceDepth = 4.0<worldUnit>
        PixelsPerWorldUnit = 2.0<cssPixel / worldUnit>
        LabelSize = Some { Width = 400.0<cssPixel>; Height = 100.0<cssPixel> }
    }

    target |> LabelPlacement.layoutLabel [| nearer; target |]
    =! {
           Placement = { X = 40.0<cssPixel>; Y = 88.0<cssPixel>; Scale = 0.025 }
           Visible = false
       }

[<Fact>]
let ``layoutLabel applies the nearer-node centre rule`` () =
    let nearer: ProjectedNode = {
        Bounds = {
            Left = 100.0<cssPixel>
            Right = 300.0<cssPixel>
            Top = 100.0<cssPixel>
            Bottom = 270.0<cssPixel>
        }
        ViewSpaceDepth = 2.0<worldUnit>
        PixelsPerWorldUnit = 80.0<cssPixel / worldUnit>
        LabelSize = None
    }

    let target: ProjectedNode = {
        Bounds = {
            Left = 250.0<cssPixel>
            Right = 270.0<cssPixel>
            Top = 250.0<cssPixel>
            Bottom = 268.0<cssPixel>
        }
        ViewSpaceDepth = 4.0<worldUnit>
        PixelsPerWorldUnit = 80.0<cssPixel / worldUnit>
        LabelSize = Some { Width = 20.0<cssPixel>; Height = 20.0<cssPixel> }
    }

    target |> LabelPlacement.layoutLabel [| nearer; target |]
    =! {
           Placement = { X = 260.0<cssPixel>; Y = 276.0<cssPixel>; Scale = 1.0 }
           Visible = false
       }

[<Fact>]
let ``layoutLabel lets an unmeasured node occlude a measured label`` () =
    let nearer: ProjectedNode = {
        Bounds = {
            Left = 100.0<cssPixel>
            Right = 200.0<cssPixel>
            Top = 100.0<cssPixel>
            Bottom = 200.0<cssPixel>
        }
        ViewSpaceDepth = 2.0<worldUnit>
        PixelsPerWorldUnit = 2.0<cssPixel / worldUnit>
        LabelSize = None
    }

    let target: ProjectedNode = {
        Bounds = {
            Left = 150.0<cssPixel>
            Right = 250.0<cssPixel>
            Top = 150.0<cssPixel>
            Bottom = 250.0<cssPixel>
        }
        ViewSpaceDepth = 4.0<worldUnit>
        PixelsPerWorldUnit = 2.0<cssPixel / worldUnit>
        LabelSize = Some { Width = 20.0<cssPixel>; Height = 20.0<cssPixel> }
    }

    target |> LabelPlacement.layoutLabel [| nearer; target |]
    =! {
           Placement = { X = 200.0<cssPixel>; Y = 258.0<cssPixel>; Scale = 0.025 }
           Visible = false
       }

[<Fact>]
let ``layoutLabel hides unmeasured and non-front labels`` () =
    let front: ProjectedNode = {
        Bounds = {
            Left = 0.0<cssPixel>
            Right = 40.0<cssPixel>
            Top = 0.0<cssPixel>
            Bottom = 40.0<cssPixel>
        }
        ViewSpaceDepth = 1.0<worldUnit>
        PixelsPerWorldUnit = 2.0<cssPixel / worldUnit>
        LabelSize = Some { Width = 20.0<cssPixel>; Height = 20.0<cssPixel> }
    }

    let atCamera: ProjectedNode = {
        Bounds = {
            Left = 50.0<cssPixel>
            Right = 90.0<cssPixel>
            Top = 0.0<cssPixel>
            Bottom = 40.0<cssPixel>
        }
        ViewSpaceDepth = 0.0<worldUnit>
        PixelsPerWorldUnit = 2.0<cssPixel / worldUnit>
        LabelSize = Some { Width = 20.0<cssPixel>; Height = 20.0<cssPixel> }
    }

    let behind: ProjectedNode = {
        Bounds = {
            Left = 100.0<cssPixel>
            Right = 140.0<cssPixel>
            Top = 0.0<cssPixel>
            Bottom = 40.0<cssPixel>
        }
        ViewSpaceDepth = System.Double.PositiveInfinity * 1.0<worldUnit>
        PixelsPerWorldUnit = 2.0<cssPixel / worldUnit>
        LabelSize = Some { Width = 20.0<cssPixel>; Height = 20.0<cssPixel> }
    }

    let unmeasured: ProjectedNode = {
        Bounds = {
            Left = 150.0<cssPixel>
            Right = 190.0<cssPixel>
            Top = 0.0<cssPixel>
            Bottom = 40.0<cssPixel>
        }
        ViewSpaceDepth = 1.0<worldUnit>
        PixelsPerWorldUnit = 2.0<cssPixel / worldUnit>
        LabelSize = None
    }

    let zeroSized: ProjectedNode = {
        Bounds = {
            Left = 200.0<cssPixel>
            Right = 240.0<cssPixel>
            Top = 0.0<cssPixel>
            Bottom = 40.0<cssPixel>
        }
        ViewSpaceDepth = 1.0<worldUnit>
        PixelsPerWorldUnit = 2.0<cssPixel / worldUnit>
        LabelSize = Some { Width = 0.0<cssPixel>; Height = 20.0<cssPixel> }
    }

    let zeroHeight: ProjectedNode = {
        Bounds = {
            Left = 250.0<cssPixel>
            Right = 290.0<cssPixel>
            Top = 0.0<cssPixel>
            Bottom = 40.0<cssPixel>
        }
        ViewSpaceDepth = 1.0<worldUnit>
        PixelsPerWorldUnit = 2.0<cssPixel / worldUnit>
        LabelSize = Some { Width = 20.0<cssPixel>; Height = 0.0<cssPixel> }
    }

    [| front; atCamera; behind; unmeasured; zeroSized; zeroHeight |]
    |> Array.map (fun node -> node |> LabelPlacement.layoutLabel [| node |] |> _.Visible)
    =! [| true; false; false; false; false; false |]

[<Fact>]
let ``layoutLabel hides a measured label when projection scale is zero`` () =
    let node: ProjectedNode = {
        Bounds = {
            Left = 0.0<cssPixel>
            Right = 40.0<cssPixel>
            Top = 0.0<cssPixel>
            Bottom = 40.0<cssPixel>
        }
        ViewSpaceDepth = 1.0<worldUnit>
        PixelsPerWorldUnit = 0.0<cssPixel / worldUnit>
        LabelSize = Some { Width = 20.0<cssPixel>; Height = 20.0<cssPixel> }
    }

    node |> LabelPlacement.layoutLabel [| node |]
    =! {
           Placement = { X = 20.0<cssPixel>; Y = 48.0<cssPixel>; Scale = 0.0 }
           Visible = false
       }

[<Fact>]
let ``layoutLabel derives a visible layout from an empty projected-node snapshot`` () =
    let measuredFrontNode: ProjectedNode = {
        Bounds = {
            Left = 120.0<cssPixel>
            Right = 280.0<cssPixel>
            Top = 200.0<cssPixel>
            Bottom = 360.0<cssPixel>
        }
        ViewSpaceDepth = 2.0<worldUnit>
        PixelsPerWorldUnit = 2.0<cssPixel / worldUnit>
        LabelSize = Some { Width = 80.0<cssPixel>; Height = 20.0<cssPixel> }
    }

    measuredFrontNode |> LabelPlacement.layoutLabel [||]
    =! {
           Placement = { X = 200.0<cssPixel>; Y = 368.0<cssPixel>; Scale = 0.025 }
           Visible = true
       }

[<Fact>]
let ``layoutLabel applies a new measurement on the next frame`` () =
    let nearer: ProjectedNode = {
        Bounds = {
            Left = 42.0<cssPixel>
            Right = 50.0<cssPixel>
            Top = 87.0<cssPixel>
            Bottom = 90.0<cssPixel>
        }
        ViewSpaceDepth = 2.0<worldUnit>
        PixelsPerWorldUnit = 2.0<cssPixel / worldUnit>
        LabelSize = None
    }

    let targetBounds: ProjectedNodeBounds = {
        Left = 0.0<cssPixel>
        Right = 80.0<cssPixel>
        Top = 0.0<cssPixel>
        Bottom = 80.0<cssPixel>
    }

    let smallTarget: ProjectedNode = {
        Bounds = targetBounds
        ViewSpaceDepth = 4.0<worldUnit>
        PixelsPerWorldUnit = 2.0<cssPixel / worldUnit>
        LabelSize = Some { Width = 20.0<cssPixel>; Height = 20.0<cssPixel> }
    }

    let largeTarget: ProjectedNode = {
        Bounds = targetBounds
        ViewSpaceDepth = 4.0<worldUnit>
        PixelsPerWorldUnit = 2.0<cssPixel / worldUnit>
        LabelSize = Some { Width = 400.0<cssPixel>; Height = 100.0<cssPixel> }
    }

    smallTarget |> LabelPlacement.layoutLabel [| nearer; smallTarget |] |> _.Visible
    =! true

    largeTarget |> LabelPlacement.layoutLabel [| nearer; largeTarget |] |> _.Visible
    =! false

[<Fact>]
let ``layoutLabel does not hide a label when node depths are equal`` () =
    let nearer: ProjectedNode = {
        Bounds = {
            Left = 100.0<cssPixel>
            Right = 200.0<cssPixel>
            Top = 100.0<cssPixel>
            Bottom = 200.0<cssPixel>
        }
        ViewSpaceDepth = 2.0<worldUnit>
        PixelsPerWorldUnit = 2.0<cssPixel / worldUnit>
        LabelSize = None
    }

    let equalDepthNode: ProjectedNode = {
        Bounds = {
            Left = 150.0<cssPixel>
            Right = 250.0<cssPixel>
            Top = 150.0<cssPixel>
            Bottom = 250.0<cssPixel>
        }
        ViewSpaceDepth = 2.0<worldUnit>
        PixelsPerWorldUnit = 2.0<cssPixel / worldUnit>
        LabelSize = Some { Width = 20.0<cssPixel>; Height = 20.0<cssPixel> }
    }

    equalDepthNode
    |> LabelPlacement.layoutLabel [| nearer; equalDepthNode |]
    |> _.Visible
    =! true

[<Theory>]
[<MemberData(nameof touchingNearerBounds)>]
let ``layoutLabel keeps a label visible when its rectangle only touches a nearer node``
    (nearerBounds: ProjectedNodeBounds)
    =
    let target: ProjectedNode = {
        Bounds = {
            Left = 0.0<cssPixel>
            Right = 20.0<cssPixel>
            Top = 0.0<cssPixel>
            Bottom = 20.0<cssPixel>
        }
        ViewSpaceDepth = 4.0<worldUnit>
        PixelsPerWorldUnit = 80.0<cssPixel / worldUnit>
        LabelSize = Some { Width = 20.0<cssPixel>; Height = 20.0<cssPixel> }
    }

    let nearer: ProjectedNode = {
        Bounds = nearerBounds
        ViewSpaceDepth = 2.0<worldUnit>
        PixelsPerWorldUnit = 80.0<cssPixel / worldUnit>
        LabelSize = None
    }

    target |> LabelPlacement.layoutLabel [| nearer; target |]
    =! {
           Placement = { X = 10.0<cssPixel>; Y = 28.0<cssPixel>; Scale = 1.0 }
           Visible = true
       }

[<Fact>]
let ``layoutLabel does not let a farther node hide a nearer label`` () =
    let target: ProjectedNode = {
        Bounds = {
            Left = 100.0<cssPixel>
            Right = 200.0<cssPixel>
            Top = 100.0<cssPixel>
            Bottom = 200.0<cssPixel>
        }
        ViewSpaceDepth = 2.0<worldUnit>
        PixelsPerWorldUnit = 2.0<cssPixel / worldUnit>
        LabelSize = Some { Width = 20.0<cssPixel>; Height = 20.0<cssPixel> }
    }

    let farther: ProjectedNode = {
        Bounds = {
            Left = 150.0<cssPixel>
            Right = 250.0<cssPixel>
            Top = 150.0<cssPixel>
            Bottom = 250.0<cssPixel>
        }
        ViewSpaceDepth = 4.0<worldUnit>
        PixelsPerWorldUnit = 2.0<cssPixel / worldUnit>
        LabelSize = None
    }

    target |> LabelPlacement.layoutLabel [| target; farther |] |> _.Visible =! true

[<Fact>]
let ``layoutLabel makes a label visible again when the nearer node no longer overlaps it`` () =
    let target: ProjectedNode = {
        Bounds = {
            Left = 100.0<cssPixel>
            Right = 180.0<cssPixel>
            Top = 100.0<cssPixel>
            Bottom = 180.0<cssPixel>
        }
        ViewSpaceDepth = 4.0<worldUnit>
        PixelsPerWorldUnit = 2.0<cssPixel / worldUnit>
        LabelSize = Some { Width = 4000.0<cssPixel>; Height = 100.0<cssPixel> }
    }

    let nearer: ProjectedNode = {
        Bounds = {
            Left = 100.0<cssPixel>
            Right = 200.0<cssPixel>
            Top = 100.0<cssPixel>
            Bottom = 200.0<cssPixel>
        }
        ViewSpaceDepth = 2.0<worldUnit>
        PixelsPerWorldUnit = 2.0<cssPixel / worldUnit>
        LabelSize = None
    }

    target |> LabelPlacement.layoutLabel [| nearer; target |] |> _.Visible =! false

    let movedNearer: ProjectedNode = {
        Bounds = {
            Left = 300.0<cssPixel>
            Right = 380.0<cssPixel>
            Top = 300.0<cssPixel>
            Bottom = 380.0<cssPixel>
        }
        ViewSpaceDepth = 2.0<worldUnit>
        PixelsPerWorldUnit = 2.0<cssPixel / worldUnit>
        LabelSize = None
    }

    target |> LabelPlacement.layoutLabel [| movedNearer; target |] |> _.Visible
    =! true

[<Property>]
let ``layoutLabel lets a nearer node hide a farther label`` () =
    let depthPairs =
        gen {
            let! nearerDepth = Gen.choose (1, 100)
            let! depthDifference = Gen.choose (1, 100)
            return float nearerDepth, float (nearerDepth + depthDifference)
        }

    Prop.forAll (Arb.fromGen depthPairs) (fun (nearerDepth, fartherDepth) ->
        let nearer: ProjectedNode = {
            Bounds = {
                Left = 100.0<cssPixel>
                Right = 200.0<cssPixel>
                Top = 100.0<cssPixel>
                Bottom = 200.0<cssPixel>
            }
            ViewSpaceDepth = nearerDepth * 1.0<worldUnit>
            PixelsPerWorldUnit = 2.0<cssPixel / worldUnit>
            LabelSize = None
        }

        let farther: ProjectedNode = {
            Bounds = {
                Left = 150.0<cssPixel>
                Right = 250.0<cssPixel>
                Top = 150.0<cssPixel>
                Bottom = 250.0<cssPixel>
            }
            ViewSpaceDepth = fartherDepth * 1.0<worldUnit>
            PixelsPerWorldUnit = 2.0<cssPixel / worldUnit>
            LabelSize = Some { Width = 20.0<cssPixel>; Height = 20.0<cssPixel> }
        }

        farther |> LabelPlacement.layoutLabel [| nearer; farther |] |> _.Visible
        =! false)
