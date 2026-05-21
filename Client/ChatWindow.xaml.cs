using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace Client
{
    public class ChatMessage
    {
        public string SenderName { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public bool IsSystem { get; set; }
    }

    public partial class ChatWindow : Window
    {
        private readonly User _currentUser;
        private readonly User _contact;
        private readonly ObservableCollection<ChatMessage> _messages = new();
        private DateTime _lastNudgeTime = DateTime.MinValue;

        public ChatWindow(User currentUser, User contact)
        {
            InitializeComponent();
            _currentUser = currentUser;
            _contact = contact;

            LstMessages.ItemsSource = _messages;
            UpdateContactStatus();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            this.Title = $"Conversation with {_contact.DisplayName}";
            TxtInput.Focus();
        }

        public void UpdateContactStatus()
        {
            LblContactName.Text = _contact.DisplayName;
            LblContactStatus.Text = $" ({_contact.Status})";
            LblContactSign.Text = string.IsNullOrEmpty(_contact.CustomSign) ? "No personal message." : _contact.CustomSign;

            // Load Contact Avatar
            if (!string.IsNullOrEmpty(_contact.AvatarUrl))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(_contact.AvatarUrl, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    ImgContactAvatar.Source = bitmap;
                }
                catch
                {
                    ImgContactAvatar.Source = null;
                }
            }
            else
            {
                ImgContactAvatar.Source = null;
            }
        }

        private void BtnSend_Click(object sender, RoutedEventArgs e)
        {
            SendMessage();
        }

        private void TxtInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                e.Handled = true;
                SendMessage();
            }
        }

        private void SendMessage()
        {
            string rawContent = TxtInput.Text.Trim();
            if (string.IsNullOrEmpty(rawContent)) return;

            string content = ResolveEmojis(rawContent);

            // Send packet to server
            NetworkClient.Instance.SendPacket("SendMessage", new
            {
                ToContactId = _contact.Id,
                Content = content
            });

            // Append locally
            AppendMessage("You", content, DateTime.Now.ToString("HH:mm:ss"));

            TxtInput.Text = string.Empty;
            TxtInput.Focus();
        }

        public void AppendMessage(string senderName, string content, string timestamp, bool isSystem = false)
        {
            _messages.Add(new ChatMessage
            {
                SenderName = senderName,
                Timestamp = timestamp,
                Content = content,
                IsSystem = isSystem
            });
            ScrollToBottom();
        }

        private void ScrollToBottom()
        {
            // Scroll ScrollViewer to end on next layout cycle
            Scroller.Dispatcher.BeginInvoke(new Action(() =>
            {
                Scroller.ScrollToEnd();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void BtnNudge_Click(object sender, RoutedEventArgs e)
        {
            // Cooldown: 5 seconds
            if ((DateTime.Now - _lastNudgeTime).TotalSeconds < 5)
            {
                AppendMessage(string.Empty, "* You cannot send nudges that frequently. *", string.Empty, isSystem: true);
                return;
            }

            _lastNudgeTime = DateTime.Now;

            // Send to server
            NetworkClient.Instance.SendPacket("SendNudge", new
            {
                ToContactId = _contact.Id
            });

            AppendMessage(string.Empty, "* You have just sent a nudge. *", string.Empty, isSystem: true);
            _ = ShakeWindow();
        }

        public void ReceiveNudge()
        {
            AppendMessage(string.Empty, $"* {_contact.DisplayName} has just sent you a nudge. *", string.Empty, isSystem: true);
            _ = ShakeWindow();
        }

        private async Task ShakeWindow()
        {
            double originalLeft = this.Left;
            double originalTop = this.Top;

            try
            {
                System.Media.SystemSounds.Beep.Play();
            }
            catch { }

            var rand = new Random();
            for (int i = 0; i < 15; i++)
            {
                this.Left = originalLeft + rand.Next(-6, 7);
                this.Top = originalTop + rand.Next(-6, 7);
                await Task.Delay(25);
            }

            this.Left = originalLeft;
            this.Top = originalTop;
        }

        private void BtnEmoticon_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();
            var emoticons = new Dictionary<string, string>
            {
                { "😊 Smile (:)", ":)" },
                { "😃 Big Grin (:D)", ":D" },
                { "😢 Sad (:()", ":(" },
                { "😜 Wink (:P)", ":P" },
                { "😮 Surprised (:O)", ":O" },
                { "👍 Thumbs Up (Y)", "(Y)" },
                { "👎 Thumbs Down (N)", "(N)" },
                { "❤️ Heart (L)", "(L)" },
                { "💋 Kiss (K)", "(K)" },
                { "🌹 Flower (F)", "(F)" }
            };

            foreach (var kvp in emoticons)
            {
                var item = new MenuItem { Header = kvp.Key };
                item.Click += (s, ev) =>
                {
                    TxtInput.Focus();
                    // Insert at cursor
                    int caretIndex = TxtInput.CaretIndex;
                    TxtInput.Text = TxtInput.Text.Insert(caretIndex, kvp.Value);
                    TxtInput.CaretIndex = caretIndex + kvp.Value.Length;
                };
                menu.Items.Add(item);
            }

            menu.PlacementTarget = BtnEmoticon;
            menu.IsOpen = true;
        }

        private string ResolveEmojis(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return text
                .Replace(":-)", "😊").Replace(":)", "😊")
                .Replace(":-D", "😃").Replace(":D", "😃")
                .Replace(":-(", "😢").Replace(":(", "😢")
                .Replace(":-P", "😜").Replace(":P", "😜")
                .Replace(":-O", "😮").Replace(":O", "😮")
                .Replace("(Y)", "👍").Replace("(y)", "👍")
                .Replace("(N)", "👎").Replace("(n)", "👎")
                .Replace("(L)", "❤️").Replace("(l)", "❤️")
                .Replace("(K)", "💋").Replace("(k)", "💋")
                .Replace("(F)", "🌹").Replace("(f)", "🌹");
        }
    }
}
