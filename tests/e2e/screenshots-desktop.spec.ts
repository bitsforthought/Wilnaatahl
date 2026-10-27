import { expect, test, type Page } from "@playwright/test";
import { clickNode, waitForNewObserverSession, waitForStableObserver } from "./observer";

const familyFixture = "tests/e2e/fixtures/desktop-family.json";
const warningFixture = "tests/e2e/fixtures/desktop-warning.json";

async function importFixture(page: Page, fixture: string) {
  const previousSession = (await waitForStableObserver(page)).session;
  await page.locator('input[type="file"]').setInputFiles(fixture);
  await waitForNewObserverSession(page, previousSession);
}

test("selected person detail card", async ({ page }) => {
  await page.goto("/");
  await importFixture(page, familyFixture);
  await clickNode(page, 101);
  const detail = page.getByRole("dialog");
  await expect(detail).toContainText("Ada North");

  await expect(detail).toHaveScreenshot("desktop-person-detail.png", {
    animations: "disabled",
    caret: "hide",
  });
});

test("Move mode toolbar", async ({ page }) => {
  await page.goto("/");
  await waitForStableObserver(page);
  await page.getByRole("button", { name: "Move" }).click();
  await expect(page.getByRole("button", { name: "View" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Undo" })).toBeDisabled();
  await expect(page.getByRole("button", { name: "Multi-select" })).toBeVisible();

  const toolbar = page.getByRole("button", { name: "Multi-select" }).locator("..");
  const bounds = await toolbar.boundingBox();
  if (!bounds) throw new Error("Move-mode toolbar is not visible.");
  expect(
    await page.screenshot({
      clip: bounds,
      animations: "disabled",
      caret: "hide",
    })
  ).toMatchSnapshot("desktop-move-toolbar.png");
});

test("import warning banner", async ({ page }) => {
  await page.goto("/");
  await waitForStableObserver(page);
  await importFixture(page, warningFixture);
  const warning = page.getByRole("status");
  await expect(warning).toContainText("Imported with warnings");

  await expect(warning).toHaveScreenshot("desktop-import-warning.png", {
    animations: "disabled",
    caret: "hide",
  });
});
