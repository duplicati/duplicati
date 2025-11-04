import { defineConfig } from "@playwright/test";

export default defineConfig({
  use: {
    baseURL: "http://localhost:8200",
    headless: true,
  },
  testDir: "playwright-tests",
  timeout: 120000,
  workers: 1,
  reporter: process.env.CI ? "html" : "list",
  outputDir: "test-results/",
});
