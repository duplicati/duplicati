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
#nullable enable

using System;
using System.Threading;
using Duplicati.Library.Interface;

namespace Duplicati.WebserverCore.Abstractions;

/// <summary>
/// Interface for application-wide settings for the server.
/// </summary>
public interface IApplicationSettings
{
    /// <summary>
    /// Action to start or stop the usage reporter
    /// </summary>
    Action? StartOrStopUsageReporter { get; set; }

    /// <summary>
    /// Gets the folder where Duplicati data is stored
    /// </summary>
    string DataFolder { get; }

    /// <summary>
    /// Used to check the origin of the web server (e.g. Tray icon or a stand alone Server)
    /// </summary>
    string Origin { get; set; }

    /// <summary>
    /// The application exit event
    /// </summary>
    ManualResetEvent ApplicationExitEvent { get; }

    /// <summary>
    /// The shared secret provider from the server invocation
    /// </summary>
    ISecretProvider? SecretProvider { get; set; }

    /// <summary>
    /// Flag to indicate if the settings encryption key was provided externally
    /// </summary>
    bool SettingsEncryptionKeyProvidedExternally { get; set; }
}

