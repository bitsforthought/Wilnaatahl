import { CylinderGeometry, Mesh, MeshStandardMaterial, Quaternion, Vector3 } from "three";
import { Entity, Not, World } from "koota";
import { IWorld, toKootaWorld } from "./koota/kootaWrapper";
import { Hidden, Line, MeshRef, PersonRef, Position, Selected } from "./traits";
import { getLinePositions } from "./connectors";
import {
  defaultArg,
  map as mapOption,
} from "../generated/fable_modules/fable-library-ts.4.27.0/Option.js";
import type { Person, Pdeek, Wilp } from "../generated/Model";

// MAINTENANCE NOTE: Keep all presentation-specific constants up here for ease of update.
const defaultEmissiveIntensity = 0;
const defaultEmissiveColour = "#000000"; // No emissive colour for unselected nodes.

const selectedNodeColour = "#8B4000"; // Deep, red copper
const selectedEmissiveIntensity = 0.8;

const lineCylinderRadius = 0.03;
const lineCylinderRadialSegments = 8;

// ---- Tree node palette ----------------------------------------------------
//
// Each Pdeek (Clan) has a base colour; each Wilp within that Pdeek renders in
// a slightly different shade of the base colour. Per-Wilp shade is derived
// from a deterministic hash of the Wilp name, so adding a new Wilp does not
// require manually picking a colour and the palette is stable across runs.
//
// Accessibility:
//   - The four Pdeek base colours are taken from the Okabe-Ito categorical
//     palette, which is designed to remain distinguishable under the three
//     common forms of color vision deficiency (deuteranopia, protanopia,
//     tritanopia) as well as full achromatopsia.
//     See https://jfly.uni-koeln.de/color/ for the original publication.
//   - We work in OKLCH (a perceptually uniform color space). Equal numeric
//     offsets in OKLCH lightness produce equal perceived shifts regardless
//     of hue, which means our per-Wilp shade variation looks consistent
//     across all four Pdeek and stays within a CVD-stable band.
//   - Unaffiliated tree nodes paint a near-neutral warm gray rather than a
//     fifth chromatic colour. Purple — the prior choice — is a fragile pick
//     under CVD because it is a red+blue mixture and collapses toward red
//     under tritanopia and toward blue under protanopia. A neutral colour
//     also reads naturally as "no Pdeek affiliation".
//   - Verify changes with the DevTools "Emulate vision deficiencies" panel
//     (Edge / Chrome → DevTools → Rendering tab).

type OKLCH = {
  /** Perceived lightness in [0, 1]. */
  L: number;
  /** Chroma (colourfulness); 0 is neutral gray, ~0.18 is near sRGB gamut edge. */
  C: number;
  /** Hue in degrees [0, 360). */
  H: number;
};

// Pdeek base colours (Okabe-Ito anchors, expressed in OKLCH):
//   Giskaast (Fireweed) → Okabe-Ito vermillion ~#D55E00
//   Ganeda   (Frog)     → Okabe-Ito bluish-green ~#009E73
//   LaxSkiik (Eagle)    → Okabe-Ito yellow ~#F0E442
//   LaxGibuu (Wolf)     → Okabe-Ito sky blue ~#56B4E9 (lifted from #0072B2 for
//                         better contrast with the dark scene background).
const pdeekBaseColour: Record<Pdeek, OKLCH> = {
  giskaast: { L: 0.63, C: 0.18, H: 41 },
  ganeda: { L: 0.63, C: 0.13, H: 161 },
  laxSkiik: { L: 0.89, C: 0.18, H: 101 },
  laxGibuu: { L: 0.73, C: 0.12, H: 235 },
};

// Per-Wilp lightness wobble in OKLCH units. Small enough that all huwilp in a
// Pdeek still cluster as that Pdeek's family of colours, large enough to be
// individually distinguishable.
const wilpLightnessWobble = 0.08;

// Unaffiliated tree nodes: a soft warm ivory, kept light enough to contrast
// clearly with the dark scene background and to remain distinguishable from
// the four Pdeek base colours under all common CVD modes.
const unaffiliatedColour: OKLCH = { L: 0.9, C: 0.04, H: 85 };

// Many of these functions will be called for every rendered tree node, so they will be
// faster as standalone functions.
function setPositionOnMesh([pos, mesh]: [{ x: number; y: number; z: number }, Mesh]) {
  mesh.position.copy(pos);
}

function copyPositionsToMeshes(world: World) {
  world.query(Position, MeshRef, Not(Hidden)).updateEach(setPositionOnMesh);
}

function setColourProperties(
  mesh: Mesh,
  colorHex: string,
  emissiveHex: string,
  emissiveIntensity: number
) {
  const material = mesh.material as MeshStandardMaterial;
  material.color.set(colorHex);
  material.emissive.set(emissiveHex);
  material.emissiveIntensity = emissiveIntensity;
}

function setSelectedColour([mesh]: [Mesh]) {
  setColourProperties(mesh, selectedNodeColour, selectedNodeColour, selectedEmissiveIntensity);
}

// djb2 string hash. Deterministic, dependency-free, and good enough to spread Wilp
// names across a small lightness window.
function hashString(str: string): number {
  let hash = 5381;
  for (let i = 0; i < str.length; i++) {
    hash = ((hash << 5) + hash + str.charCodeAt(i)) | 0;
  }
  // Convert to unsigned for predictable downstream arithmetic.
  return hash >>> 0;
}

// Maps a hash to the symmetric range [-wilpLightnessWobble, +wilpLightnessWobble].
function lightnessOffsetFromHash(hash: number): number {
  return ((hash % 1000) / 999) * 2 * wilpLightnessWobble - wilpLightnessWobble;
}

// OKLCH → linear sRGB → gamma-encoded sRGB hex string. Implements the standard
// OKLab transform (Björn Ottosson, 2020) and clips out-of-gamut components by
// clamping to [0, 1] in linear sRGB. Hue is in degrees, L and C in OKLab units.
function oklchToHex({ L, C, H }: OKLCH): string {
  const hRad = (H * Math.PI) / 180;
  const a = C * Math.cos(hRad);
  const b = C * Math.sin(hRad);

  const l_ = L + 0.3963377774 * a + 0.2158037573 * b;
  const m_ = L - 0.1055613458 * a - 0.0638541728 * b;
  const s_ = L - 0.0894841775 * a - 1.291485548 * b;

  const l = l_ * l_ * l_;
  const m = m_ * m_ * m_;
  const s = s_ * s_ * s_;

  const rLin = +4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s;
  const gLin = -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s;
  const bLin = -0.0041960863 * l - 0.7034186147 * m + 1.707614701 * s;

  return `#${linearToHexByte(rLin)}${linearToHexByte(gLin)}${linearToHexByte(bLin)}`;
}

function linearToHexByte(linear: number): string {
  const clamped = Math.max(0, Math.min(1, linear));
  const gammaEncoded =
    clamped <= 0.0031308 ? 12.92 * clamped : 1.055 * Math.pow(clamped, 1 / 2.4) - 0.055;
  return Math.round(gammaEncoded * 255)
    .toString(16)
    .padStart(2, "0");
}

function colourForWilp(wilp: Wilp): string {
  const base = pdeekBaseColour[wilp.Pdeek];
  const offset = lightnessOffsetFromHash(hashString(wilp.Name));
  return oklchToHex({ L: base.L + offset, C: base.C, H: base.H });
}

const unaffiliatedColourHex = oklchToHex(unaffiliatedColour);

function setWilpColour([mesh, person]: [Mesh, Person]) {
  const colour = defaultArg(mapOption(colourForWilp, person.Wilp), unaffiliatedColourHex);
  setColourProperties(mesh, colour, defaultEmissiveColour, defaultEmissiveIntensity);
}

function paintTreeNodes(world: World) {
  world.query(MeshRef, Selected, Not(Hidden)).updateEach(setSelectedColour);
  world.query(MeshRef, PersonRef, Not(Selected, Hidden)).updateEach(setWilpColour);
}

function copyLinePropertiesToMeshes(world: World) {
  function setLineMeshProperties([mesh]: [Mesh], entity: Entity) {
    const [from, to] = getLinePositions(world, entity);
    const direction = to.clone().sub(from);
    const length = direction.length();
    const midpoint = from.clone().add(direction.clone().multiplyScalar(0.5));
    const orientation = new Quaternion().setFromUnitVectors(
      new Vector3(0, 1, 0), // cylinder's up axis
      direction.clone().normalize()
    );

    mesh.position.copy(midpoint);
    mesh.quaternion.copy(orientation);
    mesh.geometry.dispose();
    mesh.geometry = new CylinderGeometry(
      lineCylinderRadius,
      lineCylinderRadius,
      length,
      lineCylinderRadialSegments
    );
  }

  world.query(Line, MeshRef, Not(Hidden)).select(MeshRef).updateEach(setLineMeshProperties);
}

export function render(world: IWorld): IWorld {
  const kootaWorld = toKootaWorld(world);
  copyPositionsToMeshes(kootaWorld);
  copyLinePropertiesToMeshes(kootaWorld);
  paintTreeNodes(kootaWorld);
  return world;
}
