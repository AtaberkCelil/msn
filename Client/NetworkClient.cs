using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Client
{
    public class NetworkClient
    {
        private static NetworkClient? _instance;
        public static NetworkClient Instance => _instance ??= new NetworkClient();

        public static string HttpUrl { get; set; } = "http://localhost:8080";
        public static string AesKey { get; set; } = "WLM_MESSENGER_SECRET_KEY_32BYTES!";
        public static string AesIV { get; set; } = "WLM_INIT_VECTOR16";

        private TcpClient? _client;
        private NetworkStream? _stream;
        private StreamReader? _reader;
        private StreamWriter? _writer;
        private bool _isConnected;
        private Task? _readTask;

        public event Action<string, JsonElement>? PacketReceived;
        public event Action? Disconnected;

        public bool IsConnected => _isConnected && _client != null && _client.Connected;

        private NetworkClient() { }

        public async Task<bool> ConnectAsync(string ip, int port)
        {
            try
            {
                if (IsConnected)
                {
                    Disconnect();
                }

                _client = new TcpClient();
                await _client.ConnectAsync(ip, port);
                _stream = _client.GetStream();
                _reader = new StreamReader(_stream, Encoding.UTF8);
                _writer = new StreamWriter(_stream, Encoding.UTF8) { AutoFlush = true };
                _isConnected = true;

                _readTask = Task.Run(ReadLoopAsync);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NetworkClient] Connection failed: {ex.Message}");
                return false;
            }
        }

        private async Task ReadLoopAsync()
        {
            try
            {
                while (_isConnected && _client != null && _client.Connected)
                {
                    string? encryptedLine = await _reader!.ReadLineAsync();
                    if (encryptedLine == null) break;

                    string decryptedJson = EncryptionHelper.Decrypt(encryptedLine, AesKey, AesIV);
                    if (string.IsNullOrWhiteSpace(decryptedJson)) continue;

                    using var doc = JsonDocument.Parse(decryptedJson);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("Action", out var actionProp))
                    {
                        string action = actionProp.GetString() ?? string.Empty;
                        var payload = root.GetProperty("Payload");
                        
                        PacketReceived?.Invoke(action, payload);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NetworkClient] Read error: {ex.Message}");
            }
            finally
            {
                CloseConnection();
            }
        }

        public void SendPacket(string action, object payload)
        {
            try
            {
                if (!IsConnected) return;

                string json = JsonSerializer.Serialize(new { Action = action, Payload = payload });
                string encrypted = EncryptionHelper.Encrypt(json, AesKey, AesIV);
                if (!string.IsNullOrEmpty(encrypted))
                {
                    _writer?.WriteLine(encrypted);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NetworkClient] Send error: {ex.Message}");
            }
        }

        public void Disconnect()
        {
            CloseConnection();
        }

        private void CloseConnection()
        {
            if (!_isConnected) return;
            _isConnected = false;

            try { _reader?.Dispose(); } catch { }
            try { _writer?.Dispose(); } catch { }
            try { _stream?.Dispose(); } catch { }
            try { _client?.Close(); } catch { }

            _client = null;
            _stream = null;
            _reader = null;
            _writer = null;

            Disconnected?.Invoke();
        }
    }
}
