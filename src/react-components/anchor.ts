import { Box3, Camera, Object3D, Vector3 } from "three";

// NDC spans two units; these values rescale it to a unit interval and move its
// centre to the top-left pixel origin.
const NDC_TO_UNIT_SCALE = 0.5;
const NDC_TO_UNIT_OFFSET = 0.5;

/** The projected on-screen bounds of a selected node, in canvas pixels. */
export type OverlayAnchor = {
  nodeLeft: number;
  nodeRight: number;
  nodeTop: number;
  nodeBottom: number;
  canvasWidth: number;
  canvasHeight: number;
};

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
  const UPDATE_PARENTS = true;
  const UPDATE_CHILDREN = false;
  object.updateWorldMatrix(UPDATE_PARENTS, UPDATE_CHILDREN);
  const box = new Box3().setFromObject(object);
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

  let nodeLeft = Infinity;
  let nodeRight = -Infinity;
  let nodeTop = Infinity;
  let nodeBottom = -Infinity;

  for (const corner of corners) {
    corner.project(camera);
    const x = (corner.x * NDC_TO_UNIT_SCALE + NDC_TO_UNIT_OFFSET) * canvasWidth;
    const y = (-corner.y * NDC_TO_UNIT_SCALE + NDC_TO_UNIT_OFFSET) * canvasHeight;
    nodeLeft = Math.min(nodeLeft, x);
    nodeRight = Math.max(nodeRight, x);
    nodeTop = Math.min(nodeTop, y);
    nodeBottom = Math.max(nodeBottom, y);
  }

  return {
    nodeLeft,
    nodeRight,
    nodeTop,
    nodeBottom,
    canvasWidth,
    canvasHeight,
  };
}
