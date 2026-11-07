import { describe, expect, it } from "vitest";
import { BoxGeometry, Mesh, MeshBasicMaterial, PerspectiveCamera } from "three";
import { calculateLabelPosition } from "../../../src/react-components/labelPlacement";

const canvasWidth = 800;
const canvasHeight = 600;
const verticalFieldOfViewDegrees = 55;
const aspectRatio = 1;
const rightAngleVerticalFieldOfViewDegrees = 90;
const rightAngleAspectRatio = canvasWidth / canvasHeight;
const cameraNear = 0.1;
const cameraFar = 100;
const nodeSideLength = 2;
// This distance keeps the entire origin-centred cube in front of the camera
// and between its clipping planes; it is a fixture choice, not a product constraint.
const perspectiveCameraDistance = 5;
const doubledCameraZoom = 2;

function readScale(labelElement: HTMLElement): number {
  const match = /scale\(([^)]+)\)$/.exec(labelElement.style.transform);
  if (!match) throw new Error(`Label transform has no scale: ${labelElement.style.transform}`);
  return Number(match[1]);
}

describe("calculateLabelPosition", () => {
  it("uses rotated perspective view-space depth for scale", () => {
    const mesh = new Mesh(
      new BoxGeometry(nodeSideLength, nodeSideLength, nodeSideLength),
      new MeshBasicMaterial()
    );
    const meshOffsetX = 1;
    mesh.position.set(meshOffsetX, 0, 0);
    const camera = new PerspectiveCamera(
      verticalFieldOfViewDegrees,
      aspectRatio,
      cameraNear,
      cameraFar
    );
    const cameraPosition = { x: 4, y: 3, z: 9 };
    camera.position.set(cameraPosition.x, cameraPosition.y, cameraPosition.z);
    camera.lookAt(0, 0, 0);
    camera.updateProjectionMatrix();
    camera.updateMatrixWorld();

    const labelElement = { style: { transform: "", visibility: "" } } as HTMLElement;
    const placement = calculateLabelPosition(
      mesh,
      camera,
      { width: canvasWidth, height: canvasHeight },
      labelElement
    );
    expect(placement[0]).toBeCloseTo(467.869, 2);
    expect(placement[1]).toBeCloseTo(402.131, 2);
    expect(readScale(labelElement)).toBeCloseTo(0.7271221135, 8);
  });

  it("clamps perspective depth at zero, near zero, and behind the camera", () => {
    const camera = new PerspectiveCamera(
      verticalFieldOfViewDegrees,
      aspectRatio,
      cameraNear,
      cameraFar
    );
    camera.updateProjectionMatrix();
    camera.updateMatrixWorld();
    const atCameraPlane = new Mesh(
      new BoxGeometry(nodeSideLength, nodeSideLength, nodeSideLength),
      new MeshBasicMaterial()
    );
    const justInFrontOfCameraPlane = new Mesh(
      new BoxGeometry(nodeSideLength, nodeSideLength, nodeSideLength),
      new MeshBasicMaterial()
    );
    const justInFrontDepth = -Number.EPSILON;
    justInFrontOfCameraPlane.position.z = justInFrontDepth;
    const behindCameraPlane = new Mesh(
      new BoxGeometry(nodeSideLength, nodeSideLength, nodeSideLength),
      new MeshBasicMaterial()
    );
    const behindCameraDepth = 1;
    behindCameraPlane.position.z = behindCameraDepth;

    const atCameraPlaneLabel = { style: { transform: "", visibility: "" } } as HTMLElement;
    const justInFrontLabel = { style: { transform: "", visibility: "" } } as HTMLElement;
    const behindCameraLabel = { style: { transform: "", visibility: "" } } as HTMLElement;
    calculateLabelPosition(
      atCameraPlane,
      camera,
      { width: canvasWidth, height: canvasHeight },
      atCameraPlaneLabel
    );
    calculateLabelPosition(
      justInFrontOfCameraPlane,
      camera,
      { width: canvasWidth, height: canvasHeight },
      justInFrontLabel
    );
    calculateLabelPosition(
      behindCameraPlane,
      camera,
      { width: canvasWidth, height: canvasHeight },
      behindCameraLabel
    );

    expect(Number.isFinite(readScale(atCameraPlaneLabel))).toBe(true);
    expect(readScale(atCameraPlaneLabel)).toBe(readScale(justInFrontLabel));
    expect(readScale(atCameraPlaneLabel)).toBe(readScale(behindCameraLabel));
  });

  it("returns the origin when the mesh reference is unavailable", () => {
    const camera = new PerspectiveCamera(
      rightAngleVerticalFieldOfViewDegrees,
      rightAngleAspectRatio,
      cameraNear,
      cameraFar
    );

    expect(
      calculateLabelPosition(null, camera, { width: canvasWidth, height: canvasHeight }, null)
    ).toEqual([0, 0]);
  });

  it("updates the label transform while returning the projected anchor", () => {
    const mesh = new Mesh(
      new BoxGeometry(nodeSideLength, nodeSideLength, nodeSideLength),
      new MeshBasicMaterial()
    );
    const camera = new PerspectiveCamera(
      rightAngleVerticalFieldOfViewDegrees,
      rightAngleAspectRatio,
      cameraNear,
      cameraFar
    );
    camera.position.z = perspectiveCameraDistance;
    camera.updateProjectionMatrix();
    camera.updateMatrixWorld();
    const labelElement = { style: { transform: "", visibility: "" } } as HTMLElement;

    const position = calculateLabelPosition(
      mesh,
      camera,
      { width: canvasWidth, height: canvasHeight },
      labelElement
    );

    expect(position[0]).toBeCloseTo(400);
    expect(position[1]).toBeCloseTo(383);
    expect(labelElement.style.transform).toMatch(/^translateX\(-50%\) scale\(/);
    expect(readScale(labelElement)).toBeCloseTo(0.75);
    expect(labelElement.style.visibility).toBe("visible");
  });

  it("returns the projected anchor without a mounted label element", () => {
    const mesh = new Mesh(
      new BoxGeometry(nodeSideLength, nodeSideLength, nodeSideLength),
      new MeshBasicMaterial()
    );
    const camera = new PerspectiveCamera(
      rightAngleVerticalFieldOfViewDegrees,
      rightAngleAspectRatio,
      cameraNear,
      cameraFar
    );
    camera.position.z = perspectiveCameraDistance;
    camera.updateProjectionMatrix();
    camera.updateMatrixWorld();

    const position = calculateLabelPosition(
      mesh,
      camera,
      { width: canvasWidth, height: canvasHeight },
      null
    );

    expect(position[0]).toBeCloseTo(400);
    expect(position[1]).toBeCloseTo(383);
  });

  it("updates the label scale when perspective zoom changes", () => {
    const mesh = new Mesh(
      new BoxGeometry(nodeSideLength, nodeSideLength, nodeSideLength),
      new MeshBasicMaterial()
    );
    const camera = new PerspectiveCamera(
      rightAngleVerticalFieldOfViewDegrees,
      rightAngleAspectRatio,
      cameraNear,
      cameraFar
    );
    camera.position.z = perspectiveCameraDistance;
    camera.updateProjectionMatrix();
    camera.updateMatrixWorld();
    const labelElement = { style: { transform: "", visibility: "" } } as HTMLElement;

    calculateLabelPosition(
      mesh,
      camera,
      { width: canvasWidth, height: canvasHeight },
      labelElement
    );
    camera.zoom = doubledCameraZoom;
    camera.updateProjectionMatrix();
    const zoomedPlacement = calculateLabelPosition(
      mesh,
      camera,
      { width: canvasWidth, height: canvasHeight },
      labelElement
    );

    expect(zoomedPlacement[0]).toBeCloseTo(400);
    expect(zoomedPlacement[1]).toBeCloseTo(458);
    expect(readScale(labelElement)).toBeCloseTo(1.5);
  });
});
