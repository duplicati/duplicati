using System.Data.Common;
using DeviceId.SqlServer;
using NSubstitute;
using Xunit;

namespace DeviceId.Tests.Components;

public class SqlServerComponentTests
{
    [Fact]
    public void ConnectionInstance_DoNotDisposeConnection()
    {
        var connection = Substitute.For<DbConnection>();

        var deviceId = new DeviceIdBuilder()
            .AddSqlServer(connection, sqlDeviceId => sqlDeviceId.AddDatabaseId())
            .ToString();

        connection.DidNotReceive().Dispose();
        connection.DidNotReceive().DisposeAsync();
    }

    [Fact]
    public void ConnectionFactory_DisposeConnection()
    {
        var connection = Substitute.For<DbConnection>();

        var deviceId = new DeviceIdBuilder()
            .AddSqlServer(() => connection, sqlDeviceId => sqlDeviceId.AddDatabaseId())
            .ToString();

        connection.Received().Dispose();
    }
}
