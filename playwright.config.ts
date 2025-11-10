import { defineConfig } from "@playwright/test";

export default defineConfig({
  use: {
    baseURL: "http://localhost:8200",
    headless: true,
    // Capture screenshots on failure
    screenshot: "only-on-failure",
    // Capture video on failure
    video: "retain-on-failure",
    // Capture trace on failure for detailed debugging
    trace: "retain-on-failure",
  },
  testDir: "playwright-tests",
  timeout: 120000,
  workers: 1,
  reporter: process.env.CI ? [["html"], ["list"], ["github"]] : "list",
  outputDir: "test-results/",
});
