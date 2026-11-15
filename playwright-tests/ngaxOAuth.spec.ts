import { expect, Page, test } from "@playwright/test";

const SERVER_URL = process.env.SERVER_URL || "http://localhost:8200";
const LOGIN_URL = `${SERVER_URL}/login.html`;
const NGAX_URL = `${SERVER_URL}/ngax/index.html`;
const WEBSERVICE_PASSWORD = process.env.WEBSERVICE_PASSWORD || "easy1234";
const OAUTH_SERVICE = "https://duplicati-oauth-handler.appspot.com/";

// Collects console errors and uncaught exceptions from the page
function collectErrors(page: Page) {
  const errors: string[] = [];
  page.on("console", (message) => {
    if (message.type() === "error") errors.push(message.text());
  });
  page.on("pageerror", (error) => errors.push(error.message));
  return errors;
}

async function login(page: Page) {
  // Stop at an empty page after the login, so ngax keeps the token it gets
  const rootUrl = `${SERVER_URL}/`;
  await page.route(rootUrl, (route) =>
    route.fulfill({ contentType: "text/html", body: "<html></html>" }),
  );
  await page.goto(LOGIN_URL);
  await page.fill("#login-password", WEBSERVICE_PASSWORD);
  await page.click("#login-button");
  await page.waitForURL(rootUrl);
  await page.unroute(rootUrl);
}

test("ngax gets the AuthID from the OAuth service", async ({ page, context }) => {
  await login(page);
  const errors = collectErrors(page);

  // Stands in for the OAuth service: the login window gets an empty page, and the
  // JSONP request that waits for the token gets the AuthID
  const fetches: string[] = [];
  await context.route(`${OAUTH_SERVICE}**`, async (route) => {
    const url = new URL(route.request().url());
    if (url.pathname === "/fetch") {
      fetches.push(url.search);
      const callback = url.searchParams.get("callback");
      await route.fulfill({
        contentType: "application/javascript",
        body: `${callback}(${JSON.stringify({ authid: "test-authid" })});`,
      });
    } else {
      await route.fulfill({ contentType: "text/html", body: "<html></html>" });
    }
  });

  await page.goto(`${NGAX_URL}#/add`);
  await page.waitForLoadState("networkidle");
  await page.click("button.step2");
  await page.selectOption("#type", "googledrive");
  await page.locator('a[ng-click="oauth_start_token_creation()"]').click();

  // The first check runs 6 seconds after the click
  await expect(page.locator("#oauth_authid")).toHaveValue("test-authid", {
    timeout: 20000,
  });
  expect(fetches.length).toBeGreaterThan(0);
  expect(fetches[0]).toContain("token=");
  expect(errors).toEqual([]);
});
