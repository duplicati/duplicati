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
  await login(page);
});

const PAGES: { route: string; check: (page: Page) => Promise<void> }[] = [
  {
    route: "home",
    check: async (page) => {
      // The list is empty (and has no height) when there are no backups
      await expect(
        page.locator('div.home div.tasks[ng-controller="HomeController"]'),
      ).toBeAttached();
    },
  },
  {
    route: "addstart",
    check: async (page) => {
      await expect(page.locator("h1")).toHaveText("Add a new backup");
    },
  },
  {
    route: "restorestart",
    check: async (page) => {
      await expect(page.locator("h1")).toHaveText(
        "Where do you want to restore from?",
      );
    },
  },
  {
    route: "import",
    check: async (page) => {
      await expect(page.locator("h1").first()).toHaveText(
        "Import backup configuration",
      );
    },
  },
  {
    route: "settings",
    check: async (page) => {
      await expect(page.locator("h1")).toHaveText("Settings");
      await expect(page.locator("#themedisplay")).toBeVisible();
    },
  },
  {
    route: "log",
    check: async (page) => {
      await expect(page.locator("h1")).toHaveText("Log data from the server");
    },
  },
  {
    route: "commandline",
    check: async (page) => {
      // The list of commands is loaded from the server
      await expect(
        page.locator("select#command option", { hasText: "backup" }).first(),
      ).toBeAttached();
    },
  },
  {
    route: "about",
    check: async (page) => {
      await expect(page.locator("h1")).toHaveText("About Duplicati");
    },
  },
];

for (const { route, check } of PAGES) {
  test(`ngax page #/${route} renders without errors`, async ({ page }) => {
    const errors = collectErrors(page);
    await openNgax(page, route);
    await check(page);
    expect(errors).toEqual([]);
  });
}

// The colors come from the built ngax/styles/default.css and dark.css
const THEMES = [
  {
    theme: "default",
    stylesheet: "styles/default.css",
    bodyColor: "rgb(80, 80, 80)",
    headerBackground: "rgb(237, 237, 237)",
  },
  {
    theme: "dark",
    stylesheet: "styles/dark.css",
    bodyColor: "rgb(176, 176, 176)",
    headerBackground: "rgb(51, 51, 51)",
  },
];

test("ngax applies the default and dark theme stylesheets", async ({ page }) => {
  const errors = collectErrors(page);
  await openNgax(page, "settings");

  for (const { theme, stylesheet, bodyColor, headerBackground } of THEMES) {
    // Selecting a theme previews it; nothing is saved
    await page.selectOption("#themedisplay", theme);

    // index.html also has disabled links to both files; the active one is set through ng-href
    await expect(page.locator("link[ng-href]")).toHaveAttribute(
      "href",
      stylesheet,
    );
    await expect(page.locator("body")).toHaveCSS("color", bodyColor);
    await expect(page.locator("body")).toHaveCSS(
      "font-family",
      '"Clear Sans", sans-serif',
    );
    await expect(page.locator("div.header").first()).toHaveCSS(
      "background-color",
      headerBackground,
    );
  }

  expect(errors).toEqual([]);
});

const THEME_STORAGE_KEY = 'duplicati:ngax:theme';

async function saveTheme(page: Page, theme: string) {
  await page.selectOption('#themedisplay', theme);
  const saved = page.waitForResponse(
    (response) => response.url().endsWith('/api/v1/serversettings') && response.request().method() === 'PATCH'
  );
  await page.locator('#save').click();
  expect((await saved).ok()).toBe(true);
}

test('ngax keeps a saved theme when the legacy cookie disappears or changes', async ({ page, context }) => {
  await openNgax(page, 'settings');
  await saveTheme(page, 'dark');
  await context.clearCookies({ name: 'current-theme' });
  await page.reload();
  await expect(page.locator('link[ng-href]')).toHaveAttribute('href', 'styles/dark.css');
  // Cookies are shared between ports on the same host; localStorage is not.
  await context.addCookies([{ name: 'current-theme', value: 'default', url: `${SERVER_URL}/ngax/` }]);
  await page.reload();
  await expect(page.locator('link[ng-href]')).toHaveAttribute('href', 'styles/dark.css');
  expect(await page.evaluate((key) => localStorage.getItem(key), THEME_STORAGE_KEY)).toBe('dark');
});

test('ngax migrates an existing theme cookie into browser storage', async ({ page, context }) => {
  await context.addCookies([{ name: 'current-theme', value: 'dark', url: `${SERVER_URL}/ngax/` }]);
  await openNgax(page, 'settings');
  await expect(page.locator('link[ng-href]')).toHaveAttribute('href', 'styles/dark.css');
  expect(await page.evaluate((key) => localStorage.getItem(key), THEME_STORAGE_KEY)).toBe('dark');
  await context.clearCookies({ name: 'current-theme' });
  await page.reload();
  await expect(page.locator('link[ng-href]')).toHaveAttribute('href', 'styles/dark.css');
});

test('ngax can save its theme when browser storage rejects the write', async ({ page }) => {
  await page.addInitScript((key) => {
    const getItem = Storage.prototype.getItem;
    const setItem = Storage.prototype.setItem;
    Storage.prototype.getItem = function (name) {
      if (name === key) throw new DOMException('Storage unavailable', 'SecurityError');
      return getItem.call(this, name);
    };
    Storage.prototype.setItem = function (name, value) {
      if (name === key) throw new DOMException('Storage unavailable', 'SecurityError');
      return setItem.call(this, name, value);
    };
  }, THEME_STORAGE_KEY);
  await openNgax(page, 'settings');
  await saveTheme(page, 'dark');
  await page.reload();
  await expect(page.locator('link[ng-href]')).toHaveAttribute('href', 'styles/dark.css');
});
