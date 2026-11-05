import { OrthographicCamera, PerspectiveCamera, Vector3, type Camera, type Object3D } from "three";
import {
  LabelPlacementModule_place,
  ProjectedNodeBounds as FSharpProjectedNodeBounds,
} from "../generated/ViewModel/LabelPlacement";
import { projectNodeBounds } from "./anchor";

type HtmlSize = { width: number; height: number };

export function projectLabelPlacement(
  object: Object3D,
  camera: Camera,
  canvasWidth: number,
  canvasHeight: number
) {
  const bounds = projectNodeBounds(object, camera, canvasWidth, canvasHeight);
  const pixelsPerWorldUnit = readPixelsPerWorldUnit(object, camera, canvasHeight);
  return LabelPlacementModule_place(
    new FSharpProjectedNodeBounds(bounds.left, bounds.right, bounds.top, bounds.bottom),
    pixelsPerWorldUnit
  );
}

export function calculateLabelPosition(
  mesh: Object3D | null,
  camera: Camera,
  size: HtmlSize,
  labelElement: HTMLElement | null
): number[] {
  if (!mesh) return [0, 0];

  const placement = projectLabelPlacement(mesh, camera, size.width, size.height);
  if (labelElement) {
    labelElement.style.transform = `translateX(-50%) scale(${placement.Scale})`;
    labelElement.style.visibility = "visible";
  }
  return [placement.X, placement.Y];
}

const viewSpaceCenter = new Vector3();
function readPixelsPerWorldUnit(object: Object3D, camera: Camera, canvasHeight: number): number {
  if (camera instanceof OrthographicCamera) {
    return (canvasHeight * camera.zoom) / (camera.top - camera.bottom);
  }

  if (camera instanceof PerspectiveCamera) {
    object.getWorldPosition(viewSpaceCenter);
    viewSpaceCenter.applyMatrix4(camera.matrixWorldInverse);
    const viewSpaceDepth = Math.max(-viewSpaceCenter.z, Number.EPSILON);
    return (canvasHeight * camera.projectionMatrix.elements[5]) / (2 * viewSpaceDepth);
  }

  return 1;
}
