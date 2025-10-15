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
using System.Runtime.Versioning;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Snapshots.Windows;

/// <summary>
/// Implementation of powermode handler for Windows
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsPowerModeProvider : IPowerModeProvider
{
    /// <inheritdoc />
    public Action OnResume { get; set; }
    /// <inheritdoc />
    public Action OnSuspend { get; set; }

    /// <summary>
    /// Constructs a new power mode provider
    /// </summary>
    public WindowsPowerModeProvider()
    {
        Microsoft.Win32.SystemEvents.PowerModeChanged += new Microsoft.Win32.PowerModeChangedEventHandler(SystemEvents_PowerModeChanged);
    }

    /// <summary>
    /// Handles the power mode events
    /// </summary>
    /// <param name="sender">The event sender</param>
    /// <param name="e">The event args</param>
    private void SystemEvents_PowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case Microsoft.Win32.PowerModes.Suspend:
                OnSuspend?.Invoke();
                break;
            case Microsoft.Win32.PowerModes.Resume:
                OnResume?.Invoke();
                break;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Microsoft.Win32.SystemEvents.PowerModeChanged -= new Microsoft.Win32.PowerModeChangedEventHandler(SystemEvents_PowerModeChanged);
    }

}
