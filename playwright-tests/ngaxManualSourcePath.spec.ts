import { expect, Page, test } from "@playwright/test";
import * as fs from "fs";
import * as os from "os";
import * as path from "path";

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

// The paths are checked by the server, so this test needs a server on the same machine
const folder = fs.mkdtempSync(path.join(os.tmpdir(), "ngax-manual-source-"));
const file = path.join(folder, "file.txt");
fs.writeFileSync(file, "data");
const missing = path.join(folder, "missing");

test.afterAll(() => fs.rmSync(folder, { recursive: true, force: true }));

async function openSourceStep(page: Page) {
  await page.setViewportSize({ width: 1280, height: 900 });
  await login(page);
  await page.goto(`${NGAX_URL}#/add`);
  await page.waitForLoadState("networkidle");
  await page.click("button.step3");
  // Edit the sources as text, so the added paths can be read back as entered
  await page.evaluate(() => {
    const scope = (window as any).angular
      .element(document.getElementById("SourcePathList"))
      .scope();
    scope.$apply(() => (scope.EditSourceAdvanced = true));
  });
}

async function addManualPath(page: Page, value: string) {
  await page.fill("#sourcePath", value);
  await page.click("#sourceFolderPathAdd");
}

const sourceList = (page: Page) => page.locator("#SourcePathList");

test("ngax adds a typed folder without asking whether it is a file", async ({
  page,
}) => {
  await openSourceStep(page);

  await addManualPath(page, folder);

  await expect(sourceList(page)).toHaveValue(folder + path.sep);
  await expect(page.getByText("Include a file?")).toHaveCount(0);
});

test("ngax adds a typed file without asking whether it is a file", async ({
  page,
}) => {
  await openSourceStep(page);

  await addManualPath(page, file);

  await expect(sourceList(page)).toHaveValue(file);
  await expect(page.getByText("Include a file?")).toHaveCount(0);
});

test("ngax still asks about a typed path that does not exist", async ({
  page,
}) => {
  await openSourceStep(page);

  await addManualPath(page, missing);

  await expect(page.getByText("Include a file?")).toBeVisible();
  await expect(sourceList(page)).toHaveValue("");
});
