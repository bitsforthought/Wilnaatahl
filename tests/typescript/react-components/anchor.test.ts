import { describe, expect, it } from "vitest";
import { BoxGeometry, Mesh, MeshBasicMaterial, OrthographicCamera, PerspectiveCamera } from "three";
import { projectAnchor } from "../../../src/react-components/anchor";

describe("projectAnchor", () => {
  it("projects the node bounds from NDC into pixels and inverts y", () => {
    const mesh = new Mesh(new BoxGeometry(2, 2, 2), new MeshBasicMaterial());
    const LEFT = -5;
    const RIGHT = 5;
    const TOP = 5;
    const BOTTOM = -5;
    const NEAR = 0.1;
    const FAR = 100;
    const camera = new OrthographicCamera(LEFT, RIGHT, TOP, BOTTOM, NEAR, FAR);
    camera.position.z = 5;
    camera.updateProjectionMatrix();
    camera.updateMatrixWorld();

    const anchor = projectAnchor(mesh, camera, 800, 600);

    expect(anchor.canvasWidth).toBe(800);
    expect(anchor.canvasHeight).toBe(600);
    expect(anchor.nodeLeft).toBeCloseTo(320);
    expect(anchor.nodeRight).toBeCloseTo(480);
    expect(anchor.nodeTop).toBeCloseTo(240);
    expect(anchor.nodeBottom).toBeCloseTo(360);
  });

  it("takes the min and max of all projected corners after rotation", () => {
    const mesh = new Mesh(new BoxGeometry(2, 4, 2), new MeshBasicMaterial());
    mesh.rotation.z = Math.PI / 4;
    const LEFT = -5;
    const RIGHT = 5;
    const TOP = 5;
    const BOTTOM = -5;
    const NEAR = 0.1;
    const FAR = 100;
    const camera = new OrthographicCamera(LEFT, RIGHT, TOP, BOTTOM, NEAR, FAR);
    camera.position.z = 10;
    camera.updateProjectionMatrix();
    camera.updateMatrixWorld();

    const anchor = projectAnchor(mesh, camera, 1000, 1000);
    const projectedHalfWidth = (Math.sqrt(2) * 3) / 10;

    expect(anchor.nodeLeft).toBeCloseTo((0.5 - projectedHalfWidth / 2) * 1000, 1);
    expect(anchor.nodeRight).toBeCloseTo((0.5 + projectedHalfWidth / 2) * 1000, 1);
    expect(anchor.nodeTop).toBeCloseTo((0.5 - projectedHalfWidth / 2) * 1000, 1);
    expect(anchor.nodeBottom).toBeCloseTo((0.5 + projectedHalfWidth / 2) * 1000, 1);
  });

  it("takes screen extrema after projecting a perspective-rotated box", () => {
    const mesh = new Mesh(new BoxGeometry(2, 3, 4), new MeshBasicMaterial());
    mesh.rotation.set(Math.PI / 5, Math.PI / 7, Math.PI / 9);
    const VERTICAL_FIELD_OF_VIEW = 55;
    const ASPECT_RATIO = 1;
    const NEAR = 0.1;
    const FAR = 100;
    const camera = new PerspectiveCamera(VERTICAL_FIELD_OF_VIEW, ASPECT_RATIO, NEAR, FAR);
    camera.position.set(4, 3, 9);
    camera.lookAt(0, 0, 0);
    camera.updateProjectionMatrix();
    camera.updateMatrixWorld();

    const anchor = projectAnchor(mesh, camera, 900, 700);
    // These values were calculated independently from the eight transformed
    // vertices; keeping them literal prevents the test from copying the
    // implementation's Box3/corner traversal.
    expect(anchor.nodeLeft).toBeCloseTo(126.0204450431888);
    expect(anchor.nodeRight).toBeCloseTo(689.8944339466748);
    expect(anchor.nodeTop).toBeCloseTo(168.24677029037971);
    expect(anchor.nodeBottom).toBeCloseTo(639.9188535063894);
  });
});
