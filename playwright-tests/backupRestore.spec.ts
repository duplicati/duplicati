import { expect, Page, test } from "@playwright/test";
import fs from "fs/promises";
import path from "path";

const SERVER_URL = process.env.SERVER_URL || "http://localhost:8200/ngclient";
const HOME_URL = `${SERVER_URL}/`;
const LOGIN_URL = `${SERVER_URL}/login`;
const WEBSERVICE_PASSWORD = process.env.WEBSERVICE_PASSWORD || "easy1234";
const BACKUP_NAME = process.env.BACKUP_NAME || "PlaywrightBackup";
const PASSWORD = "the_backup_password_is_really_long_and_safe";
const SOURCE_FOLDER = path.resolve("playwright_source");
const DESTINATION_FOLDER = path.resolve("playwright_destination");
const RESTORE_FOLDER = path.resolve("playwright_restore");
const TEMP_FOLDER = path.resolve("playwright_temp");
const TESTFILE_NAME = "file.txt";
const CONFIG_FILE_PASSWORD = "another_strong_password";
const CONFIG_FILE_NAME = "duplicati-playwright-config.json.aes";

async function writeRandomFile(filepath: string, size: number) {
  await fs.mkdir(path.dirname(filepath), { recursive: true });
  const buffer = Buffer.alloc(size);
  await fs.writeFile(filepath, buffer);
}

test.beforeAll(async () => {
  await fs.rm(SOURCE_FOLDER, { recursive: true, force: true });
  await fs.rm(DESTINATION_FOLDER, { recursive: true, force: true });
  await fs.rm(RESTORE_FOLDER, { recursive: true, force: true });
  await fs.rm(TEMP_FOLDER, { recursive: true, force: true });
  await writeRandomFile(path.join(SOURCE_FOLDER, TESTFILE_NAME), 1024);
  await fs.mkdir(TEMP_FOLDER, { recursive: true });
});

async function clickThreeDotMenu(page: Page, action: string) {
  const backupElement = page
    .locator("div.backup")
    .filter({ hasText: BACKUP_NAME });

  await backupElement
    .locator("button")
    .filter({
      has: page.locator("sh-icon").filter({ hasText: "three-vertical" }),
    })
    .click();

  await backupElement
    .locator("div.options button")
    .filter({ hasText: action })
    .click();
}

async function restoreAndVerify(page: Page) {
  await page.goto(HOME_URL);
  await page.waitForLoadState("networkidle");

  await clickThreeDotMenu(page, "Restore");
  await completeRestoreFlow(page);
}

async function completeRestoreFlow(page: Page) {
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
    .waitFor({ timeout: 60000 });

  const restored = await fs.stat(path.join(RESTORE_FOLDER, "file.txt"));
  expect(restored.isFile()).toBeTruthy();
  await fs.rm(path.join(RESTORE_FOLDER, "file.txt"));
}

async function createBackup(page: Page) {
  await page.goto(HOME_URL);
  await page.waitForLoadState("networkidle");
  await page.locator("div.backup").first().waitFor();

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

  await page
    .locator("footer")
    .filter({
      has: page.locator("button").filter({ hasText: "Create folder" }),
    })
    .locator("button")
    .filter({ hasText: "Create folder" })
    .click();

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
}

async function deleteBackupIfExists(page: Page) {
  await page.goto(HOME_URL);
  await page.waitForLoadState("networkidle");

  // Take a screenshot before waiting for backup elements
  await page.screenshot({
    path: path.join("test-results", "before-backup-wait.png"),
    fullPage: true,
  });

  // Log page content for debugging
  const pageContent = await page.content();
  console.log("Page HTML length:", pageContent.length);
  console.log("Page title:", await page.title());

  // Check if any backup elements exist
  const backupCount = await page.locator("div.backup").count();
  console.log("Number of backup elements found:", backupCount);

  await page.locator("div.backup").first().waitFor();

  // Cleanup existing backup with the same name
  const existingBackupElement = page
    .locator("div.backup")
    .filter({ hasText: BACKUP_NAME });

  if ((await existingBackupElement.count()) > 0) {
    await clickThreeDotMenu(page, "Delete");

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
}

async function runBackup(page: Page) {
  await page.goto(HOME_URL);
  await page.waitForLoadState("networkidle");

  const chipLocator = page
    .locator("div.backup")
    .filter({ hasText: BACKUP_NAME })
    .locator("sh-chip");

  var currentText = await chipLocator.allInnerTexts();

  const backupElement = page
    .locator("div.backup")
    .filter({ hasText: BACKUP_NAME });
  await backupElement.locator("button").filter({ hasText: "Start" }).click();

  // Wait for the chip to be present (assuming it updates after backup)
  await chipLocator.first().waitFor();

  // Check that the text has changed
  const newText = await chipLocator.first().textContent();
  expect(newText).not.toBe(currentText[0]);
}

async function directRestoreFromFiles(page: Page) {
  await page.goto(HOME_URL);
  await page.waitForLoadState("networkidle");
  await page.click("text=Restore");

  const restoreDirectCard = page.locator("sh-card").filter({
    hasText: "Direct restore from backup files",
  });

  restoreDirectCard.locator("button").filter({ hasText: "Start" }).click();

  page
    .locator("div.tile")
    .filter({
      hasText: "File system",
    })
    .click();

  await page
    .locator("button")
    .filter({ hasText: "Manually type path" })
    .click();
  await page.fill("#destination-custom-0-other", DESTINATION_FOLDER);
  await page.locator("button").filter({ hasText: "Test destination" }).click();
  await page.locator("button").filter({ hasText: "Continue" }).click();
  await page.fill("#password", PASSWORD);
  await page.locator("button").filter({ hasText: "Continue" }).click();

  await completeRestoreFlow(page);
}

async function restoreFromConfigFile(page: Page) {
  await page.goto(HOME_URL);
  await page.waitForLoadState("networkidle");

  await clickThreeDotMenu(page, "Export");

  const exportPasswords = page
    .locator("sh-toggle")
    .filter({ hasText: "Export passwords" })
    .locator('input[type="checkbox"]');

  // Wait to ensure the UI is toggled properly
  await page.waitForTimeout(1000);

  if (!(await exportPasswords.isChecked())) {
    await exportPasswords.click();
  }

  const encryptExportedFile = page
    .locator("sh-toggle")
    .filter({ hasText: "Encrypt file" })
    .locator('input[type="checkbox"]');

  if (!(await encryptExportedFile.isChecked())) {
    await encryptExportedFile.click();
  }

  const downloadPromise = page.waitForEvent("download");
  await page.fill("#password", CONFIG_FILE_PASSWORD);
  await page.fill("#repeatPassword", CONFIG_FILE_PASSWORD);
  await page.locator("button").filter({ hasText: "Export" }).click();

  const download = await downloadPromise;
  const downloadPath = path.join(TEMP_FOLDER, CONFIG_FILE_NAME);
  await download.saveAs(downloadPath);

  console.log("Exported config file");

  await page.goto(HOME_URL);
  await page.waitForLoadState("networkidle");
  await page.click("text=Restore");

  const restoreConfigCard = page.locator("sh-card").filter({
    hasText: "Restore from configuration",
  });

  await restoreConfigCard
    .locator("button")
    .filter({ hasText: "Start" })
    .click();

  await page.setInputFiles(
    'input[type="file"][accept=".json,.aes"]',
    downloadPath
  );

  await page.fill("[formcontrolname='passphrase']", CONFIG_FILE_PASSWORD);

  await page
    .locator("app-restore-from-config")
    .locator("button")
    .filter({ hasText: "Restore" })
    .click();

  console.log("Imported configuration, proceeding with restore...");

  await completeRestoreFlow(page);
}

test("backup and restore flow", async ({ page }) => {
  // Enable console logging from the browser
  page.on("console", (msg) => console.log("Browser console:", msg.text()));
  page.on("pageerror", (err) => console.error("Browser error:", err.message));

  await page
    .context()
    .addCookies([
      { name: "default-client", value: "ngclient", url: SERVER_URL },
    ]);
  await page.setDefaultTimeout(30000);
  await test.setTimeout(120000);

  console.log("Navigating to login page...");
  await page.goto(LOGIN_URL);
  await page.waitForLoadState("networkidle");

  // Take screenshot after login page loads
  await page.screenshot({
    path: path.join("test-results", "01-login-page.png"),
    fullPage: true,
  });

  await page.fill("[formcontrolname='pass']", WEBSERVICE_PASSWORD);

  await page.locator("button").filter({ hasText: "Login" }).click();

  console.log("Waiting for page to load...");

  // Take screenshot after login
  await page.screenshot({
    path: path.join("test-results", "02-after-login.png"),
    fullPage: true,
  });

  await page.locator("text=Add backup").waitFor();

  // Take screenshot when home page is ready
  await page.screenshot({
    path: path.join("test-results", "03-home-page-ready.png"),
    fullPage: true,
  });

  // Ensure no existing backup
  console.log("Deleting existing backup if it exists...");
  await deleteBackupIfExists(page);

  // Add backup
  console.log("Creating new backup...");
  await createBackup(page);

  // Run backup
  console.log("Running backup...");
  await runBackup(page);

  // Restore
  console.log("Restoring and verifying backup...");
  await restoreAndVerify(page);

  // Restore directly from backup files
  console.log("Direct restore from backup files...");
  await directRestoreFromFiles(page);

  // Restore from config
  console.log("Restore from configuration file...");
  await restoreFromConfigFile(page);
});
