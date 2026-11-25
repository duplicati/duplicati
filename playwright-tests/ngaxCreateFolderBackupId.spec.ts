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

// The server masks the passwords in the destination URL of a saved backup, and puts
// them back from the saved backup when the request names it. Creating a missing folder
// must name the backup like the connection test does, or the server gets a URL that
// still holds the placeholders.
test("ngax names the backup when it creates a missing destination folder", async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await login(page);

  await page.route("**/api/v1/backup/42", async (route) => {
    if (route.request().method() !== "GET") return route.fallback();
    const response = await route.fetch({
      url: route.request().url().replace("/backup/42", "/backupdefaults"),
    });
    const data = await response.json();
    data.Backup.ID = "42";
    data.Backup.Name = "Edited backup";
    data.Backup.TargetURL = "file:///tmp/duplicati-missing-folder-test";
    data.Backup.Sources = ["%HOME%"];
    data.Backup.Settings = [{ Name: "--no-encryption", Value: "true" }];
    await route.fulfill({ response, json: data });
  });

  let tests = 0;
  await page.route("**/api/v1/remoteoperation/test**", async (route) => {
    tests++;
    if (tests === 1)
      await route.fulfill({ status: 500, json: { Error: "missing-folder", Code: 500 } });
    else
      await route.fulfill({ json: {} });
  });

  let created: any = null;
  await page.route("**/api/v1/remoteoperation/create", async (route) => {
    created = route.request().postDataJSON();
    await route.fulfill({ json: {} });
  });

  await page.goto(`${NGAX_URL}#/edit/42`);
  await page.waitForLoadState("networkidle");
  await page.click("button.step2");
  await page.click("a.test-connection");

  await page.getByRole("link", { name: "Yes", exact: true }).click();

  await expect.poll(() => created, { timeout: 15000 }).not.toBeNull();
  expect(created.backupId).toBe("42");
});
