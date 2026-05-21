using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Navigation;

namespace Client
{
    public partial class LoginWindow : Window
    {
        public class StatusItem
        {
            public string Name { get; set; } = string.Empty;
            public Brush Color { get; set; } = Brushes.Gray;
        }

        private bool _isSigningIn;

        public LoginWindow()
        {
            InitializeComponent();
            ConfigManager.Load();
            SetupStatusComboBox();
        }

        private void SetupStatusComboBox()
        {
            var statuses = new List<StatusItem>
            {
                new StatusItem { Name = "Online", Color = new SolidColorBrush(Color.FromRgb(34, 197, 94)) }, // Green
                new StatusItem { Name = "Busy", Color = new SolidColorBrush(Color.FromRgb(239, 68, 68)) },  // Red
                new StatusItem { Name = "Away", Color = new SolidColorBrush(Color.FromRgb(249, 115, 22)) },  // Orange
                new StatusItem { Name = "Appear Offline", Color = new SolidColorBrush(Color.FromRgb(156, 163, 175)) } // Gray
            };
            CmbStatus.ItemsSource = statuses;
            CmbStatus.SelectedIndex = 0;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Populate from config
            var config = ConfigManager.Current;
            TxtEmail.Text = config.RememberEmail;
            
            if (config.RememberMe)
            {
                ChkRememberMe.IsChecked = true;
            }

            if (config.RememberPassword)
            {
                ChkRememberPassword.IsChecked = true;
                TxtPassword.Password = config.SavedPassword;
            }

            if (config.AutoLogin)
            {
                ChkAutoLogin.IsChecked = true;
                // Trigger auto sign in if email and password are provided
                if (!string.IsNullOrEmpty(TxtEmail.Text) && !string.IsNullOrEmpty(TxtPassword.Password))
                {
                    StartSignInProcess();
                }
            }
        }

        private void ChkRememberPassword_Checked(object sender, RoutedEventArgs e)
        {
            ChkRememberMe.IsChecked = true; // Must remember email if remembering password
        }

        private void ChkRememberPassword_Unchecked(object sender, RoutedEventArgs e)
        {
            if (ChkAutoLogin != null)
            {
                ChkAutoLogin.IsChecked = false;
            }
        }

        private void ChkAutoLogin_Checked(object sender, RoutedEventArgs e)
        {
            ChkRememberMe.IsChecked = true;
            ChkRememberPassword.IsChecked = true;
        }

        private void BtnSignIn_Click(object sender, RoutedEventArgs e)
        {
            StartSignInProcess();
        }

        private async void StartSignInProcess()
        {
            if (_isSigningIn) return;

            string email = TxtEmail.Text.Trim();
            string password = TxtPassword.Password;

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                ShowError("Please enter your email and password.");
                return;
            }

            ToggleInputs(false);
            ShowError(string.Empty);
            StartLogoAnimation();

            var config = ConfigManager.Current;
            NetworkClient.HttpUrl = config.ServerHttpUrl;
            NetworkClient.AesKey = config.AesKey;
            NetworkClient.AesIV = config.AesIV;

            // Connect TCP
            bool connected = await NetworkClient.Instance.ConnectAsync(config.ServerIp, config.ServerTcpPort);
            if (!connected)
            {
                StopLogoAnimation();
                ToggleInputs(true);
                ShowError("Could not connect to the messenger server. Please make sure the server is online.");
                return;
            }

            // Subscribe to packets
            NetworkClient.Instance.PacketReceived += Instance_PacketReceived;
            NetworkClient.Instance.Disconnected += Instance_Disconnected;

            // Send Login Request
            var statusItem = (StatusItem)CmbStatus.SelectedItem;
            string selectedStatus = statusItem.Name == "Appear Offline" ? "Offline" : statusItem.Name;

            NetworkClient.Instance.SendPacket("Login", new
            {
                Email = email,
                Password = password,
                Status = selectedStatus
            });
        }

        private void Instance_PacketReceived(string action, JsonElement payload)
        {
            // Direct to Dispatcher because this comes from the TCP reader background thread
            Dispatcher.Invoke(() =>
            {
                if (action == "LoginResponse")
                {
                    // Clean up event subscription so it doesn't leak or double-trigger
                    NetworkClient.Instance.PacketReceived -= Instance_PacketReceived;
                    NetworkClient.Instance.Disconnected -= Instance_Disconnected;

                    bool success = payload.GetProperty("Success").GetBoolean();
                    if (success)
                    {
                        var userProp = payload.GetProperty("User");
                        var contactsProp = payload.GetProperty("Contacts");

                        // Save local config
                        var config = ConfigManager.Current;
                        config.RememberEmail = ChkRememberMe.IsChecked == true ? TxtEmail.Text.Trim() : string.Empty;
                        config.RememberMe = ChkRememberMe.IsChecked == true;
                        config.RememberPassword = ChkRememberPassword.IsChecked == true;
                        config.SavedPassword = ChkRememberPassword.IsChecked == true ? TxtPassword.Password : string.Empty;
                        config.AutoLogin = ChkAutoLogin.IsChecked == true;
                        ConfigManager.Save();

                        // Parse current user
                        var currentUser = new User
                        {
                            Id = userProp.GetProperty("Id").GetInt32(),
                            Email = userProp.GetProperty("Email").GetString() ?? string.Empty,
                            DisplayName = userProp.GetProperty("DisplayName").GetString() ?? string.Empty,
                            Status = userProp.GetProperty("Status").GetString() ?? "Online",
                            CustomSign = userProp.GetProperty("CustomSign").GetString() ?? string.Empty,
                            AvatarFilename = userProp.GetProperty("AvatarFilename").GetString() ?? string.Empty
                        };

                        // Open main buddy list window
                        var mainWindow = new MainWindow(currentUser, contactsProp);
                        mainWindow.Show();

                        StopLogoAnimation();
                        this.Close();
                    }
                    else
                    {
                        string error = payload.GetProperty("Error").GetString() ?? "Login failed.";
                        StopLogoAnimation();
                        ToggleInputs(true);
                        ShowError(error);
                        NetworkClient.Instance.Disconnect();
                    }
                }
            });
        }

        private void Instance_Disconnected()
        {
            Dispatcher.Invoke(() =>
            {
                NetworkClient.Instance.PacketReceived -= Instance_PacketReceived;
                NetworkClient.Instance.Disconnected -= Instance_Disconnected;
                StopLogoAnimation();
                ToggleInputs(true);
                ShowError("Disconnected from server.");
            });
        }

        private void StartLogoAnimation()
        {
            _isSigningIn = true;
            DoubleAnimation rotateAnim = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(2.0)))
            {
                RepeatBehavior = RepeatBehavior.Forever
            };
            LogoRotation.BeginAnimation(RotateTransform.AngleProperty, rotateAnim);
        }

        private void StopLogoAnimation()
        {
            _isSigningIn = false;
            LogoRotation.BeginAnimation(RotateTransform.AngleProperty, null);
        }

        private void ToggleInputs(bool enabled)
        {
            TxtEmail.IsEnabled = enabled;
            TxtPassword.IsEnabled = enabled;
            CmbStatus.IsEnabled = enabled;
            ChkRememberMe.IsEnabled = enabled;
            ChkRememberPassword.IsEnabled = enabled;
            ChkAutoLogin.IsEnabled = enabled;
            BtnSignIn.IsEnabled = enabled;
        }

        private void ShowError(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                LblError.Visibility = Visibility.Collapsed;
            }
            else
            {
                LblError.Text = message;
                LblError.Visibility = Visibility.Visible;
            }
        }

        private void Hyperlink_Register_Click(object sender, RoutedEventArgs e)
        {
            OpenBrowser(ConfigManager.Current.ServerHttpUrl + "/register");
        }

        private void Hyperlink_Upload_Click(object sender, RoutedEventArgs e)
        {
            OpenBrowser(ConfigManager.Current.ServerHttpUrl + "/upload");
        }

        private void OpenBrowser(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open page: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
