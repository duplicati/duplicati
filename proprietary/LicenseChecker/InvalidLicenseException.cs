// Copyright (c) 2026 Duplicati Inc. All rights reserved.

namespace Duplicati.Proprietary.LicenseChecker;

/// <summary>
/// Exception thrown when a license is invalid
/// </summary>
/// <param name="message">The exception message</param>
public class InvalidLicenseException(string message) : Exception(message);