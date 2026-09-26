import { Box3, Camera, Object3D, Vector3 } from "three";

/**
 * The projected on-screen anchor of the selected node, in canvas pixels. It is
 * reprojected only when the selection, camera, or canvas changes, so an animating
 * selected node leaves the anchor behind — see the accepted gaps in
 * `specs/names-and-detail-overlay.md`.
 */
export type OverlayAnchor = {
  /** Screen x of the node's left edge. */
  nodeLeft: number;
  /** Screen x of the node's right edge. */
  nodeRight: number;
  /** Screen y of the node's top edge. */
  nodeTop: number;
  /** Screen y of the node's bottom edge. */
  nodeBottom: number;
  /** Canvas width in pixels. */
  canvasWidth: number;
  /** Canvas height in pixels. */
  canvasHeight: number;
};

/**
 * Converts a point in normalized device coordinates (NDC) to canvas pixels.
 *
 * `Vector3.project(camera)` returns NDC: the camera's view volume squashed into a
 * cube spanning -1..1 on every axis, with the origin at the centre of the viewport.
 * Screen pixels instead run 0..width from the left and 0..height from the *top*.
 * So each axis is rescaled from -1..1 to 0..1 and multiplied by the canvas
 * dimension. The y term is negated first because NDC's y grows upward while the
 * screen's grows downward.
 *
 * See https://threejs.org/docs/#api/en/math/Vector3.project and
 * https://learnopengl.com/Getting-started/Coordinate-Systems for background.
 */
function ndcToScreen(v: Vector3, width: number, height: number): { x: number; y: number } {
  // One over the -1..1 span, then a shift of half a unit to move the origin from the
  // centre to the edge. Equal by coincidence: the NDC span is twice the unit range.
  const NDC_TO_UNIT_SCALE = 0.5;
  const NDC_TO_UNIT_OFFSET = 0.5;

  return {
    x: (v.x * NDC_TO_UNIT_SCALE + NDC_TO_UNIT_OFFSET) * width,
    y: (-v.y * NDC_TO_UNIT_SCALE + NDC_TO_UNIT_OFFSET) * height,
  };
}

/**
 * Projects every corner of an object's world-space bounding box into canvas
 * pixels. Taking extrema after projection keeps the screen-space bounds correct
 * when the object or camera is rotated.
 */
export function projectAnchor(
  object: Object3D,
  camera: Camera,
  canvasWidth: number,
  canvasHeight: number
): OverlayAnchor {
  // Three.js names these positionally; the call reads as (updateParents, updateChildren).
  const UPDATE_PARENTS = true;
  const UPDATE_CHILDREN = false;
  object.updateWorldMatrix(UPDATE_PARENTS, UPDATE_CHILDREN);
  const box = new Box3().setFromObject(object);
  // Project all eight corners of the node's world-space box and take the
  // screen-space min/max, so the anchor's left/right/top/bottom are edge-correct
  // regardless of how the camera was orbited before selection (projecting only
  // box.min/box.max could swap or collapse the edges under rotation).
  const corners = [
    new Vector3(box.min.x, box.min.y, box.min.z),
    new Vector3(box.min.x, box.min.y, box.max.z),
    new Vector3(box.min.x, box.max.y, box.min.z),
    new Vector3(box.min.x, box.max.y, box.max.z),
    new Vector3(box.max.x, box.min.y, box.min.z),
    new Vector3(box.max.x, box.min.y, box.max.z),
    new Vector3(box.max.x, box.max.y, box.min.z),
    new Vector3(box.max.x, box.max.y, box.max.z),
  ];

  let minX = Infinity;
  let maxX = -Infinity;
  let minY = Infinity;
  let maxY = -Infinity;

  for (const corner of corners) {
    const screen = ndcToScreen(corner.project(camera), canvasWidth, canvasHeight);
    minX = Math.min(minX, screen.x);
    maxX = Math.max(maxX, screen.x);
    minY = Math.min(minY, screen.y);
    maxY = Math.max(maxY, screen.y);
  }

  return {
    nodeLeft: minX,
    nodeRight: maxX,
    nodeTop: minY,
    nodeBottom: maxY,
    canvasWidth,
    canvasHeight,
  };
}
