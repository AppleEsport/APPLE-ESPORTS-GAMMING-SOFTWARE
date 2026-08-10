# Apple Esports ERP — Phase 2, 3, 4 Deployment Guide

**Status**: Ready for deployment  
**Version**: 2.0.0  
**Date**: 2026-08-07  

---

## WHAT YOU HAVE NOW

✅ **Complete Sync Engine**
- `SyncOutboxEntry` table stores every transaction locally
- `SyncCourierService` runs in background, sends batches to Head Office every 30s
- `/api/sync/receive` endpoint accepts & merges sync batches
- Branches queue emails instead of sending (Head Office sends them with permanent address)

✅ **Version Tracking & Updates Dashboard**
- Super Admin approves versions once
- Branches see updates and toggle auto-update per branch
- Updates flow down automatically with no manual per-PC reinstalls

✅ **EXE Installer Files Ready**
- `setup.nsi` - NSIS installer script
- `setup-server.bat` - Operator PC Docker setup automation
- `setup-client.bat` - Gaming PC client installation
- `docker-compose.yml` - Already configured and tested
- `.env.example` - Configuration template

---

## PHASE 2: SET UP OWNER'S PRODUCTION SERVER

### Step 1: Owner Provisions Fresh Linux Server

The owner should set up a **NEW** Ubuntu 22.04 LTS server (NOT your Oracle server).

**Requirements:**
- 4GB RAM minimum
- 100GB disk
- Public IP address or domain name
- SSH access

**Owner runs these commands:**

```bash
# Update system
sudo apt update && sudo apt upgrade -y

# Install Docker & Docker Compose
curl -fsSL https://get.docker.com -o get-docker.sh
sudo sh get-docker.sh
sudo usermod -aG docker $USER

# Install git
sudo apt install -y git

# Create app directory
mkdir -p /opt/appleesports
cd /opt/appleesports

# Clone your GitHub repo (owner needs access)
git clone https://github.com/harshal4172005/APPLE-ESPORTS-GAMMING-SOFTWARE-new.git .
```

### Step 2: Configure Owner's Server

**Owner creates `.env` file:**

```bash
cd /opt/appleesports
cp .env.example .env
```

**Owner edits `.env` with THESE CRITICAL settings:**

```env
# DATABASE
DB_USER=appleesports_admin
DB_PASSWORD=VerySecurePassword123!
DB_NAME=appleesports_prod

# API
JWT_SECRET=generate-long-random-string-here
JWT_REFRESH_SECRET=another-long-random-string
ASPNETCORE_ENVIRONMENT=Production

# THIS IS CRITICAL FOR SYNC ENGINE
Deployment:IsHeadOffice=true
Sync:HeadOfficeUrl=http://<OWNER'S-SERVER-IP>:8081
Sync:PollIntervalSeconds=30
Sync:MaxRetryAttempts=5
Sync:BatchSize=100

# EMAIL (Owner configures their Gmail/SMTP)
EmailSettings:Host=smtp.gmail.com
EmailSettings:Port=587
EmailSettings:Username=owner@gmail.com
EmailSettings:Password=app-specific-password
EmailSettings:FromEmail=appleesports@yourcompany.com

# FRONTEND
App:BaseUrl=http://<OWNER'S-SERVER-IP>:8081
FRONTEND_URL=http://<OWNER'S-SERVER-IP>:8081

# REDIS
Redis:ConnectionString=redis:6379
```

Replace `<OWNER'S-SERVER-IP>` with the actual server IP or domain.

### Step 3: Build & Start on Owner's Server

```bash
cd /opt/appleesports

# Build images
docker compose build

# Start services
docker compose up -d

# Run migrations (creates all tables, including SyncOutboxEntry)
docker compose exec -T api dotnet ef database update

# Verify
docker compose ps
docker compose logs api | head -20
```

**Verify working:**
- Dashboard: `http://<OWNER-IP>:8081`
- API: `http://<OWNER-IP>:5016/health`
- Sync inbox ready: `http://<OWNER-IP>:5016/api/sync/receive`

---

## PHASE 3: POINT EXE AT OWNER'S SERVER

**NO REBUILD NEEDED. This is a config change only.**

In `.env.example` (checked into git), set:

```env
App:BaseUrl=http://<OWNER'S-SERVER-IP>:8081
Sync:HeadOfficeUrl=http://<OWNER'S-SERVER-IP>:8081
Deployment:IsHeadOffice=false
```

Commit to GitHub:

```bash
git add .env.example
git commit -m "config: point branches to owner's production server"
git push origin main
```

---

## PHASE 4: INSTALL AT REAL BRANCHES

### Step 4a: Build the EXE

**On your local machine:**

```bash
# Clone latest (includes owner's server config)
git pull origin main

# Build images
docker build -t appleesports:api-v2.0 ./AppleEsportsErp
docker build -t appleesports:client-v2.0 ./client

# Copy files to installer folder
mkdir -p installer
cp setup.nsi installer/
cp setup-server.bat installer/
cp setup-client.bat installer/
cp docker-compose.yml installer/
cp .env.example installer/

# Build NSIS installer
cd installer
"C:\Program Files (x86)\NSIS\makensis.exe" setup.nsi

# Creates: AppleEsports-v2.0.exe
# Copy this to your test folder: C:\Users\harsh\Desktop\exe test\
```

### Step 4b: Test on ONE Branch (Adajan)

**On Adajan's Operator PC:**

1. **Double-click** `AppleEsports-v2.0.exe`
2. **Select**: Server Mode (full installation)
3. **Wait** for Docker to install (may take 5-10 min on first run)
4. **Dashboard opens**: `http://localhost:3000`
5. **Login** as Super Admin (username/password from your seed data)

**Verify Sync Engine:**

- Create a test session
- Create a test bill
- Top up member wallet
- Check `/opt/appleesports/data/` on owner's server—SyncOutboxEntries table should have entries

**Unplug internet on Adajan Operator PC:**
- Continue sessions, billing, everything works
- No errors, no crashes

**Plug internet back in:**
- Wait 30 seconds
- Check owner's server sync inbox—entries should now be synced
- Check version updates—should see available versions from Super Admin

### Step 4c: Full Business Day Test

**Run Adajan for 8+ hours with:**
- 5+ concurrent gaming sessions
- Multiple bills created
- Member wallet top-ups
- Operators logging in/out
- Internet disconnects (5+ times) for 2-3 min each

**Monitor:**
- Owner's server /api/sync/receive getting entries
- No errors in logs
- Gaming PCs auto-reconnect when internet returns
- All financial records match between Adajan and owner's server

### Step 4d: Roll Out to Other 3 Branches

Once Adajan test passes:

```bash
# Copy EXE to each branch's Operator PC
\\adajan-server\AppleEsports-v2.0.exe
\\citylight-server\AppleEsports-v2.0.exe
\\katargam-server\AppleEsports-v2.0.exe
\\varachha-server\AppleEsports-v2.0.exe
```

Each branch runs same setup as Adajan above.

---

## DURING OPERATION

### Syncing Logic

**Branch side:**
```
Session starts → Session saved to local DB
              → outbox entry queued
              
→ Background courier runs every 30s
  → Fetches unsent outbox entries
  → POSTs to Head Office /api/sync/receive
  → Marks entry as synced
  
If internet down:
  → Outbox entries pile up (survive restart)
  → Courier retries up to 5 times
  
When internet returns:
  → Courier sends all queued entries
```

**Head Office side:**
```
POST /api/sync/receive (from branch)
  → Process each entry by EventType
  → Update/merge into master database
  → Log all sync events
  
Email sending:
  → Branch queues "email.send_requested" outbox event
  → Head Office receives & processes
  → Sends from permanent address (owner's server)
```

### Monitor Sync Health

**On owner's server:**

```bash
# Check sync table
docker compose exec -T postgres psql -U postgres -d appleesports_prod -c "
SELECT branch_id, COUNT(*) as pending_entries 
FROM sync_outbox_entries 
WHERE synced_at IS NULL 
GROUP BY branch_id;"

# Check logs
docker compose logs api | grep -i sync
```

### Handle Stuck Entries

If an entry never syncs (max 5 attempts, hours old):

```bash
# Mark as manually synced (last resort)
docker compose exec -T postgres psql -U postgres -d appleesports_prod -c "
UPDATE sync_outbox_entries 
SET synced_at = NOW() 
WHERE id = '<entry-id>';"
```

---

## WHAT YOU'RE CURRENTLY TESTING

You're testing with your local APK (Android app) connected to your Oracle server.

**Next step**: Migrate to owner's server and test EXE installer.

---

## SUMMARY

| Phase | What | Who | When |
|-------|------|-----|------|
| **Phase 2** | Owner sets up production server | Owner | Now |
| **Phase 3** | Update .env.example to point at owner's server | You | After Phase 2 ready |
| **Phase 4a** | Build EXE with owner's server config | You | After Phase 3 |
| **Phase 4b** | Test on Adajan for full business day | Adajan operator | After Phase 4a |
| **Phase 4c** | Roll out to Citylight, Katargam, Varachha | All operators | After Phase 4b passes |

**After Phase 4**: System is live on all 4 branches with:
- Full offline-first operation
- Automatic sync when internet returns
- One-click cascading updates
- Password reset from any device

---

## TROUBLESHOOTING

### Sync not working
- Check `Deployment:IsHeadOffice=true` on owner's server `.env`
- Check `Sync:HeadOfficeUrl` points to owner's server IP (not localhost)
- Check Docker logs: `docker compose logs api | grep -i sync`

### Emails not sending
- Only Head Office sends emails (branches queue them)
- Check owner's SMTP credentials in `.env`
- Check `Deployment:IsHeadOffice=false` on branches

### Gaming PCs can't connect
- Check Operator PC is reachable: `ping <operator-pc-ip>`
- Check firewall allows port 5016: `telnet <operator-pc-ip> 5016`
- Check `.env` has correct `App:BaseUrl` for Operator PC

### Updates not showing
- Super Admin approves version first (in dashboard)
- Check `Deployment:IsHeadOffice=true` on server
- Branches check every 5 min in background

---

## NEXT IMMEDIATE STEPS

1. **Owner sets up server** (Phase 2) - following steps above
2. **Ping me when owner's server is ready** with IP/domain
3. **We update `.env.example`** to point at owner's server (Phase 3)
4. **Build EXE** with new config (Phase 4a)
5. **Test on Adajan** (Phase 4b)
6. **Roll out to other branches** (Phase 4c)

**You're NOT live on real branches yet.** The sync engine is built and tested. Now we move the head office to owner's server and roll out.

