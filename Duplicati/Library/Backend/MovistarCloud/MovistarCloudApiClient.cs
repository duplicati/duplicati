using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection.Metadata;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Backend.MovistarCloud
{
    internal sealed class MovistarCloudApiClient : IDisposable
    {
        private readonly string _email;
        private readonly string _password;
        private readonly int _timeoutSeconds;
        private readonly string _deviceId;

        private readonly string _userAgent;

        private HttpClient? _http;              // with cookies
        private CookieContainer? _cookies;
        private string? _validationKey;
        private bool _reloginAttempted;

        private HttpClient Http
            => _http ?? throw new InvalidOperationException("HTTP client not initialized. Call EnsureLoggedInAsync first.");

        private string ValidationKey
            => _validationKey ?? throw new InvalidOperationException("ValidationKey not initialized. Call EnsureLoggedInAsync first.");

        public MovistarCloudApiClient(string email, string password, string deviceID, int timeoutSeconds)
        {
            _email = email ?? throw new ArgumentNullException(nameof(email));
            _password = password ?? throw new ArgumentNullException(nameof(password));
            _timeoutSeconds = timeoutSeconds;
            _deviceId = deviceID;

            _userAgent = "Duplicati-MovistarCloud-Unofficial/0.1";

            // Login immediately
            EnsureLoggedInAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        public void Dispose() => _http?.Dispose();

        // Generic wrapper: relogin once on session expiration
        public async Task<T> WithAutoRelogin<T>(Func<CancellationToken, Task<T>> op, CancellationToken ct)
        {
            try
            {
                _reloginAttempted = false;
                return await op(ct).ConfigureAwait(false);
            }
            catch (MovistarCloudSessionExpiredException) when (!_reloginAttempted)
            {
                _reloginAttempted = true;
                await EnsureLoggedInAsync(ct).ConfigureAwait(false);
                return await op(ct).ConfigureAwait(false);
            }
        }

        public async Task WithAutoRelogin(Func<CancellationToken, Task> op, CancellationToken ct)
        {
            await WithAutoRelogin(async x => { await op(x).ConfigureAwait(false); return true; }, ct).ConfigureAwait(false);
        }

        private async Task EnsureLoggedInAsync(CancellationToken ct)
        {
            var (js, vk) = await LoginAsync(_email, _password, ct).ConfigureAwait(false);
            _validationKey = vk;
            BuildHttpClientWithSession(js, vk);
        }

        private void BuildHttpClientWithSession(string jsessionid, string validationkey)
        {
            _http?.Dispose();

            _cookies = new CookieContainer();
            _cookies.Add(new Uri("https://micloud.movistar.es"), new Cookie("JSESSIONID", jsessionid, "/", "micloud.movistar.es"));
            _cookies.Add(new Uri("https://micloud.movistar.es"), new Cookie("validationkey", validationkey, "/", "micloud.movistar.es"));

            var handler = new HttpClientHandler
            {
                CookieContainer = _cookies,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            _http = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(_timeoutSeconds)
            };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(_userAgent);
        }

        private static bool LooksLikeHtml(HttpResponseMessage resp, string body)
        {
            var ct = resp.Content.Headers.ContentType?.ToString() ?? "";
            if ((int)resp.StatusCode == 401) return true;
            if (ct.Contains("text/html", StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(body) && body.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(body) && body.Contains("<title>Movistar Cloud</title>", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private async Task<string> ReadJsonOrThrowAsync(HttpResponseMessage resp, CancellationToken ct)
        {
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (LooksLikeHtml(resp, body))
                throw new MovistarCloudSessionExpiredException("Session expired (HTML/401).");

            resp.EnsureSuccessStatusCode();
            return body;
        }

        private string Vk(string url)
            => url.Contains("validationkey=") ? url : $"{url}{(url.Contains("?") ? "&" : "?")}validationkey={Uri.EscapeDataString(ValidationKey)}";

        // ---- LOGIN (email/password) ----
        private async Task<(string JSessionId, string ValidationKey)> LoginAsync(string email, string password, CancellationToken ct)
        {

            var handler = new HttpClientHandler
            {
                CookieContainer = new CookieContainer(),
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(_timeoutSeconds)};
            http.DefaultRequestHeaders.UserAgent.ParseAdd(_userAgent);
            

            // Cualquier endpoint público/ligero sirve; usamos la home o system/information
            using var respInitial = await http.GetAsync("https://micloud.movistar.es/", ct).ConfigureAwait(false);
            respInitial.EnsureSuccessStatusCode();
            // A partir de aquí, si el servidor emitió cookie, ya está en _cookies


            var url = "https://micloud.movistar.es/sapi/login?action=login";
            using var content = new FormUrlEncodedContent(new[]
            {
            new KeyValuePair<string,string>("login", email),
            new KeyValuePair<string,string>("password", password)
            });


            using var req = new HttpRequestMessage(
                HttpMethod.Post,
                url
            );
            req.Content = content;            
            req.Headers.Add("X-deviceid", _deviceId);

            using var resp = await http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();




            
            
            

            using var doc = JsonDocument.Parse(body);
            var data = doc.RootElement.GetProperty("data");

            var js = data.GetProperty("jsessionid").GetString();
            var vk = data.GetProperty("validationkey").GetString();

            if (string.IsNullOrWhiteSpace(js) || string.IsNullOrWhiteSpace(vk))
                throw new Exception("Login succeeded but did not return jsessionid/validationkey.");

            return (js!, vk!);
        }
                
        // ---- LIST files in folder ----
        public async Task<List<RemoteFile>> ListFilesAsync(long folderId, int limit, CancellationToken ct)
        {
            if (folderId==0) {

                folderId = await GetRootFolderIdAsync(ct).ConfigureAwait(false);
            }

            //Listar Ficheros
            var url = Vk($"https://micloud.movistar.es/sapi/media?action=get&folderid={folderId}&limit={limit}");
            var payload = new { data = new { fields = new[] { "name", "modificationdate", "size", "etag" } } };

            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var resp = await Http.PostAsync(url, content, ct).ConfigureAwait(false);
            var body = await ReadJsonOrThrowAsync(resp, ct).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(body);
            var media = doc.RootElement.GetProperty("data").GetProperty("media");

            var result = new List<RemoteFile>(media.GetArrayLength());
            foreach (var item in media.EnumerateArray())
            {
                var id = long.Parse(item.GetProperty("id").GetString() ?? "0");
                var name = item.GetProperty("name").GetString() ?? "";
                var size = item.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                var md = item.TryGetProperty("modificationdate", out var m) ? m.GetInt64() : 0;
                // var st = item.TryGetProperty("status", out var stp) ? stp.GetString() ?? "" : "";
                var et = item.TryGetProperty("etag", out var ep) ? ep.GetString() ?? "" : "";

                var last = md > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(md).UtcDateTime : DateTime.UtcNow;
                result.Add(new RemoteFile(id, name, size, last,false, et));
            }

            //Listar Directorios
            url = Vk($"https://micloud.movistar.es/sapi/media/folder?action=list&parentid={folderId}&limit={limit}");
            payload = new { data = new { fields = new[] { "name", "date"  } } };

            using var contentDir = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var respDir = await Http.PostAsync(url, contentDir, ct).ConfigureAwait(false);
            body = await ReadJsonOrThrowAsync(respDir, ct).ConfigureAwait(false);

            using var docDir = JsonDocument.Parse(body);
            media = docDir.RootElement.GetProperty("data").GetProperty("folders");

            
            foreach (var item in media.EnumerateArray())
            {
                var id = item.GetProperty("id").GetInt64();
                var name = item.GetProperty("name").GetString() ?? "";
                var size = 0;
                var md = item.TryGetProperty("date", out var m) ? m.GetInt64() : 0;
                // var st = item.TryGetProperty("status", out var stp) ? stp.GetString() ?? "" : "";
                var et = "";

                var last = md > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(md).UtcDateTime : DateTime.UtcNow;
                result.Add(new RemoteFile(id, name, size, last, true, et));
            }


            return result;
        }

        // ---- UPLOAD ----
        public async Task<UploadResult> UploadFileAsync(long folderId, string remoteName, string localFilePath, CancellationToken ct)
        {
            var fi = new FileInfo(localFilePath);
            if (!fi.Exists) throw new FileNotFoundException("Local file not found", localFilePath);

            var url = Vk("https://upload.micloud.movistar.es/sapi/upload?action=save&acceptasynchronous=true");

            var meta = new
            {
                data = new
                {
                    name = remoteName,
                    size = fi.Length,
                    modificationdate = fi.LastWriteTimeUtc.ToString("yyyyMMdd'T'HHmmss'Z'"),
                    contenttype = "application/octet-stream",
                    folderid = folderId
                }
            };

            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(JsonSerializer.Serialize(meta), Encoding.UTF8, "application/json"), "data");

            await using var fs = File.OpenRead(localFilePath);
            var filePart = new StreamContent(fs);
            filePart.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(filePart, "file", remoteName);

            using var resp = await Http.PostAsync(url, form, ct).ConfigureAwait(false);
            var body = await ReadJsonOrThrowAsync(resp, ct).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(body);
            var idElem = doc.RootElement.GetProperty("id");
            var id = idElem.ValueKind == JsonValueKind.String ? long.Parse(idElem.GetString()!) : idElem.GetInt64();
            var st = doc.RootElement.TryGetProperty("status", out var stp) ? stp.GetString() ?? "" : "";
            var et = doc.RootElement.TryGetProperty("etag", out var etp) ? etp.GetString() ?? "" : "";

            return new UploadResult(id, st, et);
        }

        public async Task<string> GetValidationStatusAsync(long id, CancellationToken ct)
        {
            var url = Vk("https://micloud.movistar.es/sapi/media?action=get-validation-status");
            var payload = new { data = new { ids = new[] { new { id } } } };

            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var resp = await Http.PostAsync(url, content, ct).ConfigureAwait(false);
            var body = await ReadJsonOrThrowAsync(resp, ct).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.GetProperty("data").GetProperty("ids")[0].GetProperty("status").GetString() ?? "";
        }

        // ---- DOWNLOAD ----
        public async Task<string> GetDownloadUrlAsync(long fileId, CancellationToken ct)
        {
            var url = Vk("https://micloud.movistar.es/sapi/media?action=get&origin=omh,dropbox");
            var payload = new { data = new { ids = new[] { fileId }, fields = new[] { "url", "name", "size", "etag" } } };

            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var resp = await Http.PostAsync(url, content, ct).ConfigureAwait(false);
            var body = await ReadJsonOrThrowAsync(resp, ct).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.GetProperty("data").GetProperty("media")[0].GetProperty("url").GetString()!;
        }

        public async Task DownloadToFileAsync(string signedUrl, string localFilePath, CancellationToken ct)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(localFilePath) ?? ".");
            using var resp = await Http.GetAsync(signedUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            await using var rs = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var fs = File.Create(localFilePath);
            await rs.CopyToAsync(fs, 81920, ct).ConfigureAwait(false);
        }

        // ---- DELETE (soft) ----
        public async Task SoftDeleteFileAsync(long fileId, CancellationToken ct)
        {
            var url = Vk("https://micloud.movistar.es/sapi/media/file?action=delete&softdelete=true");
            var payload = new { data = new { files = new[] { fileId } } };

            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var resp = await Http.PostAsync(url, content, ct).ConfigureAwait(false);
            _ = await ReadJsonOrThrowAsync(resp, ct).ConfigureAwait(false);
        }

        // ---- DIAGNOSTICS (TestAsync only) ----
        /*public async Task<StorageSpace> GetStorageSpaceAsync(CancellationToken ct)
        {
            var url = Vk("https://micloud.movistar.es/sapi/media?action=get-storage-space&softdeleted=true");
            using var resp = await Http.PostAsync(url, new StringContent("", Encoding.UTF8, "application/x-www-form-urlencoded"), ct).ConfigureAwait(false);
            var body = await ReadJsonOrThrowAsync(resp, ct).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(body);
            var d = doc.RootElement.GetProperty("data");
            return new StorageSpace(
                Used: d.GetProperty("used").GetInt64(),
                Free: d.GetProperty("free").GetInt64(),
                SoftDeleted: d.GetProperty("softdeleted").GetInt64(),
                NoLimit: d.TryGetProperty("nolimit", out var nl) && nl.GetBoolean()
            );
        }

        public async Task<List<TrashEntry>> ListTrashAsync(int pageSize, CancellationToken ct)
        {
            var url = Vk("https://micloud.movistar.es/sapi/media/trash?action=get");

            var payload = new Dictionary<string, object>
            {
                ["data"] = new Dictionary<string, object>
                {
                    ["max-page-size"] = pageSize,
                    ["origin"] = new[] { "omh", "dropbox" }
                }
            };


            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var resp = await Http.PostAsync(url, content, ct).ConfigureAwait(false);
            var body = await ReadJsonOrThrowAsync(resp, ct).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(body);
            var entries = doc.RootElement.GetProperty("data").GetProperty("entries");

            var res = new List<TrashEntry>(entries.GetArrayLength());
            foreach (var e in entries.EnumerateArray())
            {
                res.Add(new TrashEntry(
                    Id: e.GetProperty("id").GetInt64(),
                    Name: e.GetProperty("name").GetString() ?? "",
                    Size: e.TryGetProperty("size", out var s) ? s.GetInt64() : 0,
                    Origin: e.TryGetProperty("origin", out var o) ? o.GetString() ?? "" : ""
                ));
            }
            return res;
        }
        */

        public async Task<long?> TryGetFolderIdByNameAsync(long parentId, string name, CancellationToken ct)
        {
            // Lista subcarpetas bajo parentId y busca por nombre exacto (case-sensitive según SAPI)
            var url = Vk($"https://micloud.movistar.es/sapi/media/folder?action=list&parentid={parentId}&limit=2000");

            using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            var body = await ReadJsonOrThrowAsync(resp, ct).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(body);
            var folders = doc.RootElement.GetProperty("data").GetProperty("folders");

            foreach (var f in folders.EnumerateArray())
            {
                var fname = f.GetProperty("name").GetString() ?? "";
                if (string.Equals(fname, name, StringComparison.Ordinal))
                    return f.GetProperty("id").GetInt64();
            }

            return null;
        }

        /// <summary>
        /// Crea una carpeta bajo parentId con nombre `name`.
        /// Devuelve el ID de la carpeta (si ya existe, devuelve el existente).
        /// </summary>
        public async Task<long> CreateFolderAsync(long parentId, string name, CancellationToken ct)
        {
            if (parentId == 0)
            {
                parentId = await GetRootFolderIdAsync(ct).ConfigureAwait(false);
            }
            // 1) Idempotencia: si ya existe, devolver su id
            var existing = await TryGetFolderIdByNameAsync(parentId, name, ct).ConfigureAwait(false);
            if (existing.HasValue)
                return existing.Value;

            // 2) Crear carpeta
            var url = Vk("https://micloud.movistar.es/sapi/media/folder?action=save");

            // Payload JSON
            var payload = new
            {
                data = new
                {                                       
                    magic = false,
                    offline = false,
                    name = name,
                    parentid = parentId
                    
                }
            };

            using var content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync(url, content, ct).ConfigureAwait(false);
            var body = await ReadJsonOrThrowAsync(resp, ct).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(body);
            // Suele devolver { "data": { "folder": { "id": xxxx..., "name": "...", ... } }, ... }
            var createdFolder = doc.RootElement.GetProperty("data").GetProperty("folder");
            var id = createdFolder.GetProperty("id").GetInt64();

            return id;
        }

        public async Task<long> GetRootFolderIdAsync(CancellationToken ct)
        {
            var url = Vk("https://micloud.movistar.es/sapi/media/folder/root?action=get");
            using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            var body = await ReadJsonOrThrowAsync(resp, ct).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(body);
            // Respuesta típica: {"data":{"folders":[{"name":"/","id":xxxxxx,...}]}, ...}
            var folders = doc.RootElement.GetProperty("data").GetProperty("folders");
            return folders[0].GetProperty("id").GetInt64();
        }

        public async Task AssertFolderExistsByIdAsync(long folderId, CancellationToken ct)
        {
            // Trivial: listar 1 elemento de esa carpeta; si falla, FolderMissing
            var url = Vk($"https://micloud.movistar.es/sapi/media?action=get&folderid={folderId}&limit=1");
            // Puedes usar POST con body {"data":{"fields":["name"]}}, pero GET aquí es suficiente para validar
            using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            _ = await ReadJsonOrThrowAsync(resp, ct).ConfigureAwait(false);
        }

        public async Task<long> EnsureFolderPathAsync(string path, CancellationToken ct)
        {
            // Normalizamos ruta
            var p = path.Trim();
            if (string.IsNullOrWhiteSpace(p))
                throw new ArgumentException("root-folder-path vacío");

            // Partimos en segmentos; toleramos / inicial y múltiple slash
            var segments = p.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            // Punto de partida = raíz de MiCloud
            var currentId = await GetRootFolderIdAsync(ct).ConfigureAwait(false);

            foreach (var segment in segments)
            {
                // 1) buscar subcarpeta con ese nombre bajo currentId
                var existing = await TryGetFolderIdByNameAsync(currentId, segment, ct).ConfigureAwait(false);
                if (existing.HasValue)
                {
                    currentId = existing.Value;
                    continue;
                }

                // 2) no existe -> crearla
                currentId = await CreateFolderAsync(currentId, segment, ct).ConfigureAwait(false);
            }

            return currentId; // id final de la ruta
        }

        // DTOs
        public sealed record RemoteFile(long Id, string Name, long Size, DateTime LastWriteUtc, bool IsFolder, string ETag);
        public sealed record UploadResult(long Id, string Status, string ETag);
        public sealed record StorageSpace(long Used, long Free, long SoftDeleted, bool NoLimit);
        public sealed record TrashEntry(long Id, string Name, long Size, string Origin);
    }

    internal sealed class MovistarCloudSessionExpiredException : Exception
    {
        public MovistarCloudSessionExpiredException(string message) : base(message) { }
    }
}
