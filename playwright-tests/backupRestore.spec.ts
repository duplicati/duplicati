import { expect, test } from "@playwright/test";
import fs from "fs/promises";
import path from "path";

const SERVER_URL = process.env.SERVER_URL || "http://localhost:8200";
const SPA_PATH = "/ngclient";
const HOME_URL = `${SERVER_URL}${SPA_PATH}/index.html`;
const LOGIN_URL = `${SERVER_URL}/login.html`;
const WEBSERVICE_PASSWORD = "easy1234";
const BACKUP_NAME = "PlaywrightBackup";
const PASSWORD = "the_backup_password_is_really_long_and_safe";
const SOURCE_FOLDER = path.resolve("playwright_source");
const DESTINATION_FOLDER = path.resolve("playwright_destination");
const RESTORE_FOLDER = path.resolve("playwright_restore");
const TESTFILE_NAME = "file.txt";

async function writeRandomFile(filepath: string, size: number) {
  await fs.mkdir(path.dirname(filepath), { recursive: true });
  const buffer = Buffer.alloc(size);
  await fs.writeFile(filepath, buffer);
}

test.beforeAll(async () => {
  await fs.rm(SOURCE_FOLDER, { recursive: true, force: true });
  await fs.rm(DESTINATION_FOLDER, { recursive: true, force: true });
  await fs.rm(RESTORE_FOLDER, { recursive: true, force: true });
  await writeRandomFile(path.join(SOURCE_FOLDER, TESTFILE_NAME), 1024);
  await fs.mkdir(DESTINATION_FOLDER, { recursive: true });
});

test("backup and restore flow", async ({ page }) => {
  await page
    .context()
    .addCookies([
      { name: "default-client", value: "ngclient", url: SERVER_URL },
    ]);
  await page.goto(LOGIN_URL);
  await page.fill("#login-password", WEBSERVICE_PASSWORD);
  await page.click("#login-button");

  await page.waitForURL(HOME_URL);
  await page.waitForLoadState("networkidle");
  await page.locator("div.backup").first().waitFor();

  // Cleanup existing backup with the same name
  const existingBackupElement = page
    .locator("div.backup")
    .filter({ hasText: "PlaywrightBackup" });

  if ((await existingBackupElement.count()) > 0) {
    await existingBackupElement
      .locator("button")
      .filter({
        has: page.locator("sh-icon").filter({ hasText: "three-vertical" }),
      })
      .click();

    await page
      .locator("div.options button")
      .filter({ hasText: "Delete" })
      .click();

    const deleteDatabase = page
      .locator("sh-checkbox")
      .filter({ hasText: "Delete local database" })
      .locator('input[type="checkbox"]');
    if (!(await deleteDatabase.isChecked())) {
      await deleteDatabase.click();
    }

    await page.locator("button").filter({ hasText: "Delete backup" }).click();
    await page.locator("text=Confirm delete").waitFor();

    await page
      .locator("footer")
      .filter({
        has: page.locator("button").filter({ hasText: "Delete backup" }),
      })
      .locator("button")
      .filter({ hasText: "Delete backup" })
      .click();

    await existingBackupElement.waitFor({ state: "detached" });
  }

  // Add backup
  await page.click("text=Add backup");
  await page.locator("button").filter({ hasText: "Add a new backup" }).click();
  await page.fill("[formcontrolname='name']", BACKUP_NAME);
  await page.fill("[formcontrolname='password']", PASSWORD);
  await page.fill("[formcontrolname='repeatPassword']", PASSWORD);
  await page.locator("button").filter({ hasText: "Continue" }).click();
  await page
    .locator(
      'app-destination-list-item:has-text("File system") button:has-text("Choose")'
    )
    .click();
  await page
    .locator("button")
    .filter({ hasText: "Manually type path" })
    .click();
  await page.fill("#destination-custom-0-other", DESTINATION_FOLDER);
  await page.locator("button").filter({ hasText: "Test destination" }).click();
  await page.locator("button").filter({ hasText: "Continue" }).click();
  await page
    .getByPlaceholder("Add a direct path")
    .fill(SOURCE_FOLDER + path.sep);
  await page
    .locator("button")
    .filter({ has: page.locator("sh-icon").filter({ hasText: "plus" }) })
    .click();
  await page.locator("button").filter({ hasText: "Continue" }).click();

  const useScheduleRun = page
    .locator("sh-toggle")
    .filter({ hasText: "Automatically run backups" })
    .locator('input[type="checkbox"]');

  if (await useScheduleRun.isChecked()) {
    await useScheduleRun.click();
  }
  await page.locator("button").filter({ hasText: "Continue" }).click();
  await page.locator("button").filter({ hasText: "Submit" }).click();

  // Run backup
  const backupElement = page
    .locator("div.backup")
    .filter({ hasText: "PlayWrightBackup" });
  await backupElement.locator("button").filter({ hasText: "Start" }).click();

  await page
    .locator("div.backup")
    .filter({ hasText: "PlaywrightBackup" })
    .locator("sh-chip")
    .filter({ hasText: "1 Version" })
    .waitFor();

  // Restore
  backupElement
    .locator("button")
    .filter({
      has: page.locator("sh-icon").filter({ hasText: "three-vertical" }),
    })
    .click();

  await page
    .locator("div.options button")
    .filter({ hasText: "Restore" })
    .click();

  await page.locator("div.text").filter({ hasText: TESTFILE_NAME }).click();
  await page.locator("button").filter({ hasText: "Continue" }).click();

  await page.locator("sh-radio").filter({ hasText: "Pick location" }).click();
  await page
    .locator("button")
    .filter({ hasText: "Manually type path" })
    .click();
  await page.fill("[formcontrolname='restoreFromPath']", RESTORE_FOLDER);

  await page
    .locator("sh-radio")
    .filter({
      hasText: "Save different versions",
    })
    .click();

  await page.locator("button").filter({ hasText: "Submit" }).click();

  await page
    .locator("sh-card")
    .filter({ hasText: "Restore completed" })
    .waitFor();

  const restored = await fs.stat(path.join(RESTORE_FOLDER, "file.txt"));
  expect(restored.isFile()).toBeTruthy();
});
