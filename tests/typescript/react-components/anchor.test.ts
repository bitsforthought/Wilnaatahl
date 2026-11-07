import { describe, expect, it } from "vitest";
import { BoxGeometry, Mesh, MeshBasicMaterial, OrthographicCamera, PerspectiveCamera } from "three";
import {
  projectAnchor,
  readPixelsPerWorldUnit,
  readViewSpaceDepth,
} from "../../../src/react-components/anchor";

const CAMERA_LEFT = -5;
const CAMERA_RIGHT = 5;
const CAMERA_TOP = 5;
const CAMERA_BOTTOM = -5;
const CAMERA_VERTICAL_FIELD_OF_VIEW = 55;
const CAMERA_ASPECT_RATIO = 1;
const CAMERA_NEAR = 0.1;
const CAMERA_FAR = 100;

describe("projectAnchor", () => {
  it("projects the node bounds from NDC into pixels and inverts y", () => {
    const mesh = new Mesh(new BoxGeometry(2, 2, 2), new MeshBasicMaterial());
    const camera = new OrthographicCamera(
      CAMERA_LEFT,
      CAMERA_RIGHT,
      CAMERA_TOP,
      CAMERA_BOTTOM,
      CAMERA_NEAR,
      CAMERA_FAR
    );
    camera.position.z = 5;
    camera.updateProjectionMatrix();
    camera.updateMatrixWorld();

    const anchor = projectAnchor(mesh, camera, 800, 600);

    expect(anchor.canvasWidth).toBe(800);
    expect(anchor.canvasHeight).toBe(600);
    expect(anchor.bounds.left).toBeCloseTo(320);
    expect(anchor.bounds.right).toBeCloseTo(480);
    expect(anchor.bounds.top).toBeCloseTo(240);
    expect(anchor.bounds.bottom).toBeCloseTo(360);
  });

  it("takes the min and max of all projected corners after rotation", () => {
    const mesh = new Mesh(new BoxGeometry(2, 4, 2), new MeshBasicMaterial());
    mesh.rotation.z = Math.PI / 4;
    const camera = new OrthographicCamera(
      CAMERA_LEFT,
      CAMERA_RIGHT,
      CAMERA_TOP,
      CAMERA_BOTTOM,
      CAMERA_NEAR,
      CAMERA_FAR
    );
    camera.position.z = 10;
    camera.updateProjectionMatrix();
    camera.updateMatrixWorld();

    const anchor = projectAnchor(mesh, camera, 1000, 1000);
    const projectedHalfWidth = (Math.sqrt(2) * 3) / 10;

    expect(anchor.bounds.left).toBeCloseTo((0.5 - projectedHalfWidth / 2) * 1000, 1);
    expect(anchor.bounds.right).toBeCloseTo((0.5 + projectedHalfWidth / 2) * 1000, 1);
    expect(anchor.bounds.top).toBeCloseTo((0.5 - projectedHalfWidth / 2) * 1000, 1);
    expect(anchor.bounds.bottom).toBeCloseTo((0.5 + projectedHalfWidth / 2) * 1000, 1);
  });

  it("takes screen extrema after projecting a perspective-rotated box", () => {
    const mesh = new Mesh(new BoxGeometry(2, 3, 4), new MeshBasicMaterial());
    mesh.rotation.set(Math.PI / 5, Math.PI / 7, Math.PI / 9);
    const camera = new PerspectiveCamera(
      CAMERA_VERTICAL_FIELD_OF_VIEW,
      CAMERA_ASPECT_RATIO,
      CAMERA_NEAR,
      CAMERA_FAR
    );
    camera.position.set(4, 3, 9);
    camera.lookAt(0, 0, 0);
    camera.updateProjectionMatrix();
    camera.updateMatrixWorld();

    const anchor = projectAnchor(mesh, camera, 900, 700);
    // These values were calculated independently from the eight transformed
    // vertices; keeping them literal prevents the test from copying the
    // implementation's Box3/corner traversal.
    expect(anchor.bounds.left).toBeCloseTo(126.0204450431888);
    expect(anchor.bounds.right).toBeCloseTo(689.8944339466748);
    expect(anchor.bounds.top).toBeCloseTo(168.24677029037971);
    expect(anchor.bounds.bottom).toBeCloseTo(639.9188535063894);
  });

  it("reads positive perspective view depth and pixel scale", () => {
    const mesh = new Mesh(new BoxGeometry(1, 1, 1), new MeshBasicMaterial());
    const camera = new PerspectiveCamera(
      CAMERA_VERTICAL_FIELD_OF_VIEW,
      CAMERA_ASPECT_RATIO,
      CAMERA_NEAR,
      CAMERA_FAR
    );
    camera.position.z = 5;
    camera.updateProjectionMatrix();
    camera.updateMatrixWorld();

    expect(readViewSpaceDepth(mesh, camera)).toBeCloseTo(5);
    expect(readPixelsPerWorldUnit(mesh, camera, 600)).toBeCloseTo(
      (600 * camera.projectionMatrix.elements[5]) / 10
    );
  });

  it("does not treat geometry behind the camera as an occluder", () => {
    const mesh = new Mesh(new BoxGeometry(1, 1, 1), new MeshBasicMaterial());
    mesh.position.z = 6;
    const camera = new PerspectiveCamera(
      CAMERA_VERTICAL_FIELD_OF_VIEW,
      CAMERA_ASPECT_RATIO,
      CAMERA_NEAR,
      CAMERA_FAR
    );
    camera.position.z = 5;
    camera.updateProjectionMatrix();
    camera.updateMatrixWorld();

    expect(readViewSpaceDepth(mesh, camera)).toBe(Infinity);
  });
});
