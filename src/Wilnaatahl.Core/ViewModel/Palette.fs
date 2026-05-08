namespace Wilnaatahl.ViewModel

open System
open Wilnaatahl.Model

module Palette =

    /// An sRGB colour as three gamma-encoded bytes.
    type SrgbColour = { Red: byte; Green: byte; Blue: byte }

    /// A colour in the Oklch perceptual colour space (the cylindrical form of OKLab,
    /// see https://bottosson.github.io/posts/oklab/). Equal numeric offsets in
    /// Lightness produce equal perceived shifts regardless of Hue, which makes
    /// Oklch well-suited to deriving small, perceptually uniform colour variations.
    type Oklch = {
        /// Perceived lightness in [0, 1] (0 = black, 1 = white).
        Lightness: float
        /// Chromatic intensity. 0 is neutral grey; saturated colours sit near 0.18-0.20.
        Chroma: float
        /// Hue in degrees [0, 360).
        Hue: float
    }

    /// Final per-node paint instructions, independent of any rendering library.
    type NodePaint = { Colour: SrgbColour; Emissive: SrgbColour; EmissiveIntensity: float }

    // ---- djb2 hash ---------------------------------------------------------

    let private djb2Seed = 5381
    // (hash <<< 5) + hash is equivalent to hash * 33, the canonical djb2 multiplier.
    let private djb2ShiftBits = 5

    /// djb2 string hash by Daniel J. Bernstein. Deterministic and dependency-free,
    /// with intentional 32-bit signed wraparound so the spread stays bounded for
    /// arbitrarily long inputs.
    let internal djb2Hash (input: string) : uint32 =
        let mutable hash = djb2Seed

        for c in input do
            hash <- (hash <<< djb2ShiftBits) + hash + int c

        uint32 hash

    // ---- Per-Wilp lightness offset ----------------------------------------

    /// Maximum magnitude of the lightness offset applied to a Pdeek's base colour
    /// when deriving a per-Wilp shade. Small enough that all huwilp in a Pdeek still
    /// cluster as that Pdeek's family of colours; large enough for individual huwilp
    /// to be distinguishable from each other.
    let private wilpLightnessWobble = 0.08

    /// Number of evenly-spaced lightness positions in [-wobble, +wobble] that hashes
    /// can land on. The mod operation keeps the spread bounded and reproducible.
    let private hashBuckets = 1000u

    /// Maps a djb2 hash to a lightness offset in the symmetric range
    /// `[-wilpLightnessWobble, +wilpLightnessWobble]`.
    let internal lightnessOffsetFromHash hash =
        let lastBucket = hashBuckets - 1u
        let bucketFraction = float (hash % hashBuckets) / float lastBucket
        let rangeMin = -wilpLightnessWobble
        let rangeMax = wilpLightnessWobble
        rangeMin + bucketFraction * (rangeMax - rangeMin)

    // ---- Oklch -> sRGB conversion -----------------------------------------

    // sRGB transfer-function constants (IEC 61966-2-1):
    //   below the linear threshold, encoded = slope * linear;
    //   above it, encoded = scale * linear^exponent - offset.
    let private srgbLinearThreshold = 0.0031308
    let private srgbLinearSlope = 12.92
    let private srgbGammaScale = 1.055
    let private srgbGammaOffset = 0.055
    let private srgbGammaExponent = 1.0 / 2.4

    let private maxByteValue = 255.0
    let private minLinear = 0.0
    let private maxLinear = 1.0

    let private degreesToRadians degrees = degrees * Math.PI / 180.0

    /// Encodes a single linear-light component in [-inf, +inf] as a gamma-encoded
    /// sRGB byte, clamping out-of-gamut values into [0, 1] before encoding.
    let private linearToSrgbByte linear =
        let clamped = max minLinear (min maxLinear linear)

        let gammaEncoded =
            if clamped <= srgbLinearThreshold then
                srgbLinearSlope * clamped
            else
                srgbGammaScale * Math.Pow(clamped, srgbGammaExponent) - srgbGammaOffset

        byte (Math.Round(gammaEncoded * maxByteValue))

    /// Converts an Oklch colour to a gamma-encoded sRGB byte triple. Uses the
    /// standard OKLab transform coefficients (Björn Ottosson, 2020;
    /// https://bottosson.github.io/posts/oklab/) and clamps out-of-gamut linear
    /// components into [0, 1] before applying the sRGB gamma encoding.
    let internal oklchToSrgb oklch =
        let hueRadians = degreesToRadians oklch.Hue
        let a = oklch.Chroma * Math.Cos hueRadians
        let b = oklch.Chroma * Math.Sin hueRadians

        // Oklch -> OKLab -> nonlinear LMS -> linear LMS -> linear sRGB,
        // collapsed into the matrix multiplications below.
        let l_ = oklch.Lightness + 0.3963377774 * a + 0.2158037573 * b
        let m_ = oklch.Lightness - 0.1055613458 * a - 0.0638541728 * b
        let s_ = oklch.Lightness - 0.0894841775 * a - 1.291485548 * b

        let l = l_ * l_ * l_
        let m = m_ * m_ * m_
        let s = s_ * s_ * s_

        let redLinear = +4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s
        let greenLinear = -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s
        let blueLinear = -0.0041960863 * l - 0.7034186147 * m + 1.707614701 * s

        {
            Red = linearToSrgbByte redLinear
            Green = linearToSrgbByte greenLinear
            Blue = linearToSrgbByte blueLinear
        }

    // ---- Pdeek + unaffiliated palette --------------------------------------

    /// Base Oklch colour for each Pdeek (Clan), drawn from the Okabe-Ito CVD-safe
    /// categorical palette (Okabe & Ito 2008, https://jfly.uni-koeln.de/color/).
    /// Each Oklch triple is the conversion of the corresponding Okabe-Ito reference
    /// hex into Oklch (rounded for readability), with one deliberate adjustment for
    /// LaxGibuu where the lightness is lifted from the reference for better contrast
    /// against the dark scene background.
    ///
    ///   Giskaast (Fireweed) -> #D55E00 vermillion         -> oklch(0.63, 0.18, 41)
    ///   Ganeda   (Frog)     -> #009E73 bluish-green       -> oklch(0.63, 0.13, 161)
    ///   LaxSkiik (Eagle)    -> #F0E442 yellow             -> oklch(0.89, 0.18, 101)
    ///   LaxGibuu (Wolf)     -> #56B4E9 light sky blue     -> oklch(0.73, 0.12, 235)
    ///                          (lifted from the Okabe-Ito #0072B2 reference for
    ///                          better contrast against the dark scene background)
    ///
    /// Pattern matched (rather than table-lookup) so adding a new Pdeek case is a
    /// compile error here.
    let internal baseColourForPdeek pdeek : Oklch =
        match pdeek with
        | Giskaast -> { Lightness = 0.63; Chroma = 0.18; Hue = 41.0 }
        | Ganeda -> { Lightness = 0.63; Chroma = 0.13; Hue = 161.0 }
        | LaxSkiik -> { Lightness = 0.89; Chroma = 0.18; Hue = 101.0 }
        | LaxGibuu -> { Lightness = 0.73; Chroma = 0.12; Hue = 235.0 }

    /// Colour for tree nodes whose Person has no Wilp affiliation. A soft warm ivory,
    /// kept light enough to contrast clearly with the dark scene background and to
    /// remain distinguishable from the four Pdeek base colours under all common forms
    /// of colour-vision deficiency.
    let internal unaffiliatedColour: SrgbColour =
        oklchToSrgb { Lightness = 0.90; Chroma = 0.04; Hue = 85.0 }

    /// The displayed colour for a Wilp: the Pdeek's base Oklch with a per-name
    /// lightness offset, then converted to an sRGB byte triple. All huwilp in a
    /// Pdeek visibly cluster as one family of shades; within the family, each Wilp
    /// gets a deterministic-but-varied lightness derived from a hash of its name.
    let internal colourForWilp (wilp: Wilp) : SrgbColour =
        let baseOklch = baseColourForPdeek wilp.Pdeek
        let dLightness = lightnessOffsetFromHash (djb2Hash wilp.Name.AsString)
        oklchToSrgb { baseOklch with Lightness = baseOklch.Lightness + dLightness }

    // ---- Per-node paint decision -------------------------------------------

    /// Selected tree node colour: a copper-inspired colour applied as both the base
    /// and emissive colour so the node appears to glow when picked. Copper holds
    /// cultural significance to the Gitxsan. The hue is chosen to remain visually
    /// distinct from every Pdeek base colour, including under common forms of
    /// colour-vision deficiency.
    /// Variant C: verdigris (oxidized copper patina, soft blue-green).
    let private selectedColour: SrgbColour =
        oklchToSrgb { Lightness = 0.65; Chroma = 0.10; Hue = 175.0 }

    let private selectedEmissiveIntensity = 0.8

    /// No emissive contribution for unselected nodes.
    let private noEmissiveColour: SrgbColour = { Red = 0uy; Green = 0uy; Blue = 0uy }
    let private noEmissiveIntensity = 0.0

    /// The final paint for a single tree node. Selection wins over Wilp affiliation:
    /// a selected node always paints copper with an emissive glow regardless of its Wilp.
    let nodePaint (person: Person) (isSelected: bool) : NodePaint =
        if isSelected then
            {
                Colour = selectedColour
                Emissive = selectedColour
                EmissiveIntensity = selectedEmissiveIntensity
            }
        else
            let colour =
                match person.Wilp with
                | Some w -> colourForWilp w
                | None -> unaffiliatedColour

            {
                Colour = colour
                Emissive = noEmissiveColour
                EmissiveIntensity = noEmissiveIntensity
            }
