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
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Duplicati.GUI.TrayIcon;

internal static class PasswordPrompt
{
    public static bool IsShowingDialog { get; private set; } = false;
    /// <summary>
    /// Shows a password prompt dialog within an already-running Avalonia application.
    /// Must be called from the UI thread or will dispatch to it.
    /// </summary>
    /// <param name="isChangePassword">Whether this is a change password prompt</param>
    /// <returns>A task that completes with the password, or null if cancelled</returns>
    public static Task<string?> ShowPasswordDialogAsync(bool isChangePassword)
    {
        if (IsShowingDialog)
            throw new InvalidOperationException("A password prompt dialog is already showing.");

        IsShowingDialog = true;
        var tcs = new TaskCompletionSource<string?>();

        if (Dispatcher.UIThread.CheckAccess())
        {
            ShowDialogInternal(tcs, isChangePassword);
        }
        else
        {
            Dispatcher.UIThread.Post(() => ShowDialogInternal(tcs, isChangePassword));
        }

        return tcs.Task.ContinueWith(t =>
        {
            IsShowingDialog = false;
            return t.Result;
        });
    }

    private static void ShowDialogInternal(TaskCompletionSource<string?> tcs, bool isChangePassword)
    {
        try
        {
            var window = new PasswordPromptWindow(isChangePassword);
            window.PasswordSubmitted += (_, pwd) =>
            {
                tcs.TrySetResult(pwd);
                window.Close();
            };

            window.Cancelled += (_, _) =>
            {
                tcs.TrySetResult(null);
                window.Close();
            };

            window.Closed += (_, _) =>
            {
                // Ensure the task completes even if window is closed another way
                tcs.TrySetResult(null);
            };

            window.Show();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to show password prompt window: {ex.Message}");
            tcs.TrySetResult(null);
        }
    }
}

internal class PasswordPromptWindow : Window
{
    private readonly TextBox passwordBox;
    private readonly TextBox hostUrlBox;

    public event EventHandler<string?>? PasswordSubmitted;

    public event EventHandler? Cancelled;

    public PasswordPromptWindow(bool isChangePassword)
    {
        Title = "Duplicati TrayIcon Connect";
        Width = 420;
        Height = 210;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var description = new TextBlock
        {
            Text = isChangePassword
                ? "Enter the new Duplicati server password:"
                : "Enter the Duplicati server password to connect:",
            Margin = new Thickness(0, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap
        };

        passwordBox = new TextBox
        {
            Watermark = "Server password",
            PasswordChar = '•',
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 10)
        };

        var hostUrlLabel = new TextBlock
        {
            Text = "Duplicati server URL:",
            Margin = new Thickness(0, 5, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };

        hostUrlBox = new TextBox
        {
            Text = Program.Connection?.ServerUri?.ToString() ?? string.Empty,
            Watermark = "http://localhost:8200",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 10)
        };

        var okButton = new Button
        {
            Content = "Connect",
            IsDefault = true,
            Width = 100
        };
        okButton.Click += (_, _) => SubmitPassword();

        var cancelButton = new Button
        {
            Content = "Cancel",
            IsCancel = true,
            Width = 100
        };
        cancelButton.Click += (_, _) => Cancel();

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10
        };
        buttonPanel.Children.Add(cancelButton);
        buttonPanel.Children.Add(okButton);

        var contentPanel = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto")
        };

        var inputPanel = new StackPanel
        {
            Children =
            {
                description,
                passwordBox,
                hostUrlLabel,
                hostUrlBox
            }
        };

        Grid.SetRow(inputPanel, 0);
        Grid.SetRow(buttonPanel, 1);

        contentPanel.Children.Add(inputPanel);
        contentPanel.Children.Add(buttonPanel);

        Content = new Border
        {
            Padding = new Thickness(20),
            Child = contentPanel
        };

        Opened += (_, _) => passwordBox.Focus();
    }

    private void SubmitPassword()
    {
        var password = passwordBox.Text;
        var hostUrlText = hostUrlBox.Text?.Trim();

        if (!string.IsNullOrWhiteSpace(hostUrlText) && Program.Connection != null)
        {
            try
            {
                var uri = new Uri(hostUrlText, UriKind.Absolute);

                if (!uri.Equals(Program.Connection.ServerUri))
                {
                    Program.Connection.UpdateServerUri(uri);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Invalid host URL '{hostUrlText}': {ex.Message}");
            }
        }

        PasswordSubmitted?.Invoke(this, password);
    }

    private void Cancel()
    {
        Cancelled?.Invoke(this, EventArgs.Empty);
    }
}
