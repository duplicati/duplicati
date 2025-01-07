// Copyright (C) 2025, The Duplicati Team
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
using Duplicati.Library.Main.Database;
using Duplicati.Server.Database;
using Duplicati.WebserverCore.Abstractions;

namespace Duplicati.WebserverCore.Services;

public class TokenFamilyStore(Connection connection) : ITokenFamilyStore
{
    // Use Dapper?
    public Task<ITokenFamilyStore.TokenFamily> CreateTokenFamily(string userId, CancellationToken ct)
    {
        var familyId = System.Security.Cryptography.RandomNumberGenerator.GetHexString(16);
        var counter = System.Security.Cryptography.RandomNumberGenerator.GetInt32(1024) % 1024;
        var lastUpdated = DateTime.UtcNow;
        connection.ExecuteWithCommand(cmd =>
        {
            cmd.CommandText = @"INSERT INTO TokenFamily (""Id"", ""UserId"", ""Counter"", ""LastUpdated"") VALUES (?, ?, ?, ?)";
            cmd.AddParameter(familyId);
            cmd.AddParameter(userId);
            cmd.AddParameter(counter);
            cmd.AddParameter(lastUpdated.Ticks);
            cmd.ExecuteNonQuery();
        });

        return Task.FromResult(new ITokenFamilyStore.TokenFamily(familyId, userId, counter, lastUpdated));
    }

    public Task<ITokenFamilyStore.TokenFamily> GetTokenFamily(string userId, string familyId, CancellationToken ct)
    {
        ITokenFamilyStore.TokenFamily? family = null;
        connection.ExecuteWithCommand(cmd =>
        {
            cmd.CommandText = @"SELECT ""Id"", ""UserId"", ""Counter"", ""LastUpdated"" FROM ""TokenFamily"" WHERE ""Id"" = ? AND ""UserId"" = ?";
            cmd.AddParameter(familyId);
            cmd.AddParameter(userId);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return;

            family = new ITokenFamilyStore.TokenFamily(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), new DateTime(reader.GetInt64(3)));
        });
        return Task.FromResult(family ?? throw new Exceptions.UnauthorizedException("Token family not found"));
    }

    public Task<ITokenFamilyStore.TokenFamily> IncrementTokenFamily(ITokenFamilyStore.TokenFamily tokenFamily, CancellationToken ct)
    {
        var nextCounter = tokenFamily.Counter + 1;
        var lastUpdated = DateTime.UtcNow;
        connection.ExecuteWithCommand(cmd =>
        {
            cmd.CommandText = @"UPDATE ""TokenFamily"" SET ""Counter"" = ?, ""LastUpdated"" = ? WHERE ""Id"" = ? AND ""UserId"" = ? AND ""Counter"" = ?";
            cmd.AddParameter(nextCounter);
            cmd.AddParameter(lastUpdated.Ticks);
            cmd.AddParameter(tokenFamily.Id);
            cmd.AddParameter(tokenFamily.UserId);
            cmd.AddParameter(tokenFamily.Counter);
            if (cmd.ExecuteNonQuery() != 1)
                throw new Exceptions.ConflictException("Token family counter mismatch or not found");
        });

        return Task.FromResult(new ITokenFamilyStore.TokenFamily(tokenFamily.Id, tokenFamily.UserId, nextCounter, lastUpdated));
    }

    public Task InvalidateTokenFamily(string userId, string familyId, CancellationToken ct)
    {
        connection.ExecuteWithCommand(cmd =>
        {
            cmd.CommandText = @"DELETE FROM ""TokenFamily"" WHERE ""Id"" = ? AND ""UserId"" = ?";
            cmd.AddParameter(familyId);
            cmd.AddParameter(userId);
            if (cmd.ExecuteNonQuery() != 1)
                throw new Exceptions.NotFoundException("Token family not found");
        });

        return Task.CompletedTask;
    }

    public Task InvalidateAllTokenFamilies(string userId, CancellationToken ct)
    {
        connection.ExecuteWithCommand(cmd =>
        {
            cmd.CommandText = @"DELETE FROM ""TokenFamily"" WHERE ""UserId"" = ?";
            cmd.AddParameter(userId);
            cmd.ExecuteNonQuery();
        });

        return Task.CompletedTask;
    }

    public Task InvalidateAllTokens(CancellationToken ct)
    {
        connection.ExecuteWithCommand(cmd =>
        {
            cmd.CommandText = @"DELETE FROM ""TokenFamily""";
            cmd.ExecuteNonQuery();
        });

        return Task.CompletedTask;
    }

}
