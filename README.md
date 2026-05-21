# Nostalgic Windows Live Messenger Chat Application

A full Windows Live Messenger clone for educational purposes — WPF desktop client, C# server, MySQL database, and a web portal for registration/avatar upload. All traffic is AES-256 encrypted.

---

## 🏗️ Project Structure

```
msn/
├── Client/          WPF Desktop App (.NET 9, Windows)
├── Server/          Console TCP+HTTP Server (.NET 9, cross-platform)
├── Web/             HTML/CSS registration & avatar upload portal
├── Database/        MySQL schema
├── Dockerfile       Docker image for the server
├── docker-compose.yml   Full local stack (server + MySQL)
├── railway.toml     Railway.app cloud deployment config
└── README.md
```

---

## 🚀 Option A — Local (No Docker)

### 1. Database
```sql
-- In MySQL client:
SOURCE e:/gerekliler/msn/Database/schema.sql;
```

### 2. Server
Edit `Server/appsettings.json` → set your MySQL password:
```json
"DefaultConnection": "Server=localhost;Database=wlm_messenger;Uid=root;Pwd=YOUR_PASSWORD;"
```
```powershell
dotnet run --project Server
```

### 3. Client
```powershell
dotnet run --project Client
```
Register at **http://localhost:8080/register**, then sign in.

---

## 🐳 Option B — Local with Docker (Recommended)

Requires: **Docker Desktop** installed and running.

```powershell
# From the msn/ folder — starts MySQL + Server in one command:
docker compose up --build
```

- Web portal: **http://localhost:8080/register**
- TCP messenger port: **1863**
- Client `config.json` stays unchanged (127.0.0.1:1863)

To stop:
```powershell
docker compose down
```

---

## ☁️ Option C — Free Cloud Deploy (Railway.app)

Lets anyone connect to your server from anywhere.

### Step 1 — Push to GitHub
```powershell
cd e:\gerekliler\msn
git init
git add .
git commit -m "WLM Messenger"
# Create a repo on github.com, then:
git remote add origin https://github.com/YOUR_USERNAME/msn.git
git push -u origin main
```

### Step 2 — Create Railway project
1. Go to **https://railway.app** → sign in with GitHub (free)
2. Click **New Project** → **Deploy from GitHub repo** → select your repo
3. Railway will detect the `Dockerfile` automatically

### Step 3 — Add MySQL database
1. In your Railway project, click **+ New** → **Database** → **MySQL**
2. Once created, click the MySQL service → **Variables** tab
3. Copy the `DATABASE_URL` value

### Step 4 — Import the schema
In Railway MySQL dashboard, open the **Query** tab and paste the contents of `Database/schema.sql`, then run it.

### Step 5 — Set environment variables on the Server service
Click your server service → **Variables** tab → add:

| Variable | Value |
|---|---|
| `DATABASE_URL` | *(paste from MySQL service)* |
| `HTTP_PORT` | `8080` |
| `TCP_PORT` | `1863` |
| `AES_KEY` | `WLM_MESSENGER_SECRET_KEY_32BYTES!` |
| `AES_IV` | `WLM_INIT_VECTOR16` |

### Step 6 — Enable TCP Proxy for messenger port
1. Server service → **Settings** → **Networking**
2. Under **TCP Proxy**, click **Add** → Internal Port: `1863`
3. Railway gives you a **public hostname + port** (e.g. `junction.proxy.rlwy.net:12345`)

### Step 7 — Update Client config
Open `Client/config.json` and update:
```json
{
  "ServerIp": "junction.proxy.rlwy.net",
  "ServerTcpPort": 12345,
  "ServerHttpUrl": "https://YOUR-SERVICE.up.railway.app",
  "AesKey": "WLM_MESSENGER_SECRET_KEY_32BYTES!",
  "AesIV": "WLM_INIT_VECTOR16"
}
```

> The `ServerHttpUrl` is the public HTTPS domain Railway assigns (shown in service Settings → Domains).

Rebuild and run the client — everyone with the same `config.json` can now chat with each other!

---

## 💡 Feature Summary

| Feature | Details |
|---|---|
| **Encryption** | AES-256 CBC on all TCP traffic |
| **Passwords** | SHA-256 + random per-user salt |
| **Auto-Login** | Saved to `config.json` |
| **Presence** | Online / Busy / Away / Appear Offline |
| **Auto-Away** | After 150s idle via `GetLastInputInfo` P/Invoke |
| **Blocking** | Blocked users see you Offline; messages silently dropped |
| **Emoticons** | `:)` `:D` `:(` `:P` `:O` `(Y)` `(N)` `(L)` `(K)` `(F)` → emoji |
| **Nudges** | Window shake + beep, 5s cooldown |
| **Toast** | MSN-style slide-up notification bubble |
| **Avatars** | Web upload → Base64 → server saves → client updates live |
