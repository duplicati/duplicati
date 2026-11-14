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

// Calls the server API through the page's own AppService, which holds the login token
function callApi(page: Page, method: "get" | "post" | "delete", url: string, body?: unknown) {
  return page.evaluate(
    ([method, url, body]) =>
      new Promise((resolve, reject) => {
        const appService = (window as any).angular
          .element(document.body)
          .injector()
          .get("AppService");
        appService[method](url, body).then(
          (response: any) => resolve(response.data),
          (error: any) => reject(new Error(`${method} ${url}: ${error.status}`)),
        );
      }),
    [method, url, body] as const,
  );
}

function backupTask(page: Page, name: string) {
  return page.locator("div.tasklist div.task", { hasText: name });
}

// Pressing "Run now" on a backup that is running or already queued adds nothing to the
// queue, so the home page shows the state of the backup instead of the link (issue #3479).
test("ngax shows queued and running backups instead of the run link", async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await login(page);
  await page.goto(`${NGAX_URL}#/home`);
  await page.waitForLoadState("networkidle");

  const source = fs.mkdtempSync(path.join(os.tmpdir(), "duplicati-queued-source-"));
  fs.writeFileSync(path.join(source, "file.txt"), "queued");
  const target = fs.mkdtempSync(path.join(os.tmpdir(), "duplicati-queued-target-"));

  const name = `Queued backup ${Date.now()}`;
  const defaults: any = await callApi(page, "get", "/backupdefaults");
  defaults.Backup.Name = name;
  defaults.Backup.Sources = [source + path.sep];
  defaults.Backup.TargetURL = "file://" + target;
  defaults.Backup.Settings = [{ Name: "--no-encryption", Value: "true" }];
  const created: any = await callApi(page, "post", "/backups", {
    Backup: defaults.Backup,
    Schedule: null,
  });
  const id = String(created.ID);

  try {
    // A paused server keeps the run in the queue
    await callApi(page, "post", "/serverstate/pause");
    await page.reload();
    await page.waitForLoadState("networkidle");

    const task = backupTask(page, name);
    await expect(task.locator("a.action-link", { hasText: "Run now" })).toBeVisible();
    await callApi(page, "post", `/backup/${id}/run`);

    await expect(task.getByText("Queued to run")).toBeVisible({ timeout: 15000 });
    await expect(task.locator("a.action-link", { hasText: "Run now" })).toBeHidden();
    await expect(task.getByText("Running", { exact: true })).toBeHidden();

    // The server reports the backup as the active task while it runs. The page gets
    // the server state over a websocket, so mark the backup as active in each message.
    await page.routeWebSocket(/\/notifications/, (ws) => {
      const server = ws.connectToServer();
      server.onMessage((message) => {
        const data = JSON.parse(String(message));
        data.ActiveTask = { Item1: 999999, Item2: id };
        data.SchedulerQueueIds = [];
        ws.send(JSON.stringify(data));
      });
    });
    await page.reload();
    await page.waitForLoadState("networkidle");

    await expect(task.getByText("Running", { exact: true })).toBeVisible({ timeout: 15000 });
    await expect(task.locator("a.action-link", { hasText: "Run now" })).toBeHidden();
    await expect(task.getByText("Queued to run")).toBeHidden();
  } finally {
    // Let the queued run finish before removing the backup
    await callApi(page, "post", "/serverstate/resume");
    await expect
      .poll(
        async () => {
          const state: any = await callApi(page, "get", "/serverstate");
          return state.ActiveTask == null && state.SchedulerQueueIds.length == 0;
        },
        { timeout: 60000 },
      )
      .toBe(true);
    await callApi(page, "delete", `/backup/${id}?delete-local-db=true&delete-remote-files=false`);
    fs.rmSync(source, { recursive: true, force: true });
    fs.rmSync(target, { recursive: true, force: true });
  }
});
