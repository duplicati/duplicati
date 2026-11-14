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
using System.Text.Json;
using Duplicati.Library.Main.Database;
using Duplicati.Server.Database;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Middlewares;

namespace Duplicati.WebserverCore.Services;

/// <summary>
/// Holds the JWT configuration stored in the database, applying the runtime overrides
/// </summary>
public class JWTConfigProvider : IJWTConfigProvider
{
    /// <summary>
    /// The database connection
    /// </summary>
    private readonly Connection m_connection;
    /// <summary>
    /// The refresh token lifetime override in minutes; zero or less means no override
    /// </summary>
    private readonly int m_tokenLifetimeInMinutes;
    /// <summary>
    /// Guards resets so concurrent calls do not interleave
    /// </summary>
    private readonly object m_lock = new();
    /// <summary>
    /// The active configuration
    /// </summary>
    private volatile JWTConfig m_current;

    /// <summary>
    /// Creates a new provider, generating and storing a configuration if the database has none
    /// </summary>
    /// <param name="connection">The database connection</param>
    /// <param name="tokenLifetimeInMinutes">The refresh token lifetime override in minutes; zero or less means no override</param>
    public JWTConfigProvider(Connection connection, int tokenLifetimeInMinutes)
    {
        m_connection = connection;
        m_tokenLifetimeInMinutes = tokenLifetimeInMinutes;

        if (string.IsNullOrWhiteSpace(connection.ApplicationSettings.JWTConfig))
            connection.ApplicationSettings.JWTConfig = JsonSerializer.Serialize(JWTConfig.Create());

        var stored = JsonSerializer.Deserialize<JWTConfig>(connection.ApplicationSettings.JWTConfig)
            ?? throw new Exception("Failed to deserialize JWTConfig");

        m_current = ApplyOverrides(stored);
    }

    /// <inheritdoc />
    public JWTConfig Current => m_current;

    /// <inheritdoc />
    public event EventHandler? ConfigReset;

    /// <inheritdoc />
    public void Reset()
    {
        lock (m_lock)
        {
            var created = JWTConfig.Create();
            m_connection.ApplicationSettings.JWTConfig = JsonSerializer.Serialize(created);
            m_current = ApplyOverrides(created);

            // Tokens signed with the new key can only reference families created from now on
            m_connection.ExecuteWithCommand(cmd => cmd.ExecuteNonQuery(@"DELETE FROM ""TokenFamily"""));

            // The MCP tokens were signed with the previous key, so none of the listed tokens works anymore
            m_connection.ApplicationSettings.McpTokens = null;
        }

        ConfigReset?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Applies the runtime overrides to a stored configuration
    /// </summary>
    /// <param name="config">The stored configuration</param>
    /// <returns>The configuration to use</returns>
    private JWTConfig ApplyOverrides(JWTConfig config)
    {
        if (m_tokenLifetimeInMinutes > 0)
            config = config with { RefreshTokenDurationInMinutes = m_tokenLifetimeInMinutes };

        if (config.PbkdfConfig == null)
            config = config with { PbkdfConfig = Server.PbkdfConfig.Default };

        return config;
    }
}
