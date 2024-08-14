using System;
using System.Data.Common;
using DeviceId.Components;

namespace DeviceId.SqlServer;

/// <summary>
/// Provides a fluent interface for adding SQL Server components to a device identifier.
/// </summary>
public class SqlServerDeviceIdBuilder
{
    /// <summary>
    /// The base device identifier builder.
    /// </summary>
    private readonly DeviceIdBuilder _baseBuilder;

    /// <summary>
    /// A factory used to get a connection to the SQL Server database.
    /// </summary>
    private readonly Func<DbConnection> _connectionFactory;

    /// <summary>
    /// A value determining whether the connection should be disposed after use.
    /// </summary>
    private readonly bool _disposeConnection;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerDeviceIdBuilder"/> class.
    /// </summary>
    /// <param name="baseBuilder">The base device identifier builder.</param>
    /// <param name="connection">A connection to the SQL Server database.</param>
    public SqlServerDeviceIdBuilder(DeviceIdBuilder baseBuilder, DbConnection connection)
    {
        if (baseBuilder is null)
        {
            throw new ArgumentNullException(nameof(baseBuilder));
        }

        if (connection is null)
        {
            throw new ArgumentNullException(nameof(connection));
        }

        _baseBuilder = baseBuilder;
        _connectionFactory = () => connection;
        _disposeConnection = false;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerDeviceIdBuilder"/> class.
    /// </summary>
    /// <param name="baseBuilder">The base device identifier builder.</param>
    /// <param name="connectionFactory">A factory used to get a connection to the SQL Server database.</param>
    public SqlServerDeviceIdBuilder(DeviceIdBuilder baseBuilder, Func<DbConnection> connectionFactory)
    {
        if (baseBuilder is null)
        {
            throw new ArgumentNullException(nameof(baseBuilder));
        }

        if (connectionFactory is null)
        {
            throw new ArgumentNullException(nameof(connectionFactory));
        }

        _baseBuilder = baseBuilder;
        _connectionFactory = connectionFactory;
        _disposeConnection = true;
    }

    /// <summary>
    /// Adds the result of a SQL query to the device identifier.
    /// </summary>
    /// <param name="componentName">The name of the component.</param>
    /// <param name="sql">SQL query that returns a single value to be added to the device identifier.</param>
    /// <returns>The <see cref="SqlServerDeviceIdBuilder"/> instance.</returns>
    public SqlServerDeviceIdBuilder AddQueryResult(string componentName, string sql)
    {
        return AddQueryResult(componentName, sql, x => x.ToString());
    }

    /// <summary>
    /// Adds the result of a SQL query to the device identifier.
    /// </summary>
    /// <param name="componentName">The name of the component.</param>
    /// <param name="sql">SQL query that returns a single value to be added to the device identifier.</param>
    /// <param name="valueTransformer">A function that transforms the result of the query into a string.</param>
    /// <returns>The <see cref="SqlServerDeviceIdBuilder"/> instance.</returns>
    public SqlServerDeviceIdBuilder AddQueryResult(string componentName, string sql, Func<object, string> valueTransformer)
    {
        _baseBuilder.Components.Add(componentName, new DatabaseQueryDeviceIdComponent(_connectionFactory, sql, valueTransformer, _disposeConnection));

        return this;
    }
}
