import argparse
import os
import sys
import shutil
import errno
import time
import hashlib
from selenium import webdriver
from selenium.webdriver.common.by import By
from selenium.webdriver.support.ui import WebDriverWait
from selenium.webdriver.support import expected_conditions

parser = argparse.ArgumentParser()
parser.add_argument(
    "--headless", action='store_true'
)
parser.add_argument(
    "--no-headless", dest='headless', action='store_false'
)
parser.add_argument(
    "--use-chrome", action='store_true'
)
parser.add_argument(
    "--chrome-path"
)

parser.set_defaults(headless=True)
cmdopt = parser.parse_args()

if "TRAVIS_BUILD_NUMBER" in os.environ:
    from selenium.webdriver.firefox.options import Options
    if "SAUCE_USERNAME" not in os.environ:
        print("No sauce labs login credentials found. Stopping tests...")
        sys.exit(0)

    capabilities = {'browserName': "firefox"}
    capabilities['platform'] = "Windows 7"
    capabilities['version'] = "48.0"
    capabilities['screenResolution'] = "1280x1024"
    capabilities["build"] = os.environ["TRAVIS_BUILD_NUMBER"]
    capabilities["tunnel-identifier"] = os.environ["TRAVIS_JOB_NUMBER"]

    # connect to sauce labs
    username = os.environ["SAUCE_USERNAME"]
    access_key = os.environ["SAUCE_ACCESS_KEY"]
    hub_url = "%s:%s@localhost:4445" % (username, access_key)
    driver = webdriver.Remote(command_executor="http://%s/wd/hub" % hub_url, desired_capabilities=capabilities)
elif cmdopt.use_chrome:
    print("using LOCAL Chrome webdriver")
    from selenium.webdriver.chrome.options import Options
    chr_opt = Options()

    if cmdopt.chrome_path is None:
        import chromedriver_autoinstaller
        chromedriver_autoinstaller.install()
    else:
        chr_opt.binary_location = cmdopt.chrome_path

    opt = ["--ignore-certificate-errors", "--window-size=1280,800" ]
    if cmdopt.headless: opt += ["--headless"]
    for o in opt: chr_opt.add_argument(o)
    chr_opt.set_capability('goog:loggingPrefs', { 'browser':'ALL' })
    driver = webdriver.Chrome(options=chr_opt)
else:
    from selenium.webdriver.firefox.options import Options
    print("Using LOCAL Firefox webdriver")
    options = Options()
    options.set_preference("intl.accept_languages", "en")
    options.headless = cmdopt.headless
    driver = webdriver.Firefox(options=options)

def write_random_file(size, filename):
    if not os.path.exists(os.path.dirname(filename)):
        try:
            os.makedirs(os.path.dirname(filename))
        except OSError as exc:  # Guard against race condition
            if exc.errno != errno.EEXIST:
                raise

    with open(filename, 'wb') as fout:
        fout.write(os.urandom(size))


def sha1_file(filename):
    BLOCKSIZE = 65536
    hasher = hashlib.sha1()
    with open(filename, 'rb') as afile:
        buf = afile.read(BLOCKSIZE)
        while len(buf) > 0:
            hasher.update(buf)
            buf = afile.read(BLOCKSIZE)

    return hasher.hexdigest()


def sha1_folder(folder):
    sha1_dict = {}
    for root, dirs, files in os.walk(folder):
        for filename in files:
            file_path = os.path.join(root, filename)
            sha1 = sha1_file(file_path)
            relative_file_path = os.path.relpath(file_path, folder)
            sha1_dict.update({relative_file_path: sha1})

    return sha1_dict


def wait_for_text(xpath, text, timeout=10):
    WebDriverWait(driver, timeout).until(expected_conditions.text_to_be_present_in_element((By.XPATH, xpath), text))

def wait_for_load(by, target, timeout=10):
    return WebDriverWait(driver, timeout).until(expected_conditions.presence_of_element_located((by, target)))

def wait_for_clickable(by, target, timeout=10):
    WebDriverWait(driver, timeout).until(expected_conditions.presence_of_element_located((by, target)))
    return WebDriverWait(driver, timeout).until(expected_conditions.element_to_be_clickable((by, target)))

def wait_for_redirect(expected_url, timeout=10):
    WebDriverWait(driver, timeout).until(lambda driver: driver.current_url == expected_url)

def wait_for_title(title, timeout=10):
    WebDriverWait(driver, timeout).until(lambda driver: title in driver.title)

def runTests():
    HOME_URL = "http://localhost:8200/ngax/index.html"
    LOGIN_URL = "http://localhost:8200/login.html"
    PRELOAD_URLS = [
        "http://localhost:8200/ngax/index.html#/addstart",
        "http://localhost:8200/ngax/index.html#/add",
        "http://localhost:8200/ngax/index.html#/restorestart"
        "http://localhost:8200/ngax/index.html#/restoredirect"
        "http://localhost:8200/ngax/index.html#/"
    ]
    WEBSERVICE_PASSWORD = "easy1234"
    BACKUP_NAME = "BackupName"
    PASSWORD = "the_backup_password_is_really_long_and_safe"
    SOURCE_FOLDER = os.path.abspath("duplicati_gui_test_source")
    DESTINATION_FOLDER = os.path.abspath("duplicati_gui_test_destination")
    DESTINATION_FOLDER_DIRECT_RESTORE = os.path.abspath("duplicati_gui_test_destination_direct_restore")
    RESTORE_FOLDER = os.path.abspath("duplicati_gui_test_restore")
    DIRECT_RESTORE_FOLDER = os.path.abspath("duplicati_gui_test_direct_restore")

    driver.maximize_window()
    driver.get(LOGIN_URL)
    wait_for_load(By.ID, "login-password").send_keys(WEBSERVICE_PASSWORD)
    wait_for_load(By.ID, "login-button").click()

    print("Initial page loading ...")
    wait_for_redirect(HOME_URL)
    
    print("Preloading pages ...")
    for url in PRELOAD_URLS:
        driver.get(url)
        time.sleep(1)

    driver.get(HOME_URL)
    time.sleep(1)

    # Load attempts
    attempts = 3

    # When running in headless mode the requests are too fast
    # and index.html loads multiple .js files which exhaust the
    # Chrome pending request queue (but only in headless mode)
    # So we re-issue the "get" to depend on cached results
    # meaning less requests and less chance of exhausting the queue
    # Upgrading to a newer Angular version will fix this issue
    #
    # After the initial load is complete, caching will ensure
    # that only a few files are loaded
    while attempts > 0:
        try:
            attempts -= 1
            wait_for_title("Duplicati")
            wait_for_clickable(By.LINK_TEXT, "Add backup")
            if driver.find_element(By.ID, "connection-lost-dialog").is_displayed():
                raise Exception("connection-lost-dialog is displayed")
            
            print("Loaded page, assuming all resources are now ready")
            break
        except:
            print("Loading failed, retrying")
            driver.get(HOME_URL)
            time.sleep(1)

    # Wait for all resources to load
    time.sleep(2)

    print("Browser log lines before test: ")
    for entry in driver.get_log('browser'):
        print(entry)

    # Create and hash random files in the source folder
    write_random_file(1024 * 1024, SOURCE_FOLDER + os.sep + "1MB.test")
    write_random_file(100 * 1024, SOURCE_FOLDER + os.sep + "subfolder" + os.sep + "100KB.test")
    sha1_source = sha1_folder(SOURCE_FOLDER)

    print("Adding new backup")
    # Add new backup
    wait_for_clickable(By.LINK_TEXT, "Add backup").click()

    # Choose the "add new" option
    wait_for_clickable(By.ID, "blank").click()
    wait_for_load(By.XPATH, "//input[@class='submit next']").click()

    # Add new backup - General page
    wait_for_load(By.ID, "name").send_keys(BACKUP_NAME)
    wait_for_load(By.ID, "passphrase").send_keys(PASSWORD)
    wait_for_load(By.ID, "repeat-passphrase").send_keys(PASSWORD)
    wait_for_load(By.ID, "nextStep1").click()

    # Add new backup - Destination page
    wait_for_load(By.LINK_TEXT, "Manually type path").click()
    wait_for_load(By.ID, "file_path").send_keys(DESTINATION_FOLDER)
    wait_for_load(By.ID, "nextStep2").click()

    # Add new backup - Source Data page
    wait_for_load(By.ID, "sourcePath").send_keys(os.path.abspath(SOURCE_FOLDER) + os.sep)
    wait_for_load(By.ID, "sourceFolderPathAdd").click()
    wait_for_load(By.ID, "nextStep3").click()

    # Add new backup - Schedule page
    useScheduleRun = wait_for_load(By.ID, "useScheduleRun")
    if useScheduleRun.is_selected():
        useScheduleRun.click()
    wait_for_load(By.ID, "nextStep4").click()

    # Add new backup - Options page
    wait_for_clickable(By.ID, "save").click()
    time.sleep(1) # Delay so page has time to load

    # Run the backup job and wait for finish
    print("Running backup job")
    wait_for_clickable(By.LINK_TEXT, BACKUP_NAME).click()
    [n for n in driver.find_elements("xpath", "//dl[@class='taskmenu']/dd/p/span[contains(text(),'Run now')]") if n.is_displayed()][0].click()
    wait_for_text("//div[@class='task ng-scope']/dl[2]/dd[1]", "(took ", 60)

    # Restore
    print("Restoring")
    if len([n for n in driver.find_elements("xpath", u"//span[contains(text(),'Restore files \u2026')]") if n.is_displayed()]) == 0:
        wait_for_clickable(By.LINK_TEXT, BACKUP_NAME).click()

    [n for n in driver.find_elements("xpath", u"//span[contains(text(),'Restore files \u2026')]") if n.is_displayed()][0].click()
    wait_for_load(By.XPATH, "//span[contains(text(),'" + SOURCE_FOLDER + "')]")  # wait for filelist
    time.sleep(1) # Delay so page has time to load
    wait_for_clickable(By.XPATH, "//restore-file-picker/ul/li/div/a[2]").click()  # select root folder checkbox

    wait_for_clickable(By.XPATH, "//form[@id='restore']/div[1]/div[@class='buttons']/a/span[contains(text(), 'Continue')]").click()
    wait_for_clickable(By.ID, "restoretonewpath").click()
    wait_for_load(By.ID, "restore_path").send_keys(RESTORE_FOLDER)
    wait_for_clickable(By.XPATH, "//form[@id='restore']/div/div[@class='buttons']/a/span[contains(text(),'Restore')]").click()

    # wait for restore to finish
    print("Waiting for restore to finish")
    wait_for_text("//form[@id='restore']/div[3]/h3/div[1]", "Your files and folders have been restored successfully.", 60)

    # hash restored files
    print("Restore completed, verifying hashes")
    sha1_restore = sha1_folder(RESTORE_FOLDER)

    # cleanup: delete source and restore folder and rename destination folder for direct restore
    if os.path.exists(SOURCE_FOLDER):
        shutil.rmtree(SOURCE_FOLDER)
    if os.path.exists(RESTORE_FOLDER):
        shutil.rmtree(RESTORE_FOLDER)
    os.rename(DESTINATION_FOLDER, DESTINATION_FOLDER_DIRECT_RESTORE)

    # direct restore
    print("Starting direct restore")
    wait_for_clickable(By.LINK_TEXT, "Restore").click()

    # Choose the "restore direct" option
    wait_for_clickable(By.ID, "direct").click()
    wait_for_clickable(By.XPATH, "//input[@class='submit next']").click()

    wait_for_clickable(By.LINK_TEXT, "Manually type path").click()
    wait_for_load(By.ID, "file_path").send_keys(DESTINATION_FOLDER_DIRECT_RESTORE)
    wait_for_clickable(By.ID, "nextStep1").click()

    print("Connecting to destination")
    wait_for_load(By.ID, "password").send_keys(PASSWORD)
    wait_for_clickable(By.ID, "connect").click()

    print("Waiting for filelist")
    wait_for_load(By.XPATH, "//span[contains(text(),'" + SOURCE_FOLDER + "')]")  # wait for filelist

    time.sleep(1) # Delay so page has time to load
    wait_for_clickable(By.XPATH, "//restore-file-picker/ul/li/div/a[2]").click()  # select root folder checkbox
    wait_for_load(By.XPATH, "//form[@id='restore']/div[1]/div[@class='buttons']/a/span[contains(text(), 'Continue')]").click()

    print("Restoring files with direct restore")
    wait_for_clickable(By.ID, "restoretonewpath").click()
    wait_for_load(By.ID, "restore_path").send_keys(DIRECT_RESTORE_FOLDER)
    wait_for_clickable(By.XPATH, "//form[@id='restore']/div/div[@class='buttons']/a/span[contains(text(),'Restore')]").click()

    # wait for restore to finish
    print("Waiting for direct restore to finish")
    wait_for_text("//form[@id='restore']/div[3]/h3/div[1]", "Your files and folders have been restored successfully.", 60)

    # hash direct restore files
    print("Direct restore completed, verifying hashes")
    sha1_direct_restore = sha1_folder(DIRECT_RESTORE_FOLDER)

    print("Source hashes: " + str(sha1_source))
    print("Restore hashes: " + str(sha1_restore))
    print("Direct Restore hashes: " + str(sha1_direct_restore))

    # Tell Sauce Labs to stop the test
    driver.quit()

    if not (sha1_source == sha1_restore and sha1_source == sha1_direct_restore):
        sys.exit(1)  # return with error

try:
    runTests()
except:
    print("Test failed, emitting browser log lines: ")
    for entry in driver.get_log('browser'):
        print(entry)
    raise
