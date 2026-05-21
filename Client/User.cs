using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Client
{
    public class User : INotifyPropertyChanged
    {
        private int _id;
        private string _email = string.Empty;
        private string _displayName = string.Empty;
        private string _status = "Offline";
        private string _customSign = string.Empty;
        private string _avatarFilename = string.Empty;
        private string _relationType = "friend";

        public int Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        public string Email
        {
            get => _email;
            set { _email = value; OnPropertyChanged(); }
        }

        public string DisplayName
        {
            get => _displayName;
            set { _displayName = value; OnPropertyChanged(); }
        }

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public string CustomSign
        {
            get => _customSign;
            set { _customSign = value; OnPropertyChanged(); }
        }

        public string AvatarFilename
        {
            get => _avatarFilename;
            set 
            { 
                _avatarFilename = value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(AvatarUrl)); 
            }
        }

        public string RelationType
        {
            get => _relationType;
            set { _relationType = value; OnPropertyChanged(); }
        }

        public string AvatarUrl
        {
            get
            {
                if (string.IsNullOrEmpty(AvatarFilename))
                {
                    return string.Empty;
                }
                // Avoid cache by appending a random query parameter to reload avatar on change
                return $"{NetworkClient.HttpUrl}/avatars/{AvatarFilename}?t={DateTime.Now.Ticks}";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
