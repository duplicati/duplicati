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
using System.Threading;
using Duplicati.Browser.Test.Drivers;
using OpenQA.Selenium;

namespace Duplicati.Browser.Test.PageObjects;

public class AddBackupPage(IWebDriver webDriver)
{
    private IWebElement NextButtonElement => webDriver.WaitForElement(By.ClassName("submit"));

    public GeneralBackupSettingsPage ToGeneralSettings()
    {
        NextButtonElement.Click();

        return new GeneralBackupSettingsPage(webDriver);
    }
}

public class GeneralBackupSettingsPage(IWebDriver webDriver)
{
    private IWebElement NameTextboxElement => webDriver.WaitForElement(By.Id("name"));
    private IWebElement NextButtonElement => webDriver.WaitForElement(By.ClassName("submit"));
    private IWebElement GeneratePasswordLink => webDriver.WaitForElement(By.PartialLinkText("Generuj"));

    public GeneralBackupSettingsPage SetName(string name)
    {
        if (NameTextboxElement.WaitForElementBeing(e => e.GetAttribute("placeholder") is "Moje Zdjęcia" or "My Photos"))
        {
            NameTextboxElement.Clear();
            NameTextboxElement.SendKeysAndCheck(name);
            Thread.Sleep(1000);
            NameTextboxElement.SendKeysAndCheck(name);
        }

        return this;
    }
    
    public GeneralBackupSettingsPage GenerateRandomPassword()
    {
        GeneratePasswordLink.Click();

        return this;
    }

    public BackupTargetSettingsPage ToBackupTarget()
    {
        NextButtonElement.Click();

        return new BackupTargetSettingsPage(webDriver);
    }
}

public class BackupTargetSettingsPage(IWebDriver webDriver)
{
    private IWebElement NextButtonElement => webDriver.WaitForElement(By.CssSelector("#nextStep2.submit"));
    private IWebElement ManualPathLink => webDriver.WaitForElement(By.LinkText("Podaj ścieżkę ręcznie"));
    private IWebElement FilePathTextbox => webDriver.WaitForElement(By.Id("file_path"));

    public BackupTargetSettingsPage ChoosePathManually()
    {
        ManualPathLink.Click();

        return this;
    }
    
    public BackupTargetSettingsPage SetManualPath(string path)
    {
        if (FilePathTextbox.WaitForElementBeing(e => e.GetAttribute("placeholder") is "Wprowadź ścieżkę docelową"))
        {
            FilePathTextbox.Clear();
            FilePathTextbox.SendKeysAndCheck(path);
        }

        return this;
    }

    public BackupSourceSettingsPage ToBackupSource()
    {
        NextButtonElement.Click();

        return new BackupSourceSettingsPage(webDriver);
    }
}

public class BackupSourceSettingsPage(IWebDriver webDriver)
{
    private IWebElement NextButtonElement => webDriver.WaitForElement(By.CssSelector("#nextStep3.submit"));
    private IWebElement FilePathTextbox => webDriver.WaitForElement(By.Id("sourcePath"));
    private IWebElement AddFilePathButton => webDriver.WaitForElement(By.Id("sourceFolderPathAdd"));
    private IWebElement ConfirmAddPathButton => webDriver.WaitForElement(By.PartialLinkText("Tak"));

    public BackupSourceSettingsPage AddSourceManually(string path)
    {
        if (FilePathTextbox.WaitForElementBeing(e => e.GetAttribute("placeholder") is "Dodaj ścieżkę bezpośrednio"))
        {
            FilePathTextbox.Clear();
            FilePathTextbox.SendKeysAndCheck(path);
        }
        AddFilePathButton.Click();
        ConfirmAddPathButton.Click();

        return this;
    }

    public ScheduleSettingsPage ToSchedule()
    {
        Thread.Sleep(20000);
        NextButtonElement.Click();

        return new ScheduleSettingsPage(webDriver);
    }
}

public class ScheduleSettingsPage(IWebDriver webDriver)
{
    private IWebElement NextButtonElement => webDriver.WaitForElement(By.CssSelector("#nextStep4.submit"));
    private IWebElement AutoRunCheckbox => webDriver.WaitForElement(By.Id("useScheduleRun"));

    public ScheduleSettingsPage DisableAutorun()
    {
        if (AutoRunCheckbox.Selected)
        {
            AutoRunCheckbox.Click();
        }

        return this;
    }

    public ToOptionsSettingsPage ToOptions()
    {
        NextButtonElement.Click();

        return new ToOptionsSettingsPage(webDriver);
    }
}

public class ToOptionsSettingsPage(IWebDriver webDriver)
{
    private IWebElement SaveButtonElement => webDriver.WaitForElement(By.CssSelector("#save"));
    private IWebElement ConfirmationModal => webDriver.WaitForElement(By.CssSelector(".content.buttons"));

    public ToOptionsSettingsPage Save()
    {
        SaveButtonElement.Click();
        ConfirmationModal.WaitForElement(By.CssSelector("ul>li:nth-child(2)>a")).Click();

        return this;
    }
}