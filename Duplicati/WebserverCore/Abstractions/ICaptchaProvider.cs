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
    string CreateCaptcha(string target);
    /// <summary>
    /// Get the captcha image
    /// </summary>
    /// <param name="token">The captcha token</param>
    byte[] GetCaptchaImage(string token);
}
