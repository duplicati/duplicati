using System;
using Duplicati.Browser.Test.Drivers;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace Duplicati.Browser.Test.PageObjects
{
    /// <summary>
    /// Calculator Page Object
    /// </summary>
    public class DuplicatiPageObject(IWebDriver webDriver)
    {
        //The URL of the calculator to be opened in the browser
        private const string DuplicatiUrl = "http://localhost:8201";

        //Finding elements by ID
        private IWebElement AddBackupElement => webDriver.WaitForElement(By.ClassName("add"));
        private IWebElement HomeButtonElement => webDriver.WaitForElement(By.ClassName("home"));
        private IWebElement BackupSection => webDriver.WaitForElement(By.ClassName("task"));

        public AddBackupPage NavigateToBackupCreation()
        {
            AddBackupElement.Click();
            return new AddBackupPage(webDriver);
        }

        public void StartBackupTask()
        {
            //Navigate to home just in case
            HomeButtonElement.Click();
            BackupSection.WaitForElement(By.ClassName("ng-binding")).Click();
            BackupSection.WaitForElement(By.XPath("/dl[1]/dd[1]/p[1]"));
        }

        public void Open()
        {
            //Open the calculator page in the browser if not opened yet
            if (webDriver.Url != DuplicatiUrl)
            {
                webDriver.Url = DuplicatiUrl;
            }
        }
    }
}
