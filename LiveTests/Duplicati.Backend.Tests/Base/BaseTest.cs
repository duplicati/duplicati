namespace Duplicati.Backend.Tests.Base;

public class BaseTest
{
    
    /// <summary>
    /// Checks if the environment variables are set and if not fails with detailed about the missing variables
    /// </summary>
    /// <param name="requiredEnvironmentVariables">Array of variables to be checked</param>
    protected void CheckRequiredEnvironment(string[] requiredEnvironmentVariables)
    {
        var missingVariables = requiredEnvironmentVariables
            .Where(variable => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable)))
            .ToList();

        if (missingVariables.Any()) Assert.Fail($"Required environment variables not set: {string.Join(", ", missingVariables)}");
    }
}