using System;
using System.Data.Common;

namespace DeviceId.Components;

/// <summary>
/// An implementation of <see cref="IDeviceIdComponent"/> that gets its value from the result of a database command.
/// </summary>
public class DatabaseQueryDeviceIdComponent : IDeviceIdComponent
{
    /// <summary>
    /// A factory used to get a connection to the database.
    /// </summary>
    private readonly Func<DbConnection> _connectionFactory;

    /// <summary>
    /// SQL query that returns a single value to be added to the device identifier.
    /// </summary>
    private readonly string _sql;

    /// <summary>
    /// A function that transforms the result of the query into a string.
    /// </summary>
    private readonly Func<object, string> _valueTransformer;

    /// <summary>
    /// A value determining whether the connection should be disposed after use or not.
    /// </summary>
    private readonly bool _disposeConnection;

    /// <summary>
    /// Initializes a new instance of the <see cref="DatabaseQueryDeviceIdComponent"/> class.
    /// </summary>
    /// <param name="connectionFactory">A factory used to get a connection to the database.</param>
    /// <param name="sql">SQL query that returns a single value to be added to the device identifier.</param>
    /// <param name="valueTransformer">A function that transforms the result of the query into a string.</param>
    public DatabaseQueryDeviceIdComponent(Func<DbConnection> connectionFactory, string sql, Func<object, string> valueTransformer)
        : this(connectionFactory, sql, valueTransformer, true) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="DatabaseQueryDeviceIdComponent"/> class.
    /// </summary>
    /// <param name="connectionFactory">A factory used to get a connection to the database.</param>
    /// <param name="sql">SQL query that returns a single value to be added to the device identifier.</param>
    /// <param name="valueTransformer">A function that transforms the result of the query into a string.</param>
    /// <param name="disposeConnection">A value determining whether the connection should be disposed after use or not. Default is true.</param>
    public DatabaseQueryDeviceIdComponent(Func<DbConnection> connectionFactory, string sql, Func<object, string> valueTransformer, bool disposeConnection)
    {
        _connectionFactory = connectionFactory;
        _sql = sql;
        _valueTransformer = valueTransformer;
        _disposeConnection = disposeConnection;
    }

    /// <summary>
    /// Gets the component value.
    /// </summary>
    /// <returns>The component value.</returns>
    public string GetValue()
    {
        try
        {
            var connection = _connectionFactory.Invoke();
            try
            {
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = _sql;

                var result = command.ExecuteScalar();
                if (result != null)
                {
                    var value = _valueTransformer?.Invoke(result);
                    return value;
                }
            }
            finally
            {
                if (_disposeConnection && connection is not null)
                {
                    connection.Dispose();
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}
