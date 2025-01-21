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
using System.Threading;
using OpenQA.Selenium;
using OpenQA.Selenium.Edge;

namespace Duplicati.Browser.Test.Drivers;

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

public static class DriverExtensions
{
    public static int SingleLoopWait = 100;
    public static TimeSpan DefaultMaxWait = TimeSpan.FromSeconds(30);

    public static IWebElement WaitForElement(this ISearchContext driver, By selector, TimeSpan? maxWait = null)
    {
        maxWait ??= DefaultMaxWait;
        var i = 0;
        while (i < maxWait.Value.TotalMilliseconds)
        {
            try
            {
                return driver.FindElement(selector);
            }
            catch (NoSuchElementException)
            {
                //swallow and wait for another loop
            }

            i += SingleLoopWait;
            Thread.Sleep(TimeSpan.FromMilliseconds(SingleLoopWait));
        }

        return driver.FindElement(selector);
    }

    public static bool WaitForElementBeing(this IWebElement element, Func<IWebElement, bool> predicate,
        TimeSpan? maxWait = null)
    {
        maxWait ??= DefaultMaxWait;
        var i = 0;
        while (i < maxWait.Value.TotalMilliseconds)
        {
            try
            {
                if (predicate.Invoke(element))
                {
                    return true;
                }
            }
            catch (NoSuchElementException)
            {
                //swallow and wait for another loop
            }

            i += SingleLoopWait;
            Thread.Sleep(TimeSpan.FromMilliseconds(SingleLoopWait));
        }

        throw new Exception($"Timout of waiting for element to satisfy predicate: {predicate}!");
    }

    public static void SendKeysAndCheck(this IWebElement element, string text, TimeSpan? maxWait = null)
    {
        maxWait ??= DefaultMaxWait;
        var i = 0;
        while (i < maxWait.Value.TotalMilliseconds)
        {
            try
            {
                if (element.GetAttribute("value") != text)
                {
                    element.SendKeys(text);
                }
                else
                {
                    return;
                }
            }
            catch (NoSuchElementException)
            {
                //swallow and wait for another loop
            }

            i += SingleLoopWait;
            Thread.Sleep(TimeSpan.FromMilliseconds(SingleLoopWait));
        }

        throw new Exception("Could not set text value of element!");
    }
}