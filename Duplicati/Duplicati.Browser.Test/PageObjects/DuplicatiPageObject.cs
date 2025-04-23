// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.
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
