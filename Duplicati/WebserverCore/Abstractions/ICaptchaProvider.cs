namespace Duplicati.WebserverCore.Abstractions;

/// <summary>
/// A captcha provider
/// </summary>
public interface ICaptchaProvider
{
    /// <summary>
    /// Check if the captcha is solved
    /// </summary>
    /// <param name="token">The captcha token</param>
    /// <param name="target">The captcha target</param>
    /// <param name="answer">The captcha answer</param>
    bool SolvedCaptcha(string token, string target, string answer);
    /// <summary>
    /// Create a captcha
    /// </summary>
    /// <param name="target">The captcha target</param>
    /// <returns>The captcha token and the answer</returns>
    (string Token, string? Answer) CreateCaptcha(string target);
    /// <summary>
    /// Get the captcha image
    /// </summary>
    /// <param name="token">The captcha token</param>
    byte[] GetCaptchaImage(string token);
    /// <summary>
    /// Gets a value indicating whether the visual captcha is disabled
    /// </summary>
    bool VisualCaptchaDisabled { get; }
}
