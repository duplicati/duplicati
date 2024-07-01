using System.IO;
using Duplicati.Browser.Test.Drivers;
using Duplicati.Browser.Test.PageObjects;
using FluentAssertions;
using OpenQA.Selenium.Support.Extensions;
using TechTalk.SpecFlow;

namespace Duplicati.Browser.Test.Steps
{
    [Binding]
    public sealed class DuplicatiStepDefinitions(BrowserDriver browserDriver)
    {
        //Page Object for Calculator
        private readonly DuplicatiPageObject _duplicatiPageObject = new(browserDriver.Current);

        [Given("there is local backup defined")]
        public void GivenLocalBackupDefined()
        {
            browserDriver.Current.TakeScreenshot();
            //delegate to Page Object
           _duplicatiPageObject.NavigateToBackupCreation()
                .ToGeneralSettings()
                .SetName("Local Backup")
                .GenerateRandomPassword()
                .ToBackupTarget()
                .ChoosePathManually()
                .SetManualPath(Path.Combine(Directory.GetCurrentDirectory(), "test-backup-target"))
                .ToBackupSource()
                .AddSourceManually(Path.Combine(Directory.GetCurrentDirectory(), "test-backup-source")+"/")
                .ToSchedule()
                .DisableAutorun()
                .ToOptions()
                .Save()
                ;
        }

        [When("you run the backup")]
        public void WhenBackupIsRun()
        {
            //delegate to Page Object
            _duplicatiPageObject.StartBackupTask();
        }

        [Then("the result should be (.*)")]
        public void ThenTheResultShouldBe(int expectedResult)
        {
            //delegate to Page Object
            //var actualResult = _duplicatiPageObject.WaitForNonEmptyResult();

            //actualResult.Should().Be(expectedResult.ToString());
        }
    }
}
