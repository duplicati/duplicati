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

        //The Selenium web driver to automate the browser

        //The default wait time in seconds for wait.Until
        public const int DefaultWaitInSeconds = 5;

        //Finding elements by ID
        private IWebElement AddBackupElement => webDriver.WaitForElement(By.ClassName("add"));
        private IWebElement AddButtonElement => webDriver.WaitForElement(By.Id("add-button"));
        private IWebElement ResultElement => webDriver.WaitForElement(By.Id("result"));
        private IWebElement ResetButtonElement => webDriver.WaitForElement(By.Id("reset-button"));

        public AddBackupPage NavigateToBackupCreation()
        {
            AddBackupElement.Click();
            return new AddBackupPage(webDriver);
        }

        public void ClickAdd()
        {
            //Click the add button
            AddButtonElement.Click();
        }

        public void EnsureCalculatorIsOpenAndReset()
        {
            //Open the calculator page in the browser if not opened yet
            if (webDriver.Url != DuplicatiUrl)
            {
                webDriver.Url = DuplicatiUrl;
            }
            //Otherwise reset the calculator by clicking the reset button
            else
            {
                //Click the rest button
                ResetButtonElement.Click();

                //Wait until the result is empty again
                WaitForEmptyResult();
            }
        }

        public string WaitForNonEmptyResult()
        {
            //Wait for the result to be not empty
            return WaitUntil(
                () => ResultElement.GetAttribute("value"),
                result => !string.IsNullOrEmpty(result));
        }

        public string WaitForEmptyResult()
        {
            //Wait for the result to be empty
            return WaitUntil(
                () => ResultElement.GetAttribute("value"),
                result => result == string.Empty);
        }

        /// <summary>
        /// Helper method to wait until the expected result is available on the UI
        /// </summary>
        /// <typeparam name="T">The type of result to retrieve</typeparam>
        /// <param name="getResult">The function to poll the result from the UI</param>
        /// <param name="isResultAccepted">The function to decide if the polled result is accepted</param>
        /// <returns>An accepted result returned from the UI. If the UI does not return an accepted result within the timeout an exception is thrown.</returns>
        private T WaitUntil<T>(Func<T> getResult, Func<T, bool> isResultAccepted) where T: class
        {
            var wait = new WebDriverWait(webDriver, TimeSpan.FromSeconds(DefaultWaitInSeconds));
            return wait.Until(driver =>
            {
                var result = getResult();
                if (!isResultAccepted(result))
                    return default;

                return result;
            });

        }
    }
}
