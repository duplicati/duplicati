// Copyright (C) 2026, The Duplicati Team
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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Themes.Fluent;
using ScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility;

namespace Duplicati.GUI.TrayIcon
{
    /// <summary>
    /// Helper class for showing error dialogs
    /// </summary>
    public static class ErrorDialog
    {
        /// <summary>
        /// Shows an error dialog for the crash, being very defensive to avoid additional crashes.
        /// This should only be called when the hosted server instance is crashing.
        /// </summary>
        /// <param name="ex">The exception to show</param>
        public static void ShowErrorDialog(Exception ex)
        {

            try
            {
                // Build a simple Avalonia application to show the dialog
                var builder = AppBuilder
                    .Configure<Application>()
                    .UsePlatformDetect();

                var app = builder.SetupWithoutStarting().Instance;
                if (app == null)
                    return;

                // Apply a simple theme so controls look native
                app.Styles.Add(new FluentTheme());

                // Create the message box window
                var window = new Window
                {
                    Title = "Duplicati TrayIcon Error",
                    Width = 600,
                    Height = 400,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    CanResize = true,
                    ShowInTaskbar = false,
                    Topmost = true
                };

                // Create the content
                var stackPanel = new StackPanel
                {
                    Margin = new Thickness(20),
                    Spacing = 10
                };

                var titleBlock = new TextBlock
                {
                    Text = "A fatal error occurred",
                    FontSize = 16,
                    FontWeight = FontWeight.Bold,
                    TextWrapping = TextWrapping.Wrap
                };

                var messageBlock = new TextBlock
                {
                    Text = ex.Message,
                    TextWrapping = TextWrapping.Wrap
                };

                // Details label
                var detailsLabel = new TextBlock
                {
                    Text = "Details:",
                    FontWeight = FontWeight.Bold,
                    Margin = new Thickness(0, 10, 0, 0)
                };

                // Scrollable text area with full exception details
                var exceptionText = new TextBox
                {
                    Text = ex.ToString(),
                    IsReadOnly = true,
                    TextWrapping = TextWrapping.NoWrap,
                    AcceptsReturn = true,
                    FontFamily = new FontFamily("Consolas, Monaco, Courier New, monospace"),
                    FontSize = 10
                };

                // Explicitly wrap in a ScrollViewer for proper scrolling
                var scrollViewer = new ScrollViewer
                {
                    Content = exceptionText,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Height = 200
                };

                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 20, 0, 0)
                };

                var okButton = new Button
                {
                    Content = "OK",
                    Width = 100,
                    HorizontalContentAlignment = HorizontalAlignment.Center
                };

                okButton.Click += (_, _) =>
                {
                    try
                    {
                        window.Close();
                    }
                    catch
                    {
                        // Ignore errors closing window
                    }
                };
                buttonPanel.Children.Add(okButton);

                stackPanel.Children.Add(titleBlock);
                stackPanel.Children.Add(messageBlock);
                stackPanel.Children.Add(detailsLabel);
                stackPanel.Children.Add(scrollViewer);
                stackPanel.Children.Add(buttonPanel);

                window.Content = stackPanel;

                app.Run(window);
            }
            catch
            {
            }
        }
    }
}
