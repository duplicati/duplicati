using Duplicati.Browser.Test.Drivers;
using Duplicati.Browser.Test.PageObjects;
using FluentAssertions;
using TechTalk.SpecFlow;

namespace Duplicati.Browser.Test.Steps
{
    [Binding]
    public sealed class DuplicatiStepDefinitions(BrowserDriver browserDriver)
    {
        //Page Object for Calculator
        private readonly DuplicatiPageObject _duplicatiPageObject = new(browserDriver.Current);

        [Given("the first number is (.*)")]
        public void GivenTheFirstNumberIs(int number)
        {
            //delegate to Page Object
            _duplicatiPageObject.NavigateToBackupCreation();
        }

        [Given("the second number is (.*)")]
        public void GivenTheSecondNumberIs(int number)
        {
            //delegate to Page Object
            _duplicatiPageObject.EnterSecondNumber(number.ToString());
        }

        [When("the two numbers are added")]
        public void WhenTheTwoNumbersAreAdded()
        {
            //delegate to Page Object
            _duplicatiPageObject.ClickAdd();
        }

        [Then("the result should be (.*)")]
        public void ThenTheResultShouldBe(int expectedResult)
        {
            //delegate to Page Object
            var actualResult = _duplicatiPageObject.WaitForNonEmptyResult();

            actualResult.Should().Be(expectedResult.ToString());
        }
    }
}
