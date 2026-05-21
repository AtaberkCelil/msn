using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Client
{
    public partial class MainWindow : Window
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        public User CurrentUser { get; }
        
        // Full, unfiltered contact list
        private readonly List<User> _allContacts = new();
        
        // Data-bound collections for XAML
        public ObservableCollection<User> OnlineContacts { get; } = new();
        public ObservableCollection<User> OfflineContacts { get; } = new();

        private readonly Dictionary<int, ChatWindow> _openChats = new();
        private readonly DispatcherTimer _idleTimer;
        private bool _isAutoAway;
        private string _preIdleStatus = "Online";

        public MainWindow(User currentUser, JsonElement contactsJson)
        {
            InitializeComponent();
            CurrentUser = currentUser;
            DataContext = this;

            // Load user profile UI
            LblDisplayName.Text = CurrentUser.DisplayName;
            TxtCustomSign.Text = string.IsNullOrEmpty(CurrentUser.CustomSign) ? "Share a personal message..." : CurrentUser.CustomSign;
            UpdateAvatarUI();

            // Populate contacts
            ParseContacts(contactsJson);
            RefreshContactLists();

            // Setup presence selections
            SetupStatusSelector();

            // Listen to packets
            NetworkClient.Instance.PacketReceived += NetworkClient_PacketReceived;
            NetworkClient.Instance.Disconnected += NetworkClient_Disconnected;

            // Setup Idle detection timer (ticks every 5 seconds)
            _idleTimer = new DispatcherTimer();
            _idleTimer.Interval = TimeSpan.FromSeconds(5);
            _idleTimer.Tick += IdleTimer_Tick;
            _idleTimer.Start();
        }

        private void SetupStatusSelector()
        {
            var statuses = new List<LoginWindow.StatusItem>
            {
                new LoginWindow.StatusItem { Name = "Online", Color = System.Windows.Media.Brushes.Green },
                new LoginWindow.StatusItem { Name = "Busy", Color = System.Windows.Media.Brushes.Red },
                new LoginWindow.StatusItem { Name = "Away", Color = System.Windows.Media.Brushes.Orange },
                new LoginWindow.StatusItem { Name = "Appear Offline", Color = System.Windows.Media.Brushes.Gray }
            };

            CmbUserStatus.ItemsSource = statuses;
            
            // Set current selection without triggering status change packet again
            CmbUserStatus.SelectionChanged -= CmbUserStatus_SelectionChanged;
            var currentStatus = CurrentUser.Status;
            if (currentStatus == "Offline") currentStatus = "Appear Offline";
            
            var match = statuses.FirstOrDefault(s => s.Name == currentStatus);
            if (match != null)
            {
                CmbUserStatus.SelectedItem = match;
            }
            CmbUserStatus.SelectionChanged += CmbUserStatus_SelectionChanged;
        }

        private void UpdateAvatarUI()
        {
            if (!string.IsNullOrEmpty(CurrentUser.AvatarUrl))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(CurrentUser.AvatarUrl, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad; // prevents file locking
                    bitmap.EndInit();
                    ImgUserAvatar.Source = bitmap;
                }
                catch
                {
                    ImgUserAvatar.Source = null;
                }
            }
            else
            {
                ImgUserAvatar.Source = null;
            }
        }

        private void ParseContacts(JsonElement contactsJson)
        {
            _allContacts.Clear();
            if (contactsJson.ValueKind != JsonValueKind.Array) return;

            foreach (var item in contactsJson.EnumerateArray())
            {
                var contact = new User
                {
                    Id = item.GetProperty("Id").GetInt32(),
                    Email = item.GetProperty("Email").GetString() ?? string.Empty,
                    DisplayName = item.GetProperty("DisplayName").GetString() ?? string.Empty,
                    Status = item.GetProperty("Status").GetString() ?? "Offline",
                    CustomSign = item.GetProperty("CustomSign").GetString() ?? string.Empty,
                    AvatarFilename = item.GetProperty("AvatarFilename").GetString() ?? string.Empty,
                    RelationType = item.GetProperty("RelationType").GetString() ?? "friend"
                };
                _allContacts.Add(contact);
            }
        }

        private void RefreshContactLists()
        {
            string query = TxtSearch.Text.Trim().ToLower();

            var filtered = _allContacts.Where(c => 
                string.IsNullOrEmpty(query) || 
                c.DisplayName.ToLower().Contains(query) || 
                c.Email.ToLower().Contains(query)
            ).ToList();

            // Split into online and offline
            var online = filtered.Where(c => c.Status != "Offline").OrderBy(c => c.DisplayName).ToList();
            var offline = filtered.Where(c => c.Status == "Offline").OrderBy(c => c.DisplayName).ToList();

            // Update ObservableCollections safely
            UpdateCollection(OnlineContacts, online);
            UpdateCollection(OfflineContacts, offline);

            // Update Expander headers with counts
            ExpOnline.Header = $"Online ({OnlineContacts.Count})";
            ExpOffline.Header = $"Offline ({OfflineContacts.Count})";
        }

        private void UpdateCollection(ObservableCollection<User> target, List<User> source)
        {
            // Simple sync logic to keep animations/selections smooth
            target.Clear();
            foreach (var user in source)
            {
                target.Add(user);
            }
        }

        private void NetworkClient_PacketReceived(string action, JsonElement payload)
        {
            Dispatcher.Invoke(() =>
            {
                switch (action)
                {
                    case "PresenceChanged":
                        HandlePresenceChanged(payload);
                        break;
                    case "ContactAdded":
                        HandleContactAdded(payload);
                        break;
                    case "ContactRemoved":
                        HandleContactRemoved(payload);
                        break;
                    case "ContactBlocked":
                        HandleContactBlocked(payload);
                        break;
                    case "ReceiveMessage":
                        HandleReceiveMessage(payload);
                        break;
                    case "ReceiveNudge":
                        HandleReceiveNudge(payload);
                        break;
                    case "Notification":
                        ShowToast(payload.GetProperty("Title").GetString() ?? "Alert", payload.GetProperty("Message").GetString() ?? "");
                        break;
                }
            });
        }

        private void HandlePresenceChanged(JsonElement payload)
        {
            int userId = payload.GetProperty("UserId").GetInt32();
            string status = payload.GetProperty("Status").GetString() ?? "Offline";
            string displayName = payload.GetProperty("DisplayName").GetString() ?? string.Empty;
            string customSign = payload.GetProperty("CustomSign").GetString() ?? string.Empty;
            string avatarFilename = payload.GetProperty("AvatarFilename").GetString() ?? string.Empty;

            // Is it the user themselves?
            if (userId == CurrentUser.Id)
            {
                CurrentUser.AvatarFilename = avatarFilename;
                CurrentUser.DisplayName = displayName;
                CurrentUser.CustomSign = customSign;
                
                LblDisplayName.Text = displayName;
                TxtCustomSign.Text = string.IsNullOrEmpty(customSign) ? "Share a personal message..." : customSign;
                UpdateAvatarUI();
                return;
            }

            var contact = _allContacts.FirstOrDefault(c => c.Id == userId);
            if (contact != null)
            {
                string oldStatus = contact.Status;
                
                contact.Status = status;
                contact.DisplayName = displayName;
                contact.CustomSign = customSign;
                contact.AvatarFilename = avatarFilename;

                RefreshContactLists();

                // If contact logged in, show MSN Toast!
                if (oldStatus == "Offline" && status != "Offline" && contact.RelationType != "blocked")
                {
                    ShowToast($"{contact.DisplayName} signed in", contact.CustomSign);
                }

                // If chat window is open, update avatar/presence in chat
                if (_openChats.TryGetValue(contact.Id, out var chatWindow))
                {
                    chatWindow.UpdateContactStatus();
                }
            }
        }

        private void HandleContactAdded(JsonElement payload)
        {
            bool success = payload.GetProperty("Success").GetBoolean();
            if (success)
            {
                var contactProp = payload.GetProperty("Contact");
                var contact = new User
                {
                    Id = contactProp.GetProperty("Id").GetInt32(),
                    Email = contactProp.GetProperty("Email").GetString() ?? string.Empty,
                    DisplayName = contactProp.GetProperty("DisplayName").GetString() ?? string.Empty,
                    Status = contactProp.GetProperty("Status").GetString() ?? "Offline",
                    CustomSign = contactProp.GetProperty("CustomSign").GetString() ?? string.Empty,
                    AvatarFilename = contactProp.GetProperty("AvatarFilename").GetString() ?? string.Empty,
                    RelationType = "friend"
                };

                _allContacts.Add(contact);
                RefreshContactLists();
                ShowToast("Contact Added", $"{contact.DisplayName} is now in your list.");
            }
            else
            {
                string error = payload.GetProperty("Error").GetString() ?? "Failed to add contact.";
                MessageBox.Show(error, "Add Contact", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void HandleContactRemoved(JsonElement payload)
        {
            int contactId = payload.GetProperty("ContactId").GetInt32();
            var contact = _allContacts.FirstOrDefault(c => c.Id == contactId);
            if (contact != null)
            {
                _allContacts.Remove(contact);
                RefreshContactLists();
                
                if (_openChats.TryGetValue(contactId, out var chat))
                {
                    chat.Close();
                }
            }
        }

        private void HandleContactBlocked(JsonElement payload)
        {
            int contactId = payload.GetProperty("ContactId").GetInt32();
            bool isBlocked = payload.GetProperty("IsBlocked").GetBoolean();

            var contact = _allContacts.FirstOrDefault(c => c.Id == contactId);
            if (contact != null)
            {
                contact.RelationType = isBlocked ? "blocked" : "friend";
                RefreshContactLists();

                string msg = isBlocked ? $"{contact.DisplayName} blocked." : $"{contact.DisplayName} unblocked.";
                ShowToast("Contact Settings", msg);
            }
        }

        private void HandleReceiveMessage(JsonElement payload)
        {
            int fromContactId = payload.GetProperty("FromContactId").GetInt32();
            string content = payload.GetProperty("Content").GetString() ?? string.Empty;
            string timestamp = payload.GetProperty("Timestamp").GetString() ?? DateTime.Now.ToString("HH:mm:ss");

            var contact = _allContacts.FirstOrDefault(c => c.Id == fromContactId);
            if (contact == null) return;

            var chatWindow = GetOrCreateChatWindow(contact);
            chatWindow.AppendMessage(contact.DisplayName, content, timestamp);
            
            // If window is minimized or not active, flash it or show toast
            if (!chatWindow.IsActive)
            {
                ShowToast($"Message from {contact.DisplayName}", content);
            }
        }

        private void HandleReceiveNudge(JsonElement payload)
        {
            int fromContactId = payload.GetProperty("FromContactId").GetInt32();
            var contact = _allContacts.FirstOrDefault(c => c.Id == fromContactId);
            if (contact == null) return;

            var chatWindow = GetOrCreateChatWindow(contact);
            chatWindow.ReceiveNudge();

            if (!chatWindow.IsActive)
            {
                ShowToast("Nudge received!", $"You received a nudge from {contact.DisplayName}.");
            }
        }

        private ChatWindow GetOrCreateChatWindow(User contact)
        {
            if (!_openChats.TryGetValue(contact.Id, out var chatWindow) || !chatWindow.IsLoaded)
            {
                chatWindow = new ChatWindow(CurrentUser, contact);
                chatWindow.Closed += (s, e) => _openChats.Remove(contact.Id);
                _openChats[contact.Id] = chatWindow;
                chatWindow.Show();
            }
            return chatWindow;
        }

        private void ShowToast(string title, string message)
        {
            // Create sliding notification bubble (MSN-style Toast)
            var toast = new ToastNotification(title, message);
            toast.Show();
        }

        private void NetworkClient_Disconnected()
        {
            Dispatcher.Invoke(() =>
            {
                _idleTimer.Stop();
                MessageBox.Show("Disconnected from messenger server.", "Disconnected", MessageBoxButton.OK, MessageBoxImage.Warning);
                
                // Open login window again
                var login = new LoginWindow();
                login.Show();

                NetworkClient.Instance.PacketReceived -= NetworkClient_PacketReceived;
                NetworkClient.Instance.Disconnected -= NetworkClient_Disconnected;

                this.Close();
            });
        }

        private void CmbUserStatus_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbUserStatus.SelectedItem is LoginWindow.StatusItem statusItem)
            {
                _isAutoAway = false; // Reset auto-away state since user manually changed status
                string status = statusItem.Name == "Appear Offline" ? "Offline" : statusItem.Name;
                CurrentUser.Status = status;

                NetworkClient.Instance.SendPacket("StatusUpdate", new
                {
                    Status = status,
                    CustomSign = CurrentUser.CustomSign
                });
            }
        }

        private void TxtCustomSign_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SaveCustomSign();
                Keyboard.ClearFocus();
            }
        }

        private void TxtCustomSign_LostFocus(object sender, RoutedEventArgs e)
        {
            SaveCustomSign();
        }

        private void SaveCustomSign()
        {
            string sign = TxtCustomSign.Text.Trim();
            if (sign == "Share a personal message...") sign = string.Empty;

            CurrentUser.CustomSign = sign;
            NetworkClient.Instance.SendPacket("StatusUpdate", new
            {
                Status = CurrentUser.Status,
                CustomSign = CurrentUser.CustomSign
            });
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshContactLists();
        }

        private void BtnAddContact_Click(object sender, RoutedEventArgs e)
        {
            string email = TxtAddEmail.Text.Trim();
            if (string.IsNullOrEmpty(email)) return;

            NetworkClient.Instance.SendPacket("AddContact", new { Email = email });
            TxtAddEmail.Text = string.Empty;
        }

        private void ContactItem_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && sender is FrameworkElement element && element.DataContext is User contact)
            {
                GetOrCreateChatWindow(contact);
            }
        }

        // Context Menu Handlers
        private void MenuSendMessage_Click(object sender, RoutedEventArgs e)
        {
            if (GetSelectedContact(sender) is User contact)
            {
                GetOrCreateChatWindow(contact);
            }
        }

        private void MenuBlock_Click(object sender, RoutedEventArgs e)
        {
            if (GetSelectedContact(sender) is User contact)
            {
                NetworkClient.Instance.SendPacket("BlockContact", new { ContactId = contact.Id, IsBlocked = true });
            }
        }

        private void MenuUnblock_Click(object sender, RoutedEventArgs e)
        {
            if (GetSelectedContact(sender) is User contact)
            {
                NetworkClient.Instance.SendPacket("BlockContact", new { ContactId = contact.Id, IsBlocked = false });
            }
        }

        private void MenuRemove_Click(object sender, RoutedEventArgs e)
        {
            if (GetSelectedContact(sender) is User contact)
            {
                var result = MessageBox.Show($"Are you sure you want to remove {contact.DisplayName} from your contact list?", "Remove Contact", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    NetworkClient.Instance.SendPacket("RemoveContact", new { ContactId = contact.Id });
                }
            }
        }

        private User? GetSelectedContact(object sender)
        {
            if (sender is MenuItem menuItem && menuItem.Parent is ContextMenu contextMenu)
            {
                if (contextMenu.PlacementTarget is FrameworkElement element)
                {
                    return element.DataContext as User;
                }
            }
            return null;
        }

        private void ContextMenu_Opening(object sender, ContextMenuEventArgs e)
        {
            // Context menu is opening. Update menu items: Block vs Unblock visibility.
        }

        private void Avatar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(ConfigManager.Current.ServerHttpUrl + "/upload") { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
            }
        }

        // Auto-Away Idle Detection
        private void IdleTimer_Tick(object? sender, EventArgs e)
        {
            LASTINPUTINFO lii = new LASTINPUTINFO();
            lii.cbSize = (uint)Marshal.SizeOf(lii);
            
            if (GetLastInputInfo(ref lii))
            {
                uint idleMs = (uint)Environment.TickCount - lii.dwTime;

                // 150 seconds = 150000 ms
                if (idleMs >= 150000 && CurrentUser.Status == "Online" && !_isAutoAway)
                {
                    _preIdleStatus = CurrentUser.Status;
                    _isAutoAway = true;
                    
                    // Update dropdown UI quietly
                    SetStatusComboSilent("Away");
                    NetworkClient.Instance.SendPacket("StatusUpdate", new { Status = "Away", CustomSign = CurrentUser.CustomSign });
                    CurrentUser.Status = "Away";
                    Console.WriteLine("[Idle Timer] User went idle. Status set to Away automatically.");
                }
                // If user returns (idle time drops back down)
                else if (idleMs < 10000 && _isAutoAway)
                {
                    _isAutoAway = false;
                    SetStatusComboSilent(_preIdleStatus);
                    NetworkClient.Instance.SendPacket("StatusUpdate", new { Status = _preIdleStatus, CustomSign = CurrentUser.CustomSign });
                    CurrentUser.Status = _preIdleStatus;
                    Console.WriteLine("[Idle Timer] User active again. Status restored.");
                }
            }
        }

        private void SetStatusComboSilent(string statusName)
        {
            CmbUserStatus.SelectionChanged -= CmbUserStatus_SelectionChanged;
            if (statusName == "Offline") statusName = "Appear Offline";
            var item = CmbUserStatus.Items.Cast<LoginWindow.StatusItem>().FirstOrDefault(s => s.Name == statusName);
            if (item != null)
            {
                CmbUserStatus.SelectedItem = item;
            }
            CmbUserStatus.SelectionChanged += CmbUserStatus_SelectionChanged;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _idleTimer.Stop();
            NetworkClient.Instance.PacketReceived -= NetworkClient_PacketReceived;
            NetworkClient.Instance.Disconnected -= NetworkClient_Disconnected;
            NetworkClient.Instance.Disconnect();
        }
    }
}