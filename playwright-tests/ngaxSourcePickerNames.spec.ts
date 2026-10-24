import { expect, Page, test } from "@playwright/test";

const SERVER_URL = process.env.SERVER_URL || "http://localhost:8200";
const LOGIN_URL = `${SERVER_URL}/login.html`;
const NGAX_URL = `${SERVER_URL}/ngax/index.html`;
const WEBSERVICE_PASSWORD = process.env.WEBSERVICE_PASSWORD || "easy1234";

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

test("ngax source picker shows folder names when the system info comes late", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await login(page);
  // The server lists the user's home folder as %HOME% on every platform
  const source = "%HOME%";

  // Answer the system info only after the source picker has its folder list,
  // so the picker gets the folders before it knows the directory separator
  const folderList = page.waitForResponse("**/api/v1/filesystem?*");
  await page.route("**/api/v1/systeminfo*", async (route) => {
    await folderList;
    await page.waitForTimeout(500);
    await route.continue();
  });
  await page.route("**/api/v1/backupdefaults", async (route) => {
    const response = await route.fetch();
    const defaults = await response.json();
    defaults.Backup.Sources = [source];
    await route.fulfill({ response, json: defaults });
  });

  await page.goto(`${NGAX_URL}#/add`);
  await page.waitForLoadState("networkidle");
  await page.click("button.step3");

  const sourceData = page
    .locator("source-folder-picker li")
    .filter({ has: page.locator(':scope > div > span:text-is("Source data")') });
  await expect(sourceData.locator(":scope > ul > li > div > span")).toHaveText([
    "Home",
  ]);
});
