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
  // After a successful login the page continues to the new UI, which refreshes the
  // login token while it loads. Leaving it halfway can leave ngax with a token the
  // server already replaced, so stop at an empty page instead.
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

test("ngax runs without jQuery", async ({ page }) => {
  const errors = collectErrors(page);
  await openNgax(page, "home");

  expect(await page.evaluate(() => typeof (window as any).jQuery)).toBe(
    "undefined",
  );
  await expect(page.locator('script[src*="jquery"]')).toHaveCount(0);
  expect(errors).toEqual([]);
});

test("ngax opens and closes the mobile menu", async ({ page }) => {
  const errors = collectErrors(page);
  await page.setViewportSize({ width: 600, height: 900 });
  await openNgax(page, "home");
  const menu = page.locator("#mainmenu");

  await page.click(".menubutton");
  await expect(menu).toHaveClass(/\bmobile-open\b/);
  await expect(menu).toBeVisible();

  // The open menu covers the button, so a click anywhere else closes it
  await page.mouse.click(590, 890);
  await expect(menu).not.toHaveClass(/\bmobile-open\b/);

  expect(errors).toEqual([]);
});

test("ngax opens and closes the three-dot menu and its dialog", async ({
  page,
}) => {
  const errors = collectErrors(page);
  await openNgax(page, "add");
  await page.click("button.step2");
  const menu = page.locator("#threedotmenu_add_destination");
  const dialog = page.locator("div.modal-dialog");

  await page.click("#threedotmenubutton_add_destination");
  await expect(menu).toHaveClass(/\bopen\b/);
  await page.click("#threedotmenubutton_add_destination");
  await expect(menu).not.toHaveClass(/\bopen\b/);

  // A click anywhere else closes it
  await page.click("#threedotmenubutton_add_destination");
  await expect(menu).toHaveClass(/\bopen\b/);
  await page.click("div.step2 h2");
  await expect(menu).not.toHaveClass(/\bopen\b/);

  // A menu item opens a dialog and closes the menu
  await page.click("#threedotmenubutton_add_destination");
  await menu.getByText("Import Destination URL").click();
  await expect(menu).not.toHaveClass(/\bopen\b/);
  await expect(dialog).toBeVisible();
  await expect(dialog.locator(".title")).toHaveText("Import URL");
  await dialog.locator("a.button", { hasText: "Cancel" }).click();
  await expect(dialog).toBeHidden();

  expect(errors).toEqual([]);
});

test("ngax expands a folder in the source tree", async ({ page }) => {
  const errors = collectErrors(page);
  await openNgax(page, "add");
  await page.click("button.step3");

  const computer = page
    .locator("source-folder-picker li")
    .filter({ has: page.locator(':scope > div > span:text-is("Computer")') });
  await computer.locator(":scope > div > a.nav").click();

  // The first child of "Computer" is a drive or the file system root
  const root = computer.locator(":scope > ul > li").first();
  await root.locator(":scope > div > a.nav").click();
  await expect(root.locator(":scope > ul > li").first()).toBeAttached();

  expect(errors).toEqual([]);
});

test("ngax file picker can be resized by dragging its corner", async ({
  page,
}) => {
  const errors = collectErrors(page);
  await openNgax(page, "add");
  await page.click("button.step3");
  const picker = page.locator("div.resizable.filepicker:visible");

  const before = await picker.boundingBox();
  expect(before).not.toBeNull();
  expect(before!.height).toBe(200);

  await page.mouse.move(
    before!.x + before!.width - 4,
    before!.y + before!.height - 4,
  );
  await page.mouse.down();
  await page.mouse.move(
    before!.x + before!.width - 104,
    before!.y + before!.height + 96,
    { steps: 10 },
  );
  await page.mouse.up();

  const after = await picker.boundingBox();
  expect(after!.height).toBeGreaterThan(before!.height + 50);
  expect(after!.width).toBeLessThan(before!.width - 50);

  expect(errors).toEqual([]);
});
