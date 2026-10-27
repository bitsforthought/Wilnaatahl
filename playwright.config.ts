import { defineConfig, devices } from "@playwright/test";

export default defineConfig({
  testDir: "./tests/e2e",
  testMatch: "**/*.spec.ts",
  fullyParallel: false,
  workers: 1,
  reporter: [["list"], ["html", { outputFolder: ".e2e-output/playwright-report", open: "never" }]],
  outputDir: ".e2e-output/test-results",
  use: {
    baseURL: "http://127.0.0.1:4173",
    browserName: "chromium",
    viewport: { width: 1280, height: 800 },
    deviceScaleFactor: 1,
    locale: "en-CA",
    timezoneId: "America/Vancouver",
    colorScheme: "light",
    reducedMotion: "reduce",
    launchOptions: {
      args: ["--use-gl=angle", "--use-angle=swiftshader"],
    },
    screenshot: "only-on-failure",
    trace: "retain-on-failure",
  },
  webServer: {
    command:
      "node node_modules/vite/bin/vite.js preview --host 0.0.0.0 --port 4173 --strictPort --outDir dist-e2e",
    url: "http://127.0.0.1:4173",
    reuseExistingServer: false,
    timeout: 30_000,
  },
  projects: [
    {
      name: "desktop",
      testIgnore: ["**/touch.spec.ts", "**/screenshots-mobile.spec.ts"],
      use: { ...devices["Desktop Chrome"], viewport: { width: 1280, height: 800 } },
    },
    {
      name: "mobile",
      testMatch: ["**/touch.spec.ts", "**/screenshots-mobile.spec.ts"],
      use: {
        ...devices["Pixel 7"],
        viewport: { width: 390, height: 844 },
        deviceScaleFactor: 1,
      },
    },
  ],
});
