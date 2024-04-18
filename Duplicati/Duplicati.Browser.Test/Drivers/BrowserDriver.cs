using System;
using OpenQA.Selenium;
using OpenQA.Selenium.Edge;

namespace Duplicati.Browser.Test.Drivers
{
    /// <summary>
    /// Manages a browser instance using Selenium
    /// </summary>
    public class BrowserDriver : IDisposable
    {
        private readonly Lazy<IWebDriver> _currentWebDriverLazy = new(CreateWebDriver);
        private bool _isDisposed;

        /// <summary>
        /// The Selenium IWebDriver instance
        /// </summary>
        public IWebDriver Current => _currentWebDriverLazy.Value;

        /// <summary>
        /// Creates the Selenium web driver (opens a browser)
        /// </summary>
        /// <returns></returns>
        private static EdgeDriver CreateWebDriver()
        {
            //We use the Chrome browser
            var chromeDriverService = EdgeDriverService.CreateDefaultService(".", "msedgedriver");

            return new EdgeDriver(chromeDriverService, new EdgeOptions());
        }

        /// <summary>
        /// Disposes the Selenium web driver (closing the browser)
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            if (_currentWebDriverLazy.IsValueCreated)
            {
                Current.Quit();
            }

            _isDisposed = true;
        }
    }
}