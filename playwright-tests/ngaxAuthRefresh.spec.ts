import { expect, test } from "@playwright/test";

const NGAX_URL = process.env.NGAX_URL || "http://localhost:8200/ngax/index.html";

test("ngax redirects to login on terminal refresh failures", async ({ page }) => {
  let refreshCalls = 0;

  await page.route("**/api/v1/auth/refresh", async (route) => {
    refreshCalls++;
    await route.fulfill({
      status: 415,
      contentType: "application/json",
      body: JSON.stringify({ Error: "Unsupported Media Type" }),
    });
  });

  await page.goto(NGAX_URL, { waitUntil: "domcontentloaded" });

  // The app should immediately abandon refresh retries and go to login.
  await page.waitForURL(/\/login\.html(\?.*)?$/, { timeout: 15000 });

  // Give it a short window and ensure no runaway retry loop occurs.
  await page.waitForTimeout(1500);
  expect(refreshCalls).toBeLessThanOrEqual(3);
});

test("ngax keeps retry behavior for transient refresh failures", async ({ page }) => {
  let refreshCalls = 0;

  await page.route("**/api/v1/auth/refresh", async (route) => {
    refreshCalls++;
    await route.fulfill({
      status: 503,
      contentType: "application/json",
      body: JSON.stringify({ Error: "Service Unavailable" }),
    });
  });

  await page.goto(NGAX_URL, { waitUntil: "domcontentloaded" });

  // For transient failures, the app should not force a login redirect.
  await page.waitForTimeout(2500);
  await expect(page).not.toHaveURL(/\/login\.html(\?.*)?$/);

  // The reconnect flow should attempt refresh again rather than terminating.
  expect(refreshCalls).toBeGreaterThanOrEqual(2);
});
