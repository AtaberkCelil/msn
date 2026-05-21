using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Server
{
    public class TcpServer
    {
        private readonly int _port;
        private readonly string _aesKey;
        private readonly string _aesIV;
        private readonly DbHelper _dbHelper;
        private TcpListener? _listener;
        private bool _isRunning;
        
        // Active connected users: Key = UserId, Value = ClientHandler
        private static readonly ConcurrentDictionary<int, ClientHandler> ActiveClients = new();

        public TcpServer(int port, string aesKey, string aesIV, DbHelper dbHelper)
        {
            _port = port;
            _aesKey = aesKey;
            _aesIV = aesIV;
            _dbHelper = dbHelper;
        }

        public void Start()
        {
            _isRunning = true;
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            Console.WriteLine($"[TCP Server] Started on port {_port}");

            Task.Run(AcceptConnectionsAsync);
        }

        public void Stop()
        {
            _isRunning = false;
            _listener?.Stop();
            foreach (var client in ActiveClients.Values)
            {
                client.Disconnect();
            }
            ActiveClients.Clear();
            Console.WriteLine("[TCP Server] Stopped");
        }

        private async Task AcceptConnectionsAsync()
        {
            while (_isRunning)
            {
                try
                {
                    TcpClient client = await _listener!.AcceptTcpClientAsync();
                    var handler = new ClientHandler(client, _aesKey, _aesIV, _dbHelper, this);
                    Task.Run(() => handler.HandleClientAsync());
                }
                catch (Exception ex) when (!_isRunning)
                {
                    // Clean shutdown
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[TCP Server] Error accepting connection: {ex.Message}");
                }
            }
        }

        public static void RegisterClient(int userId, ClientHandler handler)
        {
            // Disconnect existing if already logged in from somewhere else
            if (ActiveClients.TryRemove(userId, out var existing))
            {
                existing.SendNotification("Sign-in Conflict", "You have been signed out because your account signed in from another location.");
                existing.Disconnect();
            }
            ActiveClients.TryAdd(userId, handler);
        }

        public static void UnregisterClient(int userId)
        {
            ActiveClients.TryRemove(userId, out _);
        }

        public static ClientHandler? GetClient(int userId)
        {
            ActiveClients.TryGetValue(userId, out var client);
            return client;
        }

        // Broadcast presence change of a user to all of their friends who are online
        public void BroadcastPresence(int userId)
        {
            var user = _dbHelper.GetUserById(userId);
            if (user == null) return;

            // Get contacts who have this user in their list
            // For simplicity: loop through all online clients, check if they have this user in their contact list
            foreach (var kvp in ActiveClients)
            {
                int onlineUserId = kvp.Key;
                var onlineClient = kvp.Value;

                if (onlineUserId == userId) continue;

                // Load online user's contacts
                var contacts = _dbHelper.GetContacts(onlineUserId);
                var selfAsContact = contacts.FirstOrDefault(c => c.Id == userId);
                
                if (selfAsContact != null)
                {
                    // If online user blocked this contact, online user won't get updates or sees them offline
                    // But here, we broadcast the presence of 'userId' (user) to 'onlineUserId'
                    // So we must check: Has 'user' blocked 'onlineUserId'? If yes, 'onlineUserId' sees 'user' as Offline
                    bool isBlockedByTarget = IsBlocked(userId, onlineUserId);
                    
                    string statusToSend = user.Status;
                    if (isBlockedByTarget || user.Status == "Offline")
                    {
                        statusToSend = "Offline";
                    }

                    onlineClient.SendPacket("PresenceChanged", new
                    {
                        UserId = user.Id,
                        DisplayName = user.DisplayName,
                        Status = statusToSend,
                        CustomSign = user.CustomSign,
                        AvatarFilename = user.AvatarFilename
                    });
                }
            }
        }

        private bool IsBlocked(int userId, int contactId)
        {
            // Returns true if userId has blocked contactId
            var contacts = _dbHelper.GetContacts(userId);
            var contact = contacts.FirstOrDefault(c => c.Id == contactId);
            return contact != null && contact.RelationType == "blocked";
        }
    }

    public class ClientHandler
    {
        private readonly TcpClient _client;
        private readonly string _aesKey;
        private readonly string _aesIV;
        private readonly DbHelper _dbHelper;
        private readonly TcpServer _server;
        private NetworkStream? _stream;
        private StreamReader? _reader;
        private StreamWriter? _writer;
        private bool _isAuthenticated;
        public User? CurrentUser { get; private set; }

        public ClientHandler(TcpClient client, string aesKey, string aesIV, DbHelper dbHelper, TcpServer server)
        {
            _client = client;
            _aesKey = aesKey;
            _aesIV = aesIV;
            _dbHelper = dbHelper;
            _server = server;
        }

        public async Task HandleClientAsync()
        {
            try
            {
                _stream = _client.GetStream();
                _reader = new StreamReader(_stream, Encoding.UTF8);
                _writer = new StreamWriter(_stream, Encoding.UTF8) { AutoFlush = true };

                Console.WriteLine("[TCP Server] New client connected");

                while (_client.Connected)
                {
                    string? encryptedLine = await _reader.ReadLineAsync();
                    if (encryptedLine == null) break;

                    string decryptedJson = EncryptionHelper.Decrypt(encryptedLine, _aesKey, _aesIV);
                    if (string.IsNullOrWhiteSpace(decryptedJson))
                    {
                        Console.WriteLine("[TCP Server] Decryption failed or empty packet received.");
                        continue;
                    }

                    ProcessPacket(decryptedJson);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TCP Server] Exception in ClientHandler: {ex.Message}");
            }
            finally
            {
                Cleanup();
            }
        }

        private void ProcessPacket(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("Action", out var actionProp)) return;
                string action = actionProp.GetString() ?? string.Empty;

                // Handle Login action first (if not authenticated)
                if (!_isAuthenticated)
                {
                    if (action == "Login")
                    {
                        HandleLogin(root);
                    }
                    return;
                }

                // Authenticated actions
                switch (action)
                {
                    case "StatusUpdate":
                        HandleStatusUpdate(root);
                        break;
                    case "AddContact":
                        HandleAddContact(root);
                        break;
                    case "RemoveContact":
                        HandleRemoveContact(root);
                        break;
                    case "BlockContact":
                        HandleBlockContact(root);
                        break;
                    case "SendMessage":
                        HandleSendMessage(root);
                        break;
                    case "SendNudge":
                        HandleSendNudge(root);
                        break;
                    case "Ping":
                        SendPacket("Pong", new { });
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TCP Server] Error processing packet: {ex.Message}");
            }
        }

        private void HandleLogin(JsonElement root)
        {
            string email = root.GetProperty("Email").GetString() ?? string.Empty;
            string password = root.GetProperty("Password").GetString() ?? string.Empty;
            string initialStatus = root.TryGetProperty("Status", out var stat) ? stat.GetString() ?? "Online" : "Online";

            var user = _dbHelper.AuthenticateUser(email, password);
            if (user == null)
            {
                SendPacket("LoginResponse", new { Success = false, Error = "Invalid email or password." });
                return;
            }

            _isAuthenticated = true;
            CurrentUser = user;
            
            // Set their active presence
            if (initialStatus == "Offline") initialStatus = "Offline"; // "Appear Offline"
            CurrentUser.Status = initialStatus;
            _dbHelper.UpdateStatus(CurrentUser.Id, CurrentUser.Status);

            TcpServer.RegisterClient(CurrentUser.Id, this);

            // Fetch contact list
            var dbContacts = _dbHelper.GetContacts(CurrentUser.Id);
            
            // We need to fetch contact presence details (Online/Offline statuses).
            // For contacts that are online and haven't blocked CurrentUser, get their current status from active client list.
            // If they are offline or blocked CurrentUser, they appear as Offline.
            var clientContacts = new List<object>();
            foreach (var contact in dbContacts)
            {
                var onlineClient = TcpServer.GetClient(contact.Id);
                string currentStatus = "Offline";

                if (onlineClient != null && onlineClient.CurrentUser != null)
                {
                    // Check if contact has blocked current user
                    bool isBlockedByContact = _dbHelper.GetContacts(contact.Id)
                        .Any(c => c.Id == CurrentUser.Id && c.RelationType == "blocked");

                    if (!isBlockedByContact && onlineClient.CurrentUser.Status != "Offline")
                    {
                        currentStatus = onlineClient.CurrentUser.Status;
                    }
                }

                clientContacts.Add(new
                {
                    Id = contact.Id,
                    Email = contact.Email,
                    DisplayName = contact.DisplayName,
                    Status = currentStatus,
                    CustomSign = contact.CustomSign,
                    AvatarFilename = contact.AvatarFilename,
                    RelationType = contact.RelationType
                });
            }

            SendPacket("LoginResponse", new
            {
                Success = true,
                User = new
                {
                    Id = CurrentUser.Id,
                    Email = CurrentUser.Email,
                    DisplayName = CurrentUser.DisplayName,
                    Status = CurrentUser.Status,
                    CustomSign = CurrentUser.CustomSign,
                    AvatarFilename = CurrentUser.AvatarFilename
                },
                Contacts = clientContacts
            });

            Console.WriteLine($"[TCP Server] User {CurrentUser.Email} signed in successfully with status {CurrentUser.Status}.");

            // Broadcast presence to friends
            _server.BroadcastPresence(CurrentUser.Id);
        }

        private void HandleStatusUpdate(JsonElement root)
        {
            if (CurrentUser == null) return;

            string status = root.GetProperty("Status").GetString() ?? CurrentUser.Status;
            string customSign = root.GetProperty("CustomSign").GetString() ?? CurrentUser.CustomSign;

            CurrentUser.Status = status;
            CurrentUser.CustomSign = customSign;

            _dbHelper.UpdateStatus(CurrentUser.Id, status);
            _dbHelper.UpdateCustomSign(CurrentUser.Id, customSign);

            // Send confirmation back
            SendPacket("StatusUpdateResponse", new
            {
                Success = true,
                Status = CurrentUser.Status,
                CustomSign = CurrentUser.CustomSign
            });

            // Broadcast presence change to friends
            _server.BroadcastPresence(CurrentUser.Id);
        }

        private void HandleAddContact(JsonElement root)
        {
            if (CurrentUser == null) return;
            string email = root.GetProperty("Email").GetString() ?? string.Empty;

            var contact = _dbHelper.AddContact(CurrentUser.Id, email, out string error);
            if (contact == null)
            {
                SendPacket("ContactAdded", new { Success = false, Error = error });
                return;
            }

            // Determine contact status
            string currentStatus = "Offline";
            var onlineClient = TcpServer.GetClient(contact.Id);
            if (onlineClient != null && onlineClient.CurrentUser != null)
            {
                // Check if they blocked us
                bool isBlocked = _dbHelper.GetContacts(contact.Id)
                    .Any(c => c.Id == CurrentUser.Id && c.RelationType == "blocked");
                
                if (!isBlocked && onlineClient.CurrentUser.Status != "Offline")
                {
                    currentStatus = onlineClient.CurrentUser.Status;
                }
            }

            SendPacket("ContactAdded", new
            {
                Success = true,
                Contact = new
                {
                    Id = contact.Id,
                    Email = contact.Email,
                    DisplayName = contact.DisplayName,
                    Status = currentStatus,
                    CustomSign = contact.CustomSign,
                    AvatarFilename = contact.AvatarFilename,
                    RelationType = "friend"
                }
            });

            // Also notify the contact so they can optionally add us or see we are online if they have us added
            // Broadcast presence to update contact's view of us
            _server.BroadcastPresence(CurrentUser.Id);
            _server.BroadcastPresence(contact.Id);
        }

        private void HandleRemoveContact(JsonElement root)
        {
            if (CurrentUser == null) return;
            int contactId = root.GetProperty("ContactId").GetInt32();

            bool success = _dbHelper.RemoveContact(CurrentUser.Id, contactId);
            SendPacket("ContactRemoved", new { Success = success, ContactId = contactId });

            // Update presence for both
            _server.BroadcastPresence(CurrentUser.Id);
        }

        private void HandleBlockContact(JsonElement root)
        {
            if (CurrentUser == null) return;
            int contactId = root.GetProperty("ContactId").GetInt32();
            bool block = root.GetProperty("IsBlocked").GetBoolean();

            bool success = _dbHelper.BlockContact(CurrentUser.Id, contactId, block);
            SendPacket("ContactBlocked", new { Success = success, ContactId = contactId, IsBlocked = block });

            // If we block/unblock, the presence we show to the contact changes.
            // When blocked, they see us as offline. When unblocked, they see our real status.
            _server.BroadcastPresence(CurrentUser.Id);
        }

        private void HandleSendMessage(JsonElement root)
        {
            if (CurrentUser == null) return;
            int toContactId = root.GetProperty("ToContactId").GetInt32();
            string content = root.GetProperty("Content").GetString() ?? string.Empty;

            // Check if contact has blocked us
            bool isBlocked = _dbHelper.GetContacts(toContactId)
                .Any(c => c.Id == CurrentUser.Id && c.RelationType == "blocked");

            // If not blocked, forward the message to the target client if online
            if (!isBlocked)
            {
                var targetClient = TcpServer.GetClient(toContactId);
                if (targetClient != null)
                {
                    targetClient.SendPacket("ReceiveMessage", new
                    {
                        FromContactId = CurrentUser.Id,
                        Content = content,
                        Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                    });
                }
            }
            
            // Note: If blocked, the message is silently discarded, giving the illusion of sending.
        }

        private void HandleSendNudge(JsonElement root)
        {
            if (CurrentUser == null) return;
            int toContactId = root.GetProperty("ToContactId").GetInt32();

            // Check if blocked
            bool isBlocked = _dbHelper.GetContacts(toContactId)
                .Any(c => c.Id == CurrentUser.Id && c.RelationType == "blocked");

            if (!isBlocked)
            {
                var targetClient = TcpServer.GetClient(toContactId);
                if (targetClient != null)
                {
                    targetClient.SendPacket("ReceiveNudge", new
                    {
                        FromContactId = CurrentUser.Id
                    });
                }
            }
        }

        public void SendPacket(string action, object payload)
        {
            try
            {
                string json = JsonSerializer.Serialize(new { Action = action, Payload = payload });
                string encrypted = EncryptionHelper.Encrypt(json, _aesKey, _aesIV);
                if (!string.IsNullOrEmpty(encrypted) && _writer != null)
                {
                    _writer.WriteLine(encrypted);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TCP Server] Error sending packet {action}: {ex.Message}");
            }
        }

        public void SendNotification(string title, string message)
        {
            SendPacket("Notification", new { Title = title, Message = message });
        }

        public void Disconnect()
        {
            try
            {
                _client.Close();
            }
            catch { }
            Cleanup();
        }

        private void Cleanup()
        {
            if (CurrentUser != null)
            {
                Console.WriteLine($"[TCP Server] User {CurrentUser.Email} disconnected.");
                TcpServer.UnregisterClient(CurrentUser.Id);

                // Set status to Offline in DB
                _dbHelper.UpdateStatus(CurrentUser.Id, "Offline");

                // Broadcast offline presence to friends
                _server.BroadcastPresence(CurrentUser.Id);
            }

            _reader?.Dispose();
            _writer?.Dispose();
            _stream?.Dispose();
            _client.Close();
        }
    }
}
