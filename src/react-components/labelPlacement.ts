import type { Camera, Object3D } from "three";
import {
  LabelPlacementModule_place,
  ProjectedNodeBounds as FSharpProjectedNodeBounds,
} from "../generated/ViewModel/LabelPlacement";
import { projectNodeBounds, readPixelsPerWorldUnit } from "./anchor";

export type HtmlSize = { width: number; height: number };
export type LabelPlacementLike = { x: number; y: number; scale: number };
export type LabelPlacementCache = {
  readPlacement(
    mesh: Object3D,
    camera: Camera,
    canvasWidth: number,
    canvasHeight: number
  ): LabelPlacementLike | undefined;
};

function projectLabelPlacement(
  object: Object3D,
  camera: Camera,
  canvasWidth: number,
  canvasHeight: number
) {
  const bounds = projectNodeBounds(object, camera, canvasWidth, canvasHeight);
  return LabelPlacementModule_place(
    new FSharpProjectedNodeBounds(bounds.left, bounds.right, bounds.top, bounds.bottom),
    readPixelsPerWorldUnit(object, camera, canvasHeight)
  );
}

export function projectLabelPlacementFromBounds(
  bounds: { left: number; right: number; top: number; bottom: number },
  pixelsPerWorldUnit: number
): LabelPlacementLike {
  const placement = LabelPlacementModule_place(
    new FSharpProjectedNodeBounds(bounds.left, bounds.right, bounds.top, bounds.bottom),
    pixelsPerWorldUnit
  );
  return { x: placement.X, y: placement.Y, scale: placement.Scale };
}

export function calculateLabelPosition(
  mesh: Object3D | null,
  camera: Camera,
  size: HtmlSize,
  labelElement: HTMLElement | null,
  placementCache?: LabelPlacementCache
): number[] {
  if (!mesh) return [0, 0];

  const cachedPlacement = placementCache?.readPlacement(mesh, camera, size.width, size.height);
  const placement = cachedPlacement
    ? { X: cachedPlacement.x, Y: cachedPlacement.y, Scale: cachedPlacement.scale }
    : projectLabelPlacement(mesh, camera, size.width, size.height);
  if (labelElement) {
    // Translate by half the label width to centre it on the anchor; the top-centre
    // origin keeps that anchor fixed while the label scales.
    labelElement.style.transform = `translateX(-50%) scale(${placement.Scale})`;
    if (!placementCache && labelElement.style.visibility !== "visible") {
      labelElement.style.visibility = "visible";
    }
  }
  return [placement.X, placement.Y];
}
