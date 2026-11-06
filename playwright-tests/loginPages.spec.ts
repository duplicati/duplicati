import { expect, Page, test } from "@playwright/test";

const SERVER_URL = process.env.SERVER_URL || "http://localhost:8200";
const WEBSERVICE_PASSWORD = process.env.WEBSERVICE_PASSWORD || "easy1234";

// Collects console errors and uncaught exceptions from the page
function collectErrors(page: Page) {
  const errors: string[] = [];
  page.on("console", (message) => {
    if (message.type() === "error") errors.push(message.text());
  });
  page.on("pageerror", (error) => errors.push(error.message));
  return errors;
}

// Resolves with the text of the next alert, and dismisses it
function nextAlert(page: Page) {
  return new Promise<string>((resolve) =>
    page.once("dialog", async (dialog) => {
      const message = dialog.message();
      await dialog.dismiss();
      resolve(message);
    }),
  );
}

// Stops at an empty page instead of loading the UI the login continues to
async function stubRoot(page: Page) {
  await page.route(`${SERVER_URL}/`, (route) =>
    route.fulfill({ contentType: "text/html", body: "<html></html>" }),
  );
}

for (const name of ["login", "signin"]) {
  test(`${name}.html runs without jQuery`, async ({ page }) => {
    const errors = collectErrors(page);
    // A failed refresh keeps the sign-in page open
    await page.route("**/api/v1/auth/refresh", (route) =>
      route.fulfill({ status: 401, contentType: "application/json", body: JSON.stringify({ Error: "No refresh" }) }),
    );
    if (name === "signin") page.on("dialog", (dialog) => dialog.dismiss());

    await page.goto(`${SERVER_URL}/${name}.html`);

    expect(await page.evaluate(() => typeof (window as any).jQuery)).toBe("undefined");
    // The warning is hidden by the page script once it runs
    await expect(page.locator("#no-js-warning")).toBeHidden();
    // The browser logs the refresh answered above as a failed resource
    expect(errors.filter((e) => !e.includes("status of 401"))).toEqual([]);
  });
}

test("login.html shows the server error for a wrong password", async ({ page }) => {
  await page.goto(`${SERVER_URL}/login.html`);
  await page.fill("#login-password", "not-the-password");
  const alert = nextAlert(page);
  await page.click("#login-button");

  expect(await alert).toBe("Login failed: Failed to log in");
  await expect(page).toHaveURL(`${SERVER_URL}/login.html`);
});

test("login.html shows the status text when the error is not JSON", async ({ page }) => {
  await page.route("**/api/v1/auth/login", (route) =>
    route.fulfill({ status: 500, contentType: "text/plain", body: "oops" }),
  );
  await page.goto(`${SERVER_URL}/login.html`);
  await page.fill("#login-password", "anything");
  const alert = nextAlert(page);
  await page.click("#login-button");

  expect(await alert).toBe("Login failed: Internal Server Error");
});

test("login.html signs in and keeps the refresh nonce", async ({ page }) => {
  await stubRoot(page);
  await page.goto(`${SERVER_URL}/login.html`);
  await page.fill("#login-password", WEBSERVICE_PASSWORD);
  await page.press("#login-password", "Enter");

  await page.waitForURL(`${SERVER_URL}/`);
  expect(await page.evaluate(() => localStorage.getItem("v1:persist:duplicati:refreshNonce"))).toBeTruthy();
});

test("signin.html shows the server error for a bad token", async ({ page }) => {
  await page.route("**/api/v1/auth/refresh", (route) =>
    route.fulfill({ status: 401, contentType: "application/json", body: JSON.stringify({ Error: "No refresh" }) }),
  );
  const alert = nextAlert(page);
  await page.goto(`${SERVER_URL}/signin.html?token=not-a-token`);

  expect(await alert).toMatch(/^Signin failed: /);
  await expect(page.locator("#signin-token")).toHaveValue("not-a-token");
});

test("signin.html continues when the refresh succeeds", async ({ page }) => {
  await stubRoot(page);
  await page.route("**/api/v1/auth/refresh", (route) =>
    route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ RefreshNonce: "nonce-from-refresh" }) }),
  );
  await page.goto(`${SERVER_URL}/signin.html`);

  await page.waitForURL(`${SERVER_URL}/`);
  expect(await page.evaluate(() => localStorage.getItem("v1:persist:duplicati:refreshNonce"))).toBe("nonce-from-refresh");
});
