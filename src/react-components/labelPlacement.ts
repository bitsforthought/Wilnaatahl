import { Vector3, type Camera, type Object3D } from "three";
import {
  LabelPlacementModule_place,
  ProjectedNodeBounds as FSharpProjectedNodeBounds,
} from "../generated/ViewModel/LabelPlacement";
import { projectNodeBounds } from "./anchor";

export type HtmlSize = { width: number; height: number };

function projectLabelPlacement(
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
    // Translate by half the label width to centre it on the anchor; the top-centre
    // origin keeps that anchor fixed while the label scales.
    labelElement.style.transform = `translateX(-50%) scale(${placement.Scale})`;
    labelElement.style.visibility = "visible";
  }
  return [placement.X, placement.Y];
}

function readPixelsPerWorldUnit(object: Object3D, camera: Camera, canvasHeight: number): number {
  const viewSpaceCenter = new Vector3();
  object.getWorldPosition(viewSpaceCenter);
  viewSpaceCenter.applyMatrix4(camera.matrixWorldInverse);
  // Front-facing points have negative camera-space z; positive z is behind the camera.
  // Clamp at or behind the camera plane to a positive epsilon.
  const signedViewDepth = -viewSpaceCenter.z;
  const viewSpaceDepth = Math.max(signedViewDepth, Number.EPSILON);
  // The projection maps normalized device coordinates (NDC) across a full span.
  // Three.js stores Matrix4 elements in column-major order; zero-based index 5
  // is the row 1/column 1 vertical perspective scale (zoom / tan(fov / 2)).
  // See https://threejs.org/docs/#api/en/cameras/PerspectiveCamera.
  const verticalPerspectiveScaleIndex = 5;
  const verticalPerspectiveScale = camera.projectionMatrix.elements[verticalPerspectiveScaleIndex];
  const fullNdcSpan = 2;
  return (canvasHeight * verticalPerspectiveScale) / (fullNdcSpan * viewSpaceDepth);
}
