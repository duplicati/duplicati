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
namespace Duplicati.WebserverCore.Abstractions;

/// <summary>
/// The registration of a remote control for this machine
/// </summary>
public interface IRemoteControllerRegistration
{
    /// <summary>
    /// Begin the registration of a machine
    /// </summary>
    /// <param name="registrationUrl">The URL to register the machine with</param>
    /// <returns>The task to wait on</returns>
    Task RegisterMachine(string registrationUrl);

    /// <summary>
    /// Waits for the registration to complete.
    /// </summary>
    /// <returns>The task to wait on</returns>
    public Task WaitForRegistration();

    /// <summary>
    /// Cancels the registration of the machine
    /// </summary>
    void CancelRegisterMachine();

    /// <summary>
    /// A flag indicating if the machine is currently registering
    /// </summary>
    bool IsRegistering { get; }
    /// <summary>
    /// The URL to register the machine with, if registring
    /// </summary>
    string? RegistrationUrl { get; }
}
