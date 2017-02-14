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

if "TRAVIS_BUILD_NUMBER" in os.environ:
    if "SAUCE_USERNAME" not in os.environ:
        print "No sauce labs login credentials found. Stopping tests..."
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
else:
    # local
    print "Using LOCAL webdriver"
    profile = webdriver.FirefoxProfile()
    profile.set_preference("intl.accept_languages", "en")
    driver = webdriver.Firefox(profile)
    driver.maximize_window()


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


def wait_for_text(time, xpath, text):
    WebDriverWait(driver, time).until(expected_conditions.text_to_be_present_in_element((By.XPATH, xpath), text))


BACKUP_NAME = "BackupName"
PASSWORD = "the_backup_password_is_really_long_and_safe"
SOURCE_FOLDER = os.path.abspath("duplicati_gui_test_source")
DESTINATION_FOLDER = os.path.abspath("duplicati_gui_test_destination")
DESTINATION_FOLDER_DIRECT_RESTORE = os.path.abspath("duplicati_gui_test_destination_direct_restore")
RESTORE_FOLDER = os.path.abspath("duplicati_gui_test_restore")
DIRECT_RESTORE_FOLDER = os.path.abspath("duplicati_gui_test_direct_restore")

# wait 5 seconds for duplicati server to start
time.sleep(5)

driver.implicitly_wait(10)
driver.get("http://localhost:8200/ngax/index.html")

if "Duplicati" not in driver.title:
    raise Exception("Unable to load duplicati GUI!")

# Create and hash random files in the source folder
write_random_file(1024 * 1024, SOURCE_FOLDER + os.sep + "1MB.test")
write_random_file(100 * 1024, SOURCE_FOLDER + os.sep + "subfolder" + os.sep + "100KB.test")
sha1_source = sha1_folder(SOURCE_FOLDER)

# Add new backup
driver.find_element_by_link_text("Add backup").click()

# Choose the "add new" option
driver.find_element_by_id("blank").click()
driver.find_element_by_xpath("//input[@class='submit next']").click()

# Add new backup - General page
time.sleep(1)
driver.find_element_by_id("name").send_keys(BACKUP_NAME)
driver.find_element_by_id("passphrase").send_keys(PASSWORD)
driver.find_element_by_id("repeat-passphrase").send_keys(PASSWORD)
driver.find_element_by_id("nextStep1").click()

# Add new backup - Destination page
driver.find_element_by_link_text("Manually type path").click()
driver.find_element_by_id("file_path").send_keys(DESTINATION_FOLDER)
driver.find_element_by_id("nextStep2").click()

# Add new backup - Source Data page
driver.find_element_by_id("sourcePath").send_keys(os.path.abspath(SOURCE_FOLDER) + os.sep)
driver.find_element_by_id("sourceFolderPathAdd").click()
driver.find_element_by_id("nextStep3").click()

# Add new backup - Schedule page
useScheduleRun = driver.find_element_by_id("useScheduleRun")
if useScheduleRun.is_selected():
    useScheduleRun.click()
driver.find_element_by_id("nextStep4").click()

# Add new backup - Options page
driver.find_element_by_id("save").click()

# Run the backup job and wait for finish
driver.find_element_by_link_text(BACKUP_NAME).click()
[n for n in driver.find_elements_by_xpath("//dl[@class='taskmenu']/dd/p/span[contains(text(),'Run now')]") if n.is_displayed()][0].click()
wait_for_text(60, "//div[@class='task ng-scope']/dl[2]/dd[1]", "(took ")

# Restore
if len([n for n in driver.find_elements_by_xpath("//span[contains(text(),'Restore files ...')]") if n.is_displayed()]) == 0:
    driver.find_element_by_link_text(BACKUP_NAME).click()

[n for n in driver.find_elements_by_xpath("//span[contains(text(),'Restore files ...')]") if n.is_displayed()][0].click()
driver.find_element_by_xpath("//span[contains(text(),'" + SOURCE_FOLDER + "')]")  # wait for filelist
time.sleep(1)
driver.find_element_by_xpath("//restore-file-picker/ul/li/div/a[2]").click()  # select root folder checkbox

driver.find_element_by_xpath("//form[@id='restore']/div[1]/div[@class='buttons']/a/span[contains(text(), 'Continue')]").click()
driver.find_element_by_id("restoretonewpath").click()
driver.find_element_by_id("restore_path").send_keys(RESTORE_FOLDER)
driver.find_element_by_xpath("//form[@id='restore']/div/div[@class='buttons']/a/span[contains(text(),'Restore')]").click()

# wait for restore to finish
wait_for_text(60, "//form[@id='restore']/div[3]/h3/div[1]", "Your files and folders have been restored successfully.")

# hash restored files
sha1_restore = sha1_folder(RESTORE_FOLDER)

# cleanup: delete source and restore folder and rename destination folder for direct restore
shutil.rmtree(SOURCE_FOLDER)
shutil.rmtree(RESTORE_FOLDER)
os.rename(DESTINATION_FOLDER, DESTINATION_FOLDER_DIRECT_RESTORE)

# direct restore
driver.find_element_by_link_text("Restore").click()

# Choose the "restore direct" option
driver.find_element_by_id("direct").click()
driver.find_element_by_xpath("//input[@class='submit next']").click()

time.sleep(1)
driver.find_element_by_link_text("Manually type path").click()
driver.find_element_by_id("file_path").send_keys(DESTINATION_FOLDER_DIRECT_RESTORE)
driver.find_element_by_id("nextStep1").click()

driver.find_element_by_id("password").send_keys(PASSWORD)
driver.find_element_by_id("connect").click()
driver.find_element_by_xpath("//span[contains(text(),'" + SOURCE_FOLDER + "')]")  # wait for filelist
time.sleep(1)
driver.find_element_by_xpath("//restore-file-picker/ul/li/div/a[2]").click()  # select root folder checkbox
time.sleep(1)
driver.find_element_by_xpath("//form[@id='restore']/div[1]/div[@class='buttons']/a/span[contains(text(), 'Continue')]").click()

driver.find_element_by_id("restoretonewpath").click()
driver.find_element_by_id("restore_path").send_keys(DIRECT_RESTORE_FOLDER)
driver.find_element_by_xpath("//form[@id='restore']/div/div[@class='buttons']/a/span[contains(text(),'Restore')]").click()

# wait for restore to finish
wait_for_text(60, "//form[@id='restore']/div[3]/h3/div[1]", "Your files and folders have been restored successfully.")

# hash direct restore files
sha1_direct_restore = sha1_folder(DIRECT_RESTORE_FOLDER)

print "Source hashes: " + str(sha1_source)
print "Restore hashes: " + str(sha1_restore)
print "Direct Restore hashes: " + str(sha1_direct_restore)

# Tell Sauce Labs to stop the test
driver.quit()

if not (sha1_source == sha1_restore and sha1_source == sha1_direct_restore):
    sys.exit(1)  # return with error
