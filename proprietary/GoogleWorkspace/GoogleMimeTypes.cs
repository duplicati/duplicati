// Copyright (c) 2026 Duplicati Inc. All rights reserved.

namespace Duplicati.Proprietary.GoogleWorkspace;

/// <summary>
/// Provides MIME type constants and helper methods for Google Workspace file types.
/// </summary>
public static class GoogleMimeTypes
{
    /// <summary>
    /// MIME type prefix for Google Apps.
    /// </summary>
    public const string GoogleAppsPrefix = "application/vnd.google-apps.";

    /// <summary>
    /// Google Drive folder MIME type.
    /// </summary>
    public const string Folder = "application/vnd.google-apps.folder";

    /// <summary>
    /// Google Drive shortcut MIME type.
    /// </summary>
    public const string Shortcut = "application/vnd.google-apps.shortcut";

    /// <summary>
    /// Google Docs document MIME type.
    /// </summary>
    public const string Document = "application/vnd.google-apps.document";

    /// <summary>
    /// Google Sheets spreadsheet MIME type.
    /// </summary>
    public const string Spreadsheet = "application/vnd.google-apps.spreadsheet";

    /// <summary>
    /// Google Slides presentation MIME type.
    /// </summary>
    public const string Presentation = "application/vnd.google-apps.presentation";

    /// <summary>
    /// Google Apps Script MIME type.
    /// </summary>
    public const string Script = "application/vnd.google-apps.script";

    /// <summary>
    /// Google Sites MIME type.
    /// </summary>
    public const string Site = "application/vnd.google-apps.site";

    /// <summary>
    /// Microsoft Word document export MIME type.
    /// </summary>
    public const string WordDocument = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    /// <summary>
    /// Microsoft Excel spreadsheet export MIME type.
    /// </summary>
    public const string ExcelSpreadsheet = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>
    /// Microsoft PowerPoint presentation export MIME type.
    /// </summary>
    public const string PowerPointPresentation = "application/vnd.openxmlformats-officedocument.presentationml.presentation";

    /// <summary>
    /// Google Apps Script JSON export MIME type.
    /// </summary>
    public const string ScriptJson = "application/vnd.google-apps.script+json";

    /// <summary>
    /// PDF export MIME type.
    /// </summary>
    public const string Pdf = "application/pdf";

    /// <summary>
    /// Determines whether the specified MIME type represents a Google Workspace document
    /// that can be exported (not a folder or shortcut).
    /// </summary>
    /// <param name="mimeType">The MIME type to check.</param>
    /// <returns>true if the MIME type represents a Google Workspace document; otherwise, false.</returns>
    public static bool IsGoogleDoc(string mimeType)
    {
        return !string.IsNullOrWhiteSpace(mimeType) &&
            mimeType.StartsWith(GoogleAppsPrefix, StringComparison.Ordinal) &&
            mimeType != Folder &&
            mimeType != Shortcut;
    }

    /// <summary>
    /// Gets the appropriate export MIME type for a Google Workspace document.
    /// </summary>
    /// <param name="mimeType">The Google Workspace MIME type.</param>
    /// <returns>The export MIME type for the document.</returns>
    public static string GetExportMimeType(string mimeType)
    {
        return mimeType switch
        {
            Document => WordDocument,
            Spreadsheet => ExcelSpreadsheet,
            Presentation => PowerPointPresentation,
            Script => ScriptJson,
            _ => Pdf
        };
    }
}
