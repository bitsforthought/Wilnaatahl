import type { Page } from "@playwright/test";
import { waitForNewObserverSession, waitForStableObserver } from "./observer";

export async function uploadFixture(page: Page, fixture: string): Promise<void> {
  await page.locator('input[type="file"]').setInputFiles(fixture);
}

export async function importFixture(page: Page, fixture: string): Promise<void> {
  const previousSession = (await waitForStableObserver(page)).session;
  await uploadFixture(page, fixture);
  await waitForNewObserverSession(page, previousSession);
}
