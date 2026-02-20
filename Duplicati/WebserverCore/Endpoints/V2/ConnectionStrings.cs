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

using Duplicati.Server.Database;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Dto.V2;
using Duplicati.WebserverCore.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Duplicati.Server;

namespace Duplicati.WebserverCore.Endpoints.V2
{
    public class ConnectionStrings : IEndpointV2
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapGet("/connection-strings", GetConnectionStrings).RequireAuthorization();
            group.MapGet("/connection-string/{id}", GetConnectionString).RequireAuthorization();
            group.MapPost("/connection-strings", AddConnectionString).RequireAuthorization();
            group.MapPut("/connection-string/{id}", UpdateConnectionString).RequireAuthorization();
            group.MapDelete("/connection-string/{id}", DeleteConnectionString).RequireAuthorization();
            group.MapPost("/connection-string/{id}/update-backups", UpdateBackups).RequireAuthorization();
        }

        private static ResponseEnvelope<ConnectionStringDto[]> GetConnectionStrings([FromServices] Connection connection)
        {
            var items = connection.GetConnectionStrings();
            var dtos = items.Select(x => MapToDto(x, connection)).ToArray();
            return ResponseEnvelope.Result(dtos);
        }

        private static ResponseEnvelope<ConnectionStringDto> GetConnectionString([FromServices] Connection connection, long id)
        {
            var item = connection.GetConnectionString(id);
            if (item == null)
                throw new NotFoundException("Connection string not found");

            return ResponseEnvelope.Result(MapToDto(item, connection));
        }

        private static ResponseEnvelope<ConnectionStringDto> AddConnectionString([FromServices] Connection connection, [FromBody] CreateConnectionStringDto input)
        {
            var item = new ConnectionString
            {
                Name = input.Name,
                Description = input.Description,
                BaseUrl = input.BaseUrl
            };

            connection.AddConnectionString(item);
            return ResponseEnvelope.Result(MapToDto(item, connection));
        }

        private static ResponseEnvelope<ConnectionStringDto> UpdateConnectionString([FromServices] Connection connection, long id, [FromBody] UpdateConnectionStringDto input)
        {
            var item = connection.GetConnectionString(id);
            if (item == null)
                throw new NotFoundException("Connection string not found");

            item.Name = input.Name;
            item.Description = input.Description;

            if (input.BaseUrl.Contains(Connection.PASSWORD_PLACEHOLDER))
            {
                item.BaseUrl = QuerystringMasking.Unmask(input.BaseUrl, item.BaseUrl);
            }
            else
            {
                item.BaseUrl = input.BaseUrl;
            }

            connection.UpdateConnectionString(item);
            return ResponseEnvelope.Result(MapToDto(item, connection));
        }

        private static ResponseEnvelope<object> DeleteConnectionString([FromServices] Connection connection, long id)
        {
            var backups = connection.GetBackupsUsingConnectionString(id);
            if (backups.Length > 0)
                throw new ConflictException("Cannot delete connection string because it is used by backups");

            connection.DeleteConnectionString(id);
            return ResponseEnvelope.Result<object>(null);
        }

        private static ResponseEnvelope<BulkUpdateBackupsResponseDto> UpdateBackups([FromServices] Connection connection, long id, [FromBody] BulkUpdateBackupsRequestDto input)
        {
            var cs = connection.GetConnectionString(id);
            if (cs == null)
                throw new NotFoundException("Connection string not found");

            var updatedBackupIds = new List<string>();

            foreach (var backupId in input.BackupIDs)
            {
                var backup = connection.GetBackup(backupId) as Duplicati.Server.Database.Backup;
                if (backup == null)
                    throw new NotFoundException($"Backup {backupId} not found");

                if (backup.ConnectionStringID != id)
                    throw new BadRequestException($"Backup {backupId} does not use connection string {id}");

                backup.TargetURL = MergeConnectionStrings(cs.BaseUrl, backup.TargetURL);
                connection.AddOrUpdateBackupAndSchedule(backup, null);

                updatedBackupIds.Add(backupId);
            }

            return ResponseEnvelope.Result(new BulkUpdateBackupsResponseDto
            {
                UpdatedBackupIDs = updatedBackupIds.ToArray()
            });
        }

        private static string MergeConnectionStrings(string connectionStringUrl, string backupUrl)
        {
            var csUri = new Duplicati.Library.Utility.Uri(connectionStringUrl);
            var backupUri = new Duplicati.Library.Utility.Uri(backupUrl);

            // Start with backup URI to keep Scheme, Host, Port, Path
            var resultUri = backupUri;

            // Merge credentials
            if (csUri.Username != null)
                resultUri = resultUri.SetCredentials(csUri.Username, resultUri.Password);

            if (csUri.Password != null)
                resultUri = resultUri.SetCredentials(resultUri.Username, csUri.Password);

            // Merge Query Parameters
            // Use decodeValues=false to preserve encoding
            var csQuery = Duplicati.Library.Utility.Uri.ParseQueryString(csUri.Query ?? "", false);
            var backupQuery = Duplicati.Library.Utility.Uri.ParseQueryString(backupUri.Query ?? "", false);

            var mergedQuery = new System.Collections.Specialized.NameValueCollection(backupQuery);

            foreach (var key in csQuery.AllKeys)
            {
                if (key != null)
                    mergedQuery[key] = csQuery[key];
            }

            resultUri = resultUri.SetQuery(Duplicati.Library.Utility.Uri.BuildUriQuery(mergedQuery));

            return resultUri.ToString();
        }

        private static ConnectionStringDto MapToDto(ConnectionString item, Connection connection)
        {
            var backups = connection.GetBackupsUsingConnectionString(item.ID);
            return new ConnectionStringDto
            {
                ID = item.ID,
                Name = item.Name,
                Description = item.Description,
                BaseUrl = QuerystringMasking.Mask(item.BaseUrl, Connection.PasswordFieldNames),
                CreatedAt = item.CreatedAt,
                UpdatedAt = item.UpdatedAt,
                Backups = backups.Select(x => new ConnectedBackupDto { ID = x.ID, Name = x.Name }).ToArray()
            };
        }
    }
}
