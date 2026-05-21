using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Server
{
    public class HttpServer
    {
        private readonly int _port;
        private readonly string _avatarDir;
        private readonly DbHelper _dbHelper;
        private readonly TcpServer _tcpServer;
        private HttpListener? _listener;
        private bool _isRunning;

        public HttpServer(int port, string avatarDir, DbHelper dbHelper, TcpServer tcpServer)
        {
            _port = port;
            _avatarDir = avatarDir;
            _dbHelper = dbHelper;
            _tcpServer = tcpServer;

            if (!Directory.Exists(_avatarDir))
            {
                Directory.CreateDirectory(_avatarDir);
            }
        }

        public void Start()
        {
            _isRunning = true;
            _listener = new HttpListener();
            try
            {
                _listener.Prefixes.Add($"http://*:{_port}/");
                _listener.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HTTP Server] Could not bind to wildcard port {_port} (needs admin rights): {ex.Message}");
                Console.WriteLine("[HTTP Server] Falling back to localhost/127.0.0.1 bindings...");
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{_port}/");
                _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
                _listener.Start();
            }

            Console.WriteLine($"[HTTP Server] Started on port {_port}");
            Task.Run(ListenAsync);
        }

        public void Stop()
        {
            _isRunning = false;
            _listener?.Stop();
            Console.WriteLine("[HTTP Server] Stopped");
        }

        private async Task ListenAsync()
        {
            while (_isRunning)
            {
                try
                {
                    HttpListenerContext context = await _listener!.GetContextAsync();
                    _ = Task.Run(() => HandleRequestAsync(context));
                }
                catch (Exception ex) when (!_isRunning)
                {
                    // Clean shutdown
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[HTTP Server] Error getting context: {ex.Message}");
                }
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext context)
        {
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;

            string urlPath = request.Url?.AbsolutePath.ToLower() ?? string.Empty;
            string method = request.HttpMethod.ToUpper();

            try
            {
                // Enable CORS
                response.Headers.Add("Access-Control-Allow-Origin", "*");
                response.Headers.Add("Access-Control-Allow-Methods", "POST, GET, OPTIONS");
                response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

                if (method == "OPTIONS")
                {
                    response.StatusCode = (int)HttpStatusCode.OK;
                    response.Close();
                    return;
                }

                if (method == "GET")
                {
                    if (urlPath == "/" || urlPath == "/register")
                    {
                        await ServeFileAsync(response, "Web/register.html", "text/html");
                    }
                    else if (urlPath == "/upload")
                    {
                        await ServeFileAsync(response, "Web/upload.html", "text/html");
                    }
                    else if (urlPath == "/style.css")
                    {
                        await ServeFileAsync(response, "Web/style.css", "text/css");
                    }
                    else if (urlPath.StartsWith("/avatars/"))
                    {
                        string filename = Path.GetFileName(urlPath);
                        string filePath = Path.Combine(_avatarDir, filename);
                        if (File.Exists(filePath))
                        {
                            string ext = Path.GetExtension(filePath).ToLower();
                            string contentType = ext == ".png" ? "image/png" : ext == ".gif" ? "image/gif" : "image/jpeg";
                            await ServeFileAsync(response, filePath, contentType);
                        }
                        else
                        {
                            response.StatusCode = (int)HttpStatusCode.NotFound;
                            byte[] buffer = Encoding.UTF8.GetBytes("Avatar not found.");
                            response.ContentLength64 = buffer.Length;
                            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                            response.Close();
                        }
                    }
                    else
                    {
                        response.StatusCode = (int)HttpStatusCode.NotFound;
                        byte[] buffer = Encoding.UTF8.GetBytes("404 - Not Found");
                        response.ContentLength64 = buffer.Length;
                        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                        response.Close();
                    }
                }
                else if (method == "POST")
                {
                    if (urlPath == "/api/register")
                    {
                        await HandleApiRegisterAsync(request, response);
                    }
                    else if (urlPath == "/api/upload")
                    {
                        await HandleApiUploadAsync(request, response);
                    }
                    else
                    {
                        response.StatusCode = (int)HttpStatusCode.NotFound;
                        response.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HTTP Server] Exception handling request {urlPath}: {ex.Message}");
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
                response.Close();
            }
        }

        private async Task ServeFileAsync(HttpListenerResponse response, string relativeOrAbsolutePath, string contentType)
        {
            // If path is already absolute (e.g. avatar files), use it directly.
            // Otherwise resolve relative to the app's base directory.
            string filePath = Path.IsPathRooted(relativeOrAbsolutePath)
                ? relativeOrAbsolutePath
                : Path.Combine(AppContext.BaseDirectory, relativeOrAbsolutePath);

            if (!File.Exists(filePath))
            {
                response.StatusCode = (int)HttpStatusCode.NotFound;
                byte[] err = Encoding.UTF8.GetBytes($"File not found: {relativeOrAbsolutePath}");
                response.ContentLength64 = err.Length;
                await response.OutputStream.WriteAsync(err, 0, err.Length);
                response.Close();
                return;
            }

            response.ContentType = contentType;
            byte[] fileBytes = await File.ReadAllBytesAsync(filePath);
            response.ContentLength64 = fileBytes.Length;
            await response.OutputStream.WriteAsync(fileBytes, 0, fileBytes.Length);
            response.Close();
        }

        private async Task HandleApiRegisterAsync(HttpListenerRequest request, HttpListenerResponse response)
        {
            using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
            string body = await reader.ReadToEndAsync();
            
            try
            {
                var data = JsonSerializer.Deserialize<RegisterRequest>(body);
                if (data == null)
                {
                    await WriteJsonResponseAsync(response, HttpStatusCode.BadRequest, new { success = false, error = "Invalid JSON data." });
                    return;
                }

                bool ok = _dbHelper.RegisterUser(data.email, data.password, data.displayName, out string dbError);
                if (ok)
                {
                    await WriteJsonResponseAsync(response, HttpStatusCode.OK, new { success = true });
                }
                else
                {
                    await WriteJsonResponseAsync(response, HttpStatusCode.BadRequest, new { success = false, error = dbError });
                }
            }
            catch (Exception ex)
            {
                await WriteJsonResponseAsync(response, HttpStatusCode.InternalServerError, new { success = false, error = ex.Message });
            }
        }

        private async Task HandleApiUploadAsync(HttpListenerRequest request, HttpListenerResponse response)
        {
            using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
            string body = await reader.ReadToEndAsync();
            
            try
            {
                var data = JsonSerializer.Deserialize<UploadRequest>(body);
                if (data == null || string.IsNullOrEmpty(data.email) || string.IsNullOrEmpty(data.password) || string.IsNullOrEmpty(data.avatarBase64))
                {
                    await WriteJsonResponseAsync(response, HttpStatusCode.BadRequest, new { success = false, error = "Invalid upload parameters." });
                    return;
                }

                var user = _dbHelper.AuthenticateUser(data.email, data.password);
                if (user == null)
                {
                    await WriteJsonResponseAsync(response, HttpStatusCode.Unauthorized, new { success = false, error = "Invalid credentials." });
                    return;
                }

                byte[] imageBytes = Convert.FromBase64String(data.avatarBase64);
                string fileExt = Path.GetExtension(data.fileName);
                if (string.IsNullOrEmpty(fileExt)) fileExt = ".png";
                
                string uniqueFileName = $"{user.Id}_{Guid.NewGuid().ToString().Substring(0, 8)}{fileExt}";
                string savePath = Path.Combine(_avatarDir, uniqueFileName);

                await File.WriteAllBytesAsync(savePath, imageBytes);

                bool updated = _dbHelper.UpdateAvatar(user.Id, uniqueFileName);
                if (updated)
                {
                    await WriteJsonResponseAsync(response, HttpStatusCode.OK, new { success = true });

                    _tcpServer.BroadcastPresence(user.Id);

                    var onlineClient = TcpServer.GetClient(user.Id);
                    if (onlineClient != null)
                    {
                        onlineClient.SendPacket("PresenceChanged", new
                        {
                            UserId = user.Id,
                            DisplayName = user.DisplayName,
                            Status = onlineClient.CurrentUser?.Status ?? "Online",
                            CustomSign = onlineClient.CurrentUser?.CustomSign ?? "",
                            AvatarFilename = uniqueFileName
                        });
                    }
                }
                else
                {
                    await WriteJsonResponseAsync(response, HttpStatusCode.InternalServerError, new { success = false, error = "Failed to update profile picture in database." });
                }
            }
            catch (Exception ex)
            {
                await WriteJsonResponseAsync(response, HttpStatusCode.InternalServerError, new { success = false, error = ex.Message });
            }
        }

        private async Task WriteJsonResponseAsync(HttpListenerResponse response, HttpStatusCode code, object payload)
        {
            response.StatusCode = (int)code;
            response.ContentType = "application/json";
            byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            response.Close();
        }

        private class RegisterRequest
        {
            public string email { get; set; } = string.Empty;
            public string password { get; set; } = string.Empty;
            public string displayName { get; set; } = string.Empty;
        }

        private class UploadRequest
        {
            public string email { get; set; } = string.Empty;
            public string password { get; set; } = string.Empty;
            public string fileName { get; set; } = string.Empty;
            public string avatarBase64 { get; set; } = string.Empty;
        }
    }
}
