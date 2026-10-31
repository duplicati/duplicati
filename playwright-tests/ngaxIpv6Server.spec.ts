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

// An IPv6 address contains colons, so the destination URL must keep it in brackets
// for the server name and the port to be told apart (issue #3959).
test("ngax keeps an IPv6 server address in the destination URL", async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await login(page);

  await page.route("**/api/v1/backup/42", async (route) => {
    if (route.request().method() !== "GET") return route.fallback();
    const response = await route.fetch({
      url: route.request().url().replace("/backup/42", "/backupdefaults"),
    });
    const data = await response.json();
    data.Backup.ID = "42";
    data.Backup.Name = "IPv6 backup";
    data.Backup.TargetURL = "ssh://[2001:db8::2]:2222/backups?auth-username=user";
    data.Backup.Sources = ["%HOME%"];
    data.Backup.Settings = [{ Name: "--no-encryption", Value: "true" }];
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
  await page.click("button.step2");

  // The stored URL is read back into its parts
  await expect(page.locator("#generic_server")).toHaveValue("[2001:db8::2]");
  await expect(page.locator("#generic_port")).toHaveValue("2222");
  await expect(page.locator("#generic_path")).toHaveValue("backups");

  // An address typed without brackets gets them in the URL
  await page.fill("#generic_server", "2001:db8::3");
  await page.click("button.step5");
  await page.click("#save");

  await expect.poll(() => saved, { timeout: 15000 }).not.toBeNull();
  expect(saved.Backup.TargetURL).toMatch(/^ssh:\/\/\[2001:db8::3\]:2222\/backups\?/);
});
