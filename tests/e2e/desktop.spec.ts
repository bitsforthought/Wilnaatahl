import { expect, test, type Page } from "@playwright/test";
import {
  clickNode,
  readObserver,
  waitForFrameAfter,
  waitForNewObserverSession,
  waitForStableObserver,
} from "./observer";

const familyFixture = "tests/e2e/fixtures/desktop-family.json";
const warningFixture = "tests/e2e/fixtures/desktop-warning.json";
const invalidFixture = "tests/e2e/fixtures/desktop-invalid.json";

async function importFixture(page: Page, fixture: string) {
  const previousSession = (await waitForStableObserver(page)).session;
  await page.locator('input[type="file"]').setInputFiles(fixture);
  await waitForNewObserverSession(page, previousSession);
}

test("[known defect pin] initial import omits the first rendered node label", async ({ page }) => {
  await page.goto("/");
  await waitForStableObserver(page);
  await importFixture(page, familyFixture);

  const snapshot = await readObserver(page);
  expect(snapshot.nodes.map((node) => node.personId).sort()).toEqual([101, 102, 103]);
  await expect(page.getByText("Ben's title")).toBeVisible();
  await expect(page.getByText("Cora's title")).toBeVisible();
  // Diagnostic pin for https://github.com/bitsforthought/Wilnaatahl/issues/116;
  // it records observed behaviour, not the intended UI contract.
  await expect(page.getByText("Ada's title")).toHaveCount(0);
});

test("view-mode projected node input opens and dismisses the matching detail card", async ({
  page,
}) => {
  await page.goto("/");
  await importFixture(page, familyFixture);

  await clickNode(page, 101);
  await expect(page.getByRole("dialog")).toContainText("Ada North");
  await expect(page.getByRole("dialog")).toBeVisible();

  await page.getByRole("button", { name: "Dismiss detail" }).click();
  await expect(page.getByRole("dialog")).toBeHidden();
  expect((await readObserver(page)).selectedPersonIds).toEqual([]);
});

test("detail overlay isolates card input and suppresses camera wheel gestures", async ({
  page,
}) => {
  await page.goto("/");
  await importFixture(page, familyFixture);
  await clickNode(page, 101);
  await expect(page.getByRole("dialog")).toContainText("Ada North");

  const before = await readObserver(page);
  await page.getByRole("dialog").getByText("Ada North").click();
  await page.mouse.move(before.canvas.left + 24, before.canvas.top + 24);
  await page.mouse.wheel(0, 500);
  const after = await waitForStableObserver(page);

  expect(after.camera).toEqual(before.camera);
  expect(after.selectedPersonIds).toEqual([101]);
  await expect(page.getByRole("dialog")).toBeVisible();
});

test("View and Move controls clear selection and expose mode-specific history controls", async ({
  page,
}) => {
  await page.goto("/");
  await importFixture(page, familyFixture);
  await clickNode(page, 101);

  await page.getByRole("button", { name: "Move" }).click();
  await expect(page.getByRole("button", { name: "View" })).toBeVisible();
  expect((await waitForStableObserver(page)).selectedPersonIds).toEqual([]);
  await expect(page.getByRole("button", { name: "Undo" })).toBeDisabled();
  await expect(page.getByRole("button", { name: "Redo" })).toBeDisabled();
  await expect(page.getByRole("button", { name: "Multi-select" })).toBeVisible();

  await page.getByRole("button", { name: "View" }).click();
  await expect(page.getByRole("button", { name: "Move" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Undo" })).toBeHidden();
  await expect(page.getByRole("button", { name: "Redo" })).toBeHidden();
  expect((await waitForStableObserver(page)).selectedPersonIds).toEqual([]);
});

test("[known defect pin] Move-mode projected clicks are intercepted and leave multi-selection empty", async ({
  page,
}) => {
  await page.goto("/");
  await importFixture(page, familyFixture);
  await clickNode(page, 101);
  await expect(page.getByRole("dialog")).toBeVisible();

  await page.getByRole("button", { name: "Move" }).click();
  await expect(page.getByRole("button", { name: "View" })).toBeVisible();
  expect((await waitForStableObserver(page)).selectedPersonIds).toEqual([]);

  await page.getByRole("button", { name: "Multi-select" }).click();
  await expect(page.getByRole("button", { name: "Single-select" })).toBeVisible();
  const projectedNode = (await readObserver(page)).nodes.find(
    (candidate) => candidate.personId === 102
  );
  if (!projectedNode) throw new Error("Projected fixture node was not rendered.");
  const hitTarget = await page.evaluate(({ x, y }) => {
    const element = document.elementFromPoint(x, y);
    return element?.tagName;
  }, projectedNode.cssPosition);
  // Diagnostic pin for https://github.com/bitsforthought/Wilnaatahl/issues/117.
  expect(hitTarget).not.toBe("CANVAS");
  await clickNode(page, 102);
  await clickNode(page, 103);
  // Diagnostic pin for https://github.com/bitsforthought/Wilnaatahl/issues/117;
  // it records observed behaviour, not the intended UI contract.
  expect((await waitForStableObserver(page)).selectedPersonIds).toEqual([]);
});

test("[known defect pin] selected-node drag and undo remain blocked by empty projected selection", async ({
  page,
}) => {
  await page.goto("/");
  await importFixture(page, familyFixture);
  await page.getByRole("button", { name: "Move" }).click();
  await expect(page.getByRole("button", { name: "View" })).toBeVisible();
  await page.getByRole("button", { name: "Multi-select" }).click();
  await expect(page.getByRole("button", { name: "Single-select" })).toBeVisible();

  const before = await readObserver(page);
  const node = before.nodes.find((candidate) => candidate.personId === 102);
  if (!node) throw new Error("Projected fixture node was not rendered.");
  await clickNode(page, 102);
  // Diagnostic pin for https://github.com/bitsforthought/Wilnaatahl/issues/117.
  expect((await readObserver(page)).selectedPersonIds).toEqual([]);
  await page.mouse.move(node.cssPosition.x, node.cssPosition.y);
  await page.mouse.down();
  await page.mouse.move(node.cssPosition.x + 40, node.cssPosition.y, { steps: 4 });
  await page.mouse.up();

  const after = await waitForFrameAfter(page, before);
  const beforeNode = before.nodes.find((candidate) => candidate.personId === 102);
  const afterNode = after.nodes.find((candidate) => candidate.personId === 102);
  // Diagnostic pin for https://github.com/bitsforthought/Wilnaatahl/issues/117.
  expect(after.selectedPersonIds).toEqual([]);
  expect(afterNode?.logicalPosition).toEqual(beforeNode?.logicalPosition);
  await expect(page.getByRole("button", { name: "Undo" })).toBeDisabled();
});

test("background drag orbits the camera without changing logical node positions", async ({
  page,
}) => {
  await page.goto("/");
  await importFixture(page, familyFixture);
  const before = await readObserver(page);
  const logicalBefore = before.nodes.map((node) => [node.personId, node.logicalPosition]);
  const x = before.canvas.left + 40;
  const y = before.canvas.top + 40;

  await page.mouse.move(x, y);
  await page.mouse.down();
  await page.mouse.move(x + 80, y + 20, { steps: 4 });
  await page.mouse.up();
  const after = await waitForFrameAfter(page, before);

  expect(after.camera.position).not.toEqual(before.camera.position);
  expect(after.nodes.map((node) => [node.personId, node.logicalPosition])).toEqual(logicalBefore);
});

test("wheel zoom changes the camera without changing logical node positions", async ({ page }) => {
  await page.goto("/");
  await importFixture(page, familyFixture);
  const before = await readObserver(page);
  const logicalBefore = before.nodes.map((node) => [node.personId, node.logicalPosition]);

  await page.mouse.move(
    before.canvas.left + before.canvas.width / 2,
    before.canvas.top + before.canvas.height / 2
  );
  await page.mouse.wheel(0, -500);
  const after = await waitForStableObserver(page);

  expect(after.camera.position).not.toEqual(before.camera.position);
  expect(after.nodes.map((node) => [node.personId, node.logicalPosition])).toEqual(logicalBefore);
});

test("save download can be reimported with representative graph data intact", async ({ page }) => {
  await page.goto("/");
  await importFixture(page, familyFixture);
  const before = await readObserver(page);
  const downloadPromise = page.waitForEvent("download");

  await page.getByRole("button", { name: "Save" }).click();
  const download = await downloadPromise;
  const savedPath = await download.path();
  if (!savedPath) throw new Error("Save did not provide a downloadable file path.");

  await importFixture(page, savedPath);
  const after = await readObserver(page);
  expect(after.nodes.map((node) => node.personId).sort()).toEqual(
    before.nodes.map((node) => node.personId).sort()
  );
  await clickNode(page, 101);
  await expect(page.getByRole("dialog")).toContainText("Ada North");
  await expect(page.getByRole("dialog")).toContainText("January 2, 1940");
});

test("warning and invalid imports use the real browser messages without replacing the scene on error", async ({
  page,
}) => {
  await page.goto("/");

  await importFixture(page, warningFixture);
  const sessionBeforeInvalid = (await readObserver(page)).session;
  await expect(page.getByRole("status")).toContainText("Imported with warnings");
  await page.getByRole("button", { name: "Dismiss warnings" }).click();
  await expect(page.getByRole("status")).toBeHidden();

  await page.locator('input[type="file"]').setInputFiles(invalidFixture);
  await expect(page.getByRole("alert")).toBeVisible();
  expect((await readObserver(page)).session).toBe(sessionBeforeInvalid);
  await page.getByRole("button", { name: "Dismiss error" }).click();
  await expect(page.getByRole("alert")).toBeHidden();
});
