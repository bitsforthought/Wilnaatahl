module Wilnaatahl.Tests.ViewModel.PaletteTests

open Xunit
open FsCheck
open FsCheck.FSharp
open FsCheck.Xunit
open Swensen.Unquote
open Wilnaatahl.Model
open Wilnaatahl.ViewModel

let private srgb red green blue : Palette.SrgbColour = { Red = red; Green = green; Blue = blue }

let private wilp pdeek name = { Name = WilpName name; Pdeek = pdeek }

let private personWith kinship = { Person.Empty with Id = PersonId 0; Kinship = kinship }

let private copperBytes = srgb 0x3Euy 0xA3uy 0x8Cuy
let private blackBytes = srgb 0x00uy 0x00uy 0x00uy

// ---- djb2Hash -------------------------------------------------------------

[<Fact>]
let ``djb2Hash of empty string is the seed value 5381`` () = Palette.djb2Hash "" =! 5381u

[<Fact>]
let ``djb2Hash distinguishes case (a vs A)`` () =
    Palette.djb2Hash "a" =! 177670u
    Palette.djb2Hash "A" =! 177638u

[<Fact>]
let ``djb2Hash advances the running hash by (hash * 33) + charCode for each character`` () =
    // The defining property of djb2: appending a character `c` to `s` produces a hash
    // equal to applying one more step `nextHash = hash * 33 + int c` to `hash s`.
    // (Multiplication by 33 is implemented as `(hash <<< 5) + hash`, so the step is
    // intentionally written below in those primitive terms to match the algorithm.)
    let baseString = "a"
    let nextChar = 'b'
    let baseHash = Palette.djb2Hash baseString
    let advancedHash = uint32 ((int baseHash <<< 5) + int baseHash + int nextChar)
    Palette.djb2Hash (baseString + string nextChar) =! advancedHash

[<Fact>]
let ``djb2Hash produces deterministic but distinct values for distinct strings`` () =
    Palette.djb2Hash "Giskaast" =! 2829097596u
    Palette.djb2Hash "Giskaast" <>! Palette.djb2Hash "Ganeda"

[<Fact>]
let ``djb2Hash wraps around 32-bit signed bounds for long inputs`` () =
    // Long inputs force the inner accumulator past Int32.MaxValue. The implementation
    // performs its arithmetic in 32-bit signed space so the value wraps around rather
    // than growing without bound, keeping the spread of outputs uniform.
    Palette.djb2Hash "A_long_overflowing_string_to_force_wraparound_around_int32_bounds"
    =! 267330159u

// ---- lightnessOffsetFromHash ----------------------------------------------
//
// Maps a djb2 hash to a per-Wilp lightness offset within the wobble range,
// skipping a small band around zero so per-Wilp shades never collapse onto
// the Pdeek base colour reserved for `unknownWilpColourForPdeek`. The
// endpoints (hash 0 and hash hashBuckets-1) hit ±wobble exactly.

[<Fact>]
let ``lightnessOffsetFromHash maps hash 0 to negative wobble`` () =
    Palette.lightnessOffsetFromHash 0u =! -0.12

[<Fact>]
let ``lightnessOffsetFromHash maps hash 999 to positive wobble`` () =
    Palette.lightnessOffsetFromHash 999u =! 0.12

[<Fact>]
let ``lightnessOffsetFromHash uses hash mod 1000 (1000u behaves like 0u, mapping to negative wobble)`` () =
    Palette.lightnessOffsetFromHash 1000u =! -0.12

[<Fact>]
let ``lightnessOffsetFromHash always returns an offset of at least the minimum magnitude`` () =
    // The middle band around zero is reserved for unknownWilpColourForPdeek so
    // per-Wilp shades never collapse onto the Pdeek base. Asserted across the
    // full hash bucket range to catch any off-by-one in the band-skip arithmetic.
    let minMagnitude = 0.02

    let violating =
        [ 0u .. 999u ]
        |> List.tryFind (fun h -> abs (Palette.lightnessOffsetFromHash h) < minMagnitude)

    violating =! None

[<Fact>]
let ``lightnessOffsetFromHash maps the bucket just below the midpoint to just below -wilpMinLightnessOffset`` () =
    // hash 499 → bucketFraction = 499/999, mapped to the negative-side boundary
    // of the band-skip — just outside `-wilpMinLightnessOffset = -0.02`. Pins
    // the boundary value of the lower half so an off-by-one in the band-skip
    // arithmetic is caught here.
    let actual = Palette.lightnessOffsetFromHash 499u
    let expected = -0.02 - (1.0 / 999.0) * (0.12 - 0.02)
    abs (actual - expected) <! 1e-12

[<Fact>]
let ``lightnessOffsetFromHash maps the midpoint bucket to just above wilpMinLightnessOffset`` () =
    // hash 500 → bucketFraction = 500/999, mapped to the positive-side boundary
    // of the band-skip — just outside `+wilpMinLightnessOffset = +0.02`. Mirror
    // of the test above, pinning the boundary value of the upper half.
    let actual = Palette.lightnessOffsetFromHash 500u
    let expected = 0.02 + (1.0 / 999.0) * (0.12 - 0.02)
    abs (actual - expected) <! 1e-12

[<Property>]
let ``lightnessOffsetFromHash stays within the wobble range and outside the zero band`` (hash: uint32) =
    // wobble = Palette.wilpLightnessWobble, minMagnitude = Palette.wilpMinLightnessOffset
    // (both private). For any hash the offset lands in
    // [-wobble, -minMagnitude] ∪ [minMagnitude, wobble]; this also exercises the
    // hash-mod-1000 wraparound that the bucket-range facts above don't reach.
    let wobble = 0.12
    let minMagnitude = 0.02
    let comparisonTolerance = 1e-9
    let offset = Palette.lightnessOffsetFromHash hash

    abs offset <= wobble + comparisonTolerance
    && abs offset >= minMagnitude - comparisonTolerance

// ---- oklchToSrgb ----------------------------------------------------------
//
// Converts an Oklch triple to an sRGB byte triple via the Ottosson OKLab transform,
// linear-sRGB clamping for out-of-gamut handling, and the standard sRGB gamma encode.

[<Fact>]
let ``oklchToSrgb maps the Giskaast vermillion base to the expected bytes`` () =
    Palette.oklchToSrgb { Lightness = 0.63; Chroma = 0.18; Hue = 41.0 }
    =! srgb 0xDFuy 0x59uy 0x1Duy

[<Fact>]
let ``oklchToSrgb maps the Ganeda bluish-green base to the expected bytes`` () =
    Palette.oklchToSrgb { Lightness = 0.63; Chroma = 0.13; Hue = 161.0 }
    =! srgb 0x24uy 0xA1uy 0x6Fuy

[<Fact>]
let ``oklchToSrgb maps the LaxSkiik yellow base to the expected bytes`` () =
    Palette.oklchToSrgb { Lightness = 0.89; Chroma = 0.18; Hue = 101.0 }
    =! srgb 0xF5uy 0xDDuy 0x21uy

[<Fact>]
let ``oklchToSrgb maps the LaxGibuu sky blue base to the expected bytes`` () =
    Palette.oklchToSrgb { Lightness = 0.73; Chroma = 0.12; Hue = 235.0 }
    =! srgb 0x50uy 0xB3uy 0xE8uy

[<Fact>]
let ``oklchToSrgb maps the unaffiliated ivory to the expected bytes`` () =
    Palette.oklchToSrgb { Lightness = 0.90; Chroma = 0.04; Hue = 85.0 }
    =! srgb 0xEAuy 0xDDuy 0xC1uy

[<Fact>]
let ``oklchToSrgb maps Lightness=1 Chroma=0 to pure white`` () =
    Palette.oklchToSrgb { Lightness = 1.0; Chroma = 0.0; Hue = 0.0 }
    =! srgb 0xFFuy 0xFFuy 0xFFuy

[<Fact>]
let ``oklchToSrgb maps Lightness=0 Chroma=0 to pure black`` () =
    Palette.oklchToSrgb { Lightness = 0.0; Chroma = 0.0; Hue = 0.0 }
    =! srgb 0x00uy 0x00uy 0x00uy

[<Fact>]
let ``oklchToSrgb maps a chroma-zero colour to a neutral grey (R = G = B)`` () =
    let result = Palette.oklchToSrgb { Lightness = 0.5; Chroma = 0.0; Hue = 0.0 }
    result.Red =! result.Green
    result.Green =! result.Blue
    result.Red =! 0x63uy

[<Fact>]
let ``oklchToSrgb clamps out-of-gamut linear-sRGB components into [0, 255]`` () =
    // Very high chroma at moderate lightness produces negative green/blue and a red
    // greater than 1 in linear-sRGB. The implementation must clamp before gamma encode
    // so the bytes stay valid.
    Palette.oklchToSrgb { Lightness = 0.5; Chroma = 0.40; Hue = 30.0 }
    =! srgb 0xFDuy 0x00uy 0x00uy

// ---- baseColourForPdeek + unaffiliatedColour ------------------------------
//
// Each Pdeek (Clan) has a base Oklch colour drawn from the Okabe-Ito CVD-safe
// categorical palette. Tests are exhaustive over the four Pdeek cases so that
// adding a new case to the discriminated union is caught here as well as by the
// match expression in Palette.fs. The unaffiliated colour is a soft warm ivory
// kept light enough to remain distinguishable from every Pdeek base under all
// common forms of colour-vision deficiency.

[<Fact>]
let ``baseColourForPdeek returns the Okabe-Ito vermillion for Giskaast`` () =
    Palette.baseColourForPdeek Giskaast
    =! { Lightness = 0.63; Chroma = 0.18; Hue = 41.0 }

[<Fact>]
let ``baseColourForPdeek returns the Okabe-Ito bluish-green for Ganeda`` () =
    Palette.baseColourForPdeek Ganeda
    =! { Lightness = 0.63; Chroma = 0.13; Hue = 161.0 }

[<Fact>]
let ``baseColourForPdeek returns the Okabe-Ito yellow for LaxSkiik`` () =
    Palette.baseColourForPdeek LaxSkiik
    =! { Lightness = 0.89; Chroma = 0.18; Hue = 101.0 }

[<Fact>]
let ``baseColourForPdeek returns a light Okabe-Ito sky blue for LaxGibuu`` () =
    Palette.baseColourForPdeek LaxGibuu
    =! { Lightness = 0.73; Chroma = 0.12; Hue = 235.0 }

[<Fact>]
let ``unaffiliatedColour is the expected warm ivory bytes`` () =
    Palette.unaffiliatedColour =! srgb 0xEAuy 0xDDuy 0xC1uy

// ---- unknownWilpColourForPdeek -------------------------------------------
//
// For a Person whose Pdeek (Clan) is known but whose specific Wilp is not,
// the displayed colour is the Pdeek's base Oklch converted directly to sRGB
// with no per-Wilp lightness wobble. The result is the "centre" of the shade
// family that the Pdeek's known huwilp occupy, so an UnknownWilp node sits
// visually adjacent to its Pdeek's known huwilp.

[<Fact>]
let ``unknownWilpColourForPdeek produces the expected bytes for all four Pdeek`` () =
    Palette.unknownWilpColourForPdeek Giskaast =! srgb 0xDFuy 0x59uy 0x1Duy
    Palette.unknownWilpColourForPdeek Ganeda =! srgb 0x24uy 0xA1uy 0x6Fuy
    Palette.unknownWilpColourForPdeek LaxSkiik =! srgb 0xF5uy 0xDDuy 0x21uy
    Palette.unknownWilpColourForPdeek LaxGibuu =! srgb 0x50uy 0xB3uy 0xE8uy

// ---- colourForWilp --------------------------------------------------------
//
// Composes the Pdeek base, the per-Wilp lightness offset (from a hash of the
// Wilp name), and the Oklch->sRGB conversion into a single byte triple. A
// regression in any of the upstream pieces shows up here too.

[<Fact>]
let ``colourForWilp produces the expected bytes for the four Initial huwilp`` () =
    Palette.colourForWilp (wilp Giskaast "A") =! srgb 0xEFuy 0x68uy 0x31uy
    Palette.colourForWilp (wilp Ganeda "B") =! srgb 0x39uy 0xB0uy 0x7Duy
    Palette.colourForWilp (wilp LaxSkiik "C") =! srgb 0xFFuy 0xEDuy 0x3Euy
    Palette.colourForWilp (wilp LaxGibuu "D") =! srgb 0x60uy 0xC3uy 0xF8uy

[<Fact>]
let ``colourForWilp is deterministic (same Wilp -> same bytes)`` () =
    let w = wilp Giskaast "A"
    Palette.colourForWilp w =! Palette.colourForWilp w

[<Fact>]
let ``colourForWilp produces different shades for different names within the same Pdeek`` () =
    let a = Palette.colourForWilp (wilp Giskaast "A")
    let other = Palette.colourForWilp (wilp Giskaast "Different")
    a <>! other
    // Pin the specific bytes for the second name so this isn't just a "hash collisions
    // are unlikely" probabilistic test.
    other =! srgb 0xCAuy 0x46uy 0x00uy

// ---- nodePaint ------------------------------------------------------------
//
// `nodePaint person isSelected` returns the colour, the emissive colour, and the
// emissive intensity to apply to a tree node. Selected nodes use a copper colour
// with strong emissive glow; unselected nodes use the per-Wilp colour (or the
// unaffiliated ivory) and no emissive glow.

[<Fact>]
let ``nodePaint of a selected affiliated person paints copper with strong emissive glow`` () =
    let person = personWith (Wilp(wilp Giskaast "A"))

    Palette.nodePaint person true
    =! {
           Colour = copperBytes
           Emissive = copperBytes
           EmissiveIntensity = 0.8
       }

[<Fact>]
let ``nodePaint of a selected unaffiliated person also paints copper`` () =
    // Selection wins over Wilp affiliation: no matter what the Person's Kinship is,
    // a selected node gets the same copper highlight.
    let person = personWith NoneProvided

    Palette.nodePaint person true
    =! {
           Colour = copperBytes
           Emissive = copperBytes
           EmissiveIntensity = 0.8
       }

[<Fact>]
let ``nodePaint of an unselected affiliated person uses colourForWilp with no emissive`` () =
    let w = wilp Giskaast "A"
    let person = personWith (Wilp w)

    Palette.nodePaint person false
    =! {
           Colour = Palette.colourForWilp w
           Emissive = blackBytes
           EmissiveIntensity = 0.0
       }

[<Fact>]
let ``nodePaint of an unselected unaffiliated person uses ivory with no emissive`` () =
    let person = personWith NoneProvided

    Palette.nodePaint person false
    =! {
           Colour = Palette.unaffiliatedColour
           Emissive = blackBytes
           EmissiveIntensity = 0.0
       }

[<Fact>]
let ``nodePaint of an unselected UnknownWilp person uses the Pdeek base colour with no emissive`` () =
    let person = personWith (UnknownWilp Ganeda)

    Palette.nodePaint person false
    =! {
           Colour = Palette.unknownWilpColourForPdeek Ganeda
           Emissive = blackBytes
           EmissiveIntensity = 0.0
       }

[<Fact>]
let ``nodePaint of a selected UnknownWilp person also paints copper`` () =
    // Selection wins over Kinship: even a Pdeek-only person paints copper when selected.
    let person = personWith (UnknownWilp LaxSkiik)

    Palette.nodePaint person true
    =! {
           Colour = copperBytes
           Emissive = copperBytes
           EmissiveIntensity = 0.8
       }
