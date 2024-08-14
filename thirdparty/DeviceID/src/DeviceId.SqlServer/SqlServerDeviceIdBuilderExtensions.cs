namespace DeviceId.SqlServer;

/// <summary>
/// Extension methods for <see cref="SqlServerDeviceIdBuilder"/>.
/// </summary>
public static class SqlServerDeviceIdBuilderExtensions
{
    /// <summary>
    /// Adds the server name to the device identifier.
    /// </summary>
    /// <param name="builder">The <see cref="SqlServerDeviceIdBuilder"/> to add the component to.</param>
    /// <returns>The <see cref="SqlServerDeviceIdBuilder"/> instance.</returns>
    public static SqlServerDeviceIdBuilder AddServerName(this SqlServerDeviceIdBuilder builder)
    {
        var name = $"SqlServerProperty:ServerName";
        var sql = $"select serverproperty('ServerName');";

        return builder.AddQueryResult(name, sql, x =>
        {
            var s = x.ToString();
            var i = s.IndexOf('#');
            return i >= 0 ? s.Substring(0, i) : s;
        });
    }

    /// <summary>
    /// Adds the database name to the device identifier.
    /// </summary>
    /// <param name="builder">The <see cref="SqlServerDeviceIdBuilder"/> to add the component to.</param>
    /// <returns>The <see cref="SqlServerDeviceIdBuilder"/> instance.</returns>
    public static SqlServerDeviceIdBuilder AddDatabaseName(this SqlServerDeviceIdBuilder builder)
    {
        var name = "SqlServerDatabaseName";
        var sql = "select db_name();";

        return builder.AddQueryResult(name, sql);
    }

    /// <summary>
    /// Adds the database ID to the device identifier.
    /// </summary>
    /// <param name="builder">The <see cref="SqlServerDeviceIdBuilder"/> to add the component to.</param>
    /// <returns>The <see cref="SqlServerDeviceIdBuilder"/> instance.</returns>
    public static SqlServerDeviceIdBuilder AddDatabaseId(this SqlServerDeviceIdBuilder builder)
    {
        var name = "SqlServerDatabaseId";
        var sql = "select db_id();";

        return builder.AddQueryResult(name, sql);
    }

    /// <summary>
    /// Adds the specified server property to the device identifier.
    /// </summary>
    /// <param name="builder">The <see cref="SqlServerDeviceIdBuilder"/> to add the component to.</param>
    /// <param name="propertyName">The property name.</param>
    /// <returns>The <see cref="SqlServerDeviceIdBuilder"/> instance.</returns>
    public static SqlServerDeviceIdBuilder AddServerProperty(this SqlServerDeviceIdBuilder builder, string propertyName)
    {
        var name = $"SqlServerProperty:{propertyName}";
        var sql = $"select serverproperty('{propertyName.Replace("'", "''")}')";

        return builder.AddQueryResult(name, sql);
    }

    /// <summary>
    /// Adds the specified extended property to the device identifier.
    /// </summary>
    /// <param name="builder">The <see cref="SqlServerDeviceIdBuilder"/> to add the component to.</param>
    /// <param name="propertyName">The property name.</param>
    /// <returns>The <see cref="SqlServerDeviceIdBuilder"/> instance.</returns>
    public static SqlServerDeviceIdBuilder AddExtendedProperty(this SqlServerDeviceIdBuilder builder, string propertyName)
    {
        var name = $"SqlServerExtendedProperty:{propertyName}";
        var sql = $"select [value] from [sys].[extended_properties] where [name] = '{propertyName.Replace("'", "''")}';";

        return builder.AddQueryResult(name, sql);
    }
}
