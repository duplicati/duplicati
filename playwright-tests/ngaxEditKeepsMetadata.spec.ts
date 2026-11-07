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

// The metadata of a backup holds the results of its last run, which the server updates
// while the edit page is open. Sending back the copy the page loaded would make a backup
// that finished in the meantime look as if it never ran (issue #3018).
test("ngax does not send the loaded metadata back when saving an edited backup", async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await login(page);

  // An existing backup, made from the defaults, with the results of an earlier run
  await page.route("**/api/v1/backup/42", async (route) => {
    if (route.request().method() !== "GET") return route.fallback();
    const response = await route.fetch({
      url: route.request().url().replace("/backup/42", "/backupdefaults"),
    });
    const data = await response.json();
    data.Backup.ID = "42";
    data.Backup.Name = "Edited backup";
    data.Backup.TargetURL = "file:///tmp/duplicati-edit-test";
    data.Backup.Sources = ["%HOME%"];
    data.Backup.Settings = [{ Name: "--no-encryption", Value: "true" }];
    data.Backup.Metadata = { LastBackupDate: "20200101T000000Z" };
    await route.fulfill({ response, json: data });
  });

  let saved: any = null;
  await page.route("**/api/v1/backup/42", async (route) => {
    if (route.request().method() !== "PUT") return route.fallback();
    saved = route.request().postDataJSON();
    await route.fulfill({ json: {} });
  });

  await page.goto(`${NGAX_URL}#/edit/42`);
  await page.waitForLoadState("networkidle");
  await page.click("button.step5");
  await page.click("#save");

  await expect.poll(() => saved, { timeout: 15000 }).not.toBeNull();
  expect(saved.Backup.Name).toBe("Edited backup");
  expect(saved.Backup.Metadata ?? null).toBeNull();
});
