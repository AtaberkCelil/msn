using System;
using System.IO;
using System.Text.Json;

namespace Client
{
    public class ClientConfig
    {
        public string ServerIp { get; set; } = "127.0.0.1";
        public int ServerTcpPort { get; set; } = 1863;
        public string ServerHttpUrl { get; set; } = "http://localhost:8080";
        public string AesKey { get; set; } = "WLM_MESSENGER_SECRET_KEY_32BYTES!";
        public string AesIV { get; set; } = "WLM_INIT_VECTOR16";
        public string RememberEmail { get; set; } = string.Empty;
        public string SavedPassword { get; set; } = string.Empty;
        public bool RememberMe { get; set; }
        public bool RememberPassword { get; set; }
        public bool AutoLogin { get; set; }
    }

    public static class ConfigManager
    {
        private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        public static ClientConfig Current { get; private set; } = new();

        public static void Load()
        {
            try
            {
                string path = File.Exists(ConfigPath) ? ConfigPath : "config.json";
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    Current = JsonSerializer.Deserialize<ClientConfig>(json) ?? new ClientConfig();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ConfigManager] Load error: {ex.Message}");
            }
        }

        public static void Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ConfigManager] Save error: {ex.Message}");
            }
        }
    }
}
