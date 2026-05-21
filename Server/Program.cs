using System;
using System.IO;
using System.Text.Json;

namespace Server
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("     WINDOWS LIVE MESSENGER - SERVER STARTUP     ");
            Console.WriteLine("==================================================");

            string configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            // Fallback for development if appsettings is in source directory and not copied yet
            if (!File.Exists(configPath))
            {
                configPath = "appsettings.json";
            }

            if (!File.Exists(configPath))
            {
                Console.WriteLine($"[Error] Configuration file not found at: {configPath}");
                return;
            }

            ServerConfig? config = null;
            try
            {
                string json = File.ReadAllText(configPath);
                config = JsonSerializer.Deserialize<ServerConfig>(json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] Failed to read configuration: {ex.Message}");
                return;
            }

            if (config == null || config.ServerSettings == null || config.ConnectionStrings == null)
            {
                Console.WriteLine("[Error] Configuration file is invalid.");
                return;
            }

            var settings = config.ServerSettings;
            var connStr = config.ConnectionStrings.DefaultConnection;

            // --- Environment variable overrides (for cloud deployment) ---
            // DATABASE_URL takes priority over appsettings connection string
            string? envDb = Environment.GetEnvironmentVariable("DATABASE_URL");
            if (!string.IsNullOrEmpty(envDb)) connStr = envDb;

            // TCP_PORT override
            string? envTcp = Environment.GetEnvironmentVariable("TCP_PORT");
            if (!string.IsNullOrEmpty(envTcp) && int.TryParse(envTcp, out int tcpPort)) settings.TcpPort = tcpPort;

            // HTTP PORT override (Railway injects PORT for HTTP)
            string? envHttp = Environment.GetEnvironmentVariable("HTTP_PORT");
            if (!string.IsNullOrEmpty(envHttp) && int.TryParse(envHttp, out int httpPort)) settings.HttpPort = httpPort;
            else
            {
                string? envPort = Environment.GetEnvironmentVariable("PORT");
                if (!string.IsNullOrEmpty(envPort) && int.TryParse(envPort, out int port)) settings.HttpPort = port;
            }

            // AES key/IV overrides
            string? envKey = Environment.GetEnvironmentVariable("AES_KEY");
            if (!string.IsNullOrEmpty(envKey)) settings.AesKey = envKey;
            string? envIv = Environment.GetEnvironmentVariable("AES_IV");
            if (!string.IsNullOrEmpty(envIv)) settings.AesIV = envIv;

            Console.WriteLine($"[Config] TCP Port: {settings.TcpPort}");
            Console.WriteLine($"[Config] HTTP Port: {settings.HttpPort}");
            Console.WriteLine($"[Config] Avatar Dir: {settings.AvatarDirectory}");
            Console.WriteLine($"[Config] AES Key: {settings.AesKey[..Math.Min(8, settings.AesKey.Length)]}...");
            Console.WriteLine($"[Config] AES IV:  {settings.AesIV[..Math.Min(8, settings.AesIV.Length)]}...");

            // Initialize DB helper
            var dbHelper = new DbHelper(connStr);
            Console.WriteLine("[Database] Testing connection...");
            if (!dbHelper.TestConnection(out string dbError))
            {
                Console.WriteLine($"[Database] [Warning] Connection failed: {dbError}");
                Console.WriteLine("[Database] Please ensure MySQL is running and database 'wlm_messenger' exists.");
            }
            else
            {
                Console.WriteLine("[Database] Connected successfully!");
            }

            // Resolve avatar directory: if relative, make it absolute from the app base dir
            if (!Path.IsPathRooted(settings.AvatarDirectory))
            {
                settings.AvatarDirectory = Path.GetFullPath(
                    Path.Combine(AppContext.BaseDirectory, settings.AvatarDirectory));
            }

            // Start TCP Server
            var tcpServer = new TcpServer(settings.TcpPort, settings.AesKey, settings.AesIV, dbHelper);
            tcpServer.Start();

            // Start HTTP Server
            var httpServer = new HttpServer(settings.HttpPort, settings.AvatarDirectory, dbHelper, tcpServer);
            httpServer.Start();

            Console.WriteLine("\nServer is running. Type 'exit' to shut down gracefully.");
            Console.WriteLine("==================================================\n");

            while (true)
            {
                string? line = Console.ReadLine();
                if (line?.Trim().ToLower() == "exit")
                {
                    break;
                }
            }

            Console.WriteLine("[Shutdown] Stopping servers...");
            httpServer.Stop();
            tcpServer.Stop();
            Console.WriteLine("[Shutdown] Completed. Goodbye!");
        }
    }

    public class ServerConfig
    {
        public ServerSettings? ServerSettings { get; set; }
        public ConnectionStrings? ConnectionStrings { get; set; }
    }

    public class ServerSettings
    {
        public int TcpPort { get; set; }
        public int HttpPort { get; set; }
        public string AvatarDirectory { get; set; } = string.Empty;
        public string AesKey { get; set; } = string.Empty;
        public string AesIV { get; set; } = string.Empty;
    }

    public class ConnectionStrings
    {
        public string DefaultConnection { get; set; } = string.Empty;
    }
}
