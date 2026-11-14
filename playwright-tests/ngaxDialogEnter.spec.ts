import { expect, Page, test } from "@playwright/test";

const SERVER_URL = process.env.SERVER_URL || "http://localhost:8200";
const LOGIN_URL = `${SERVER_URL}/login.html`;
const NGAX_URL = `${SERVER_URL}/ngax/index.html`;
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

async function login(page: Page) {
  // Stop at an empty page after the login, so the new UI does not refresh the token
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

async function openNgax(page: Page, route: string) {
  await page.goto(`${NGAX_URL}#/${route}`);
  await page.waitForLoadState("networkidle");
}

test.beforeEach(async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await login(page);
});

test("ngax dismisses a single-button dialog with Enter", async ({ page }) => {
  const errors = collectErrors(page);
  await openNgax(page, "add");
  const dialog = page.locator("div.modal-dialog");

  // Saving a backup without a name shows a dialog with only an OK button
  await page.click("button.step5");
  await page.click("#save");
  await expect(dialog).toBeVisible();
  await expect(dialog.locator(".title")).toHaveText("Missing name");
  await expect(dialog.locator("a.button")).toHaveCount(1);

  // The Save button still has the focus; Enter must close the dialog, not save again
  await page.keyboard.press("Enter");
  await expect(dialog).toBeHidden();
  await page.waitForTimeout(500);
  await expect(dialog).toBeHidden();

  expect(errors).toEqual([]);
});

test("ngax keeps a dialog with several buttons open on Enter", async ({
  page,
}) => {
  const errors = collectErrors(page);
  await openNgax(page, "add");
  await page.click("button.step2");
  const menu = page.locator("#threedotmenu_add_destination");
  const dialog = page.locator("div.modal-dialog");

  await page.click("#threedotmenubutton_add_destination");
  await menu.getByText("Import Destination URL").click();
  await expect(dialog).toBeVisible();
  await expect(dialog.locator("a.button")).toHaveCount(2);

  await page.keyboard.press("Enter");
  await page.waitForTimeout(500);
  await expect(dialog).toBeVisible();

  await dialog.locator("a.button", { hasText: "Cancel" }).click();
  await expect(dialog).toBeHidden();
  expect(errors).toEqual([]);
});
