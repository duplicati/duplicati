using Duplicati.Browser.Test.Drivers;
using Duplicati.Browser.Test.PageObjects;
using TechTalk.SpecFlow;

namespace Duplicati.Browser.Test.Hooks
{
    /// <summary>
    /// Calculator related hooks
    /// </summary>
    [Binding]
    public class DuplicatiHooks
    {
        ///<summary>
        ///  Reset the calculator before each scenario tagged with "Calculator"
        /// </summary>
        [BeforeScenario("Duplicati")]
        public static void BeforeScenario(BrowserDriver browserDriver)
        {
            var calculatorPageObject = new DuplicatiPageObject(browserDriver.Current);
            calculatorPageObject.Open();
        }
    }
}