# Apple Esports ERP — EXE Build & Production Server Migration Guide

> **Status**: Ready to implement  
> **Date**: 2026-08-07  
> **Version**: 1.0  
> **Author**: Claude Code

---

## TABLE OF CONTENTS

1. [Current Architecture & What We Have](#1-current-architecture--what-we-have)
2. [EXE Build Strategy](#2-exe-build-strategy)
3. [Step-by-Step EXE Creation](#3-step-by-step-exe-creation)
4. [Production Server Migration](#4-production-server-migration)
5. [Testing Plan](#5-testing-plan)
6. [Deployment Checklist](#6-deployment-checklist)

---

## 1. CURRENT ARCHITECTURE & WHAT WE HAVE

### Stack Overview

| Layer | Technology | Status |
|-------|-----------|--------|
| **Frontend** | React 19 + Vite | ✅ Built & Running |
| **Backend** | .NET 8 ASP.NET Core | ✅ Built & Running |
| **Database** | PostgreSQL 16 | ✅ Running on Oracle Cloud |
| **Deployment** | Docker Compose | ✅ Containerized |
| **Hosting** | Oracle Cloud (Temporary) | ✅ Running |
| **Real-time** | SignalR (LAN + Cloud) | ✅ Working |

### Current Deployment

```
Your Local Machine (Dev)
    ↓
GitHub Repository (Source Control)
    ↓
Oracle Cloud Server (140.245.195.222:8081)
    ├── PostgreSQL Database
    ├── .NET API (Port 5016)
    └── React Frontend (Port 3000)
```

**Problem**: Owner doesn't have direct access. Everything flows through your machine.

---

## 2. EXE BUILD STRATEGY

### Option A: Docker-Based EXE (Recommended)

**How it works:**
- Single EXE installer that includes Docker Desktop embedded
- On first run, sets up Docker containers automatically
- Operator PC runs Server Mode (all containers)
- Gaming PC runs Client Mode (just the client UI)
- No technical knowledge needed from end users

**Pros:**
- Exact same environment as production (what runs locally runs on server)
- Easy updates (just redeploy Docker images)
- Clean uninstall (remove containers and volumes)
- Works on any Windows 10+

**Cons:**
- Larger file size (~500MB for Docker + images)
- Slight startup delay while containers boot

### Option B: Self-Contained .NET EXE (Alternative)

**How it works:**
- Publish .NET as self-contained executable
- Embed PostgreSQL as portable database
- React frontend bundled in Electron wrapper

**Pros:**
- Smaller file size (~300MB)
- Faster startup

**Cons:**
- More complex setup
- Database initialization more error-prone
- Updates harder to distribute

### Recommendation: **Option A (Docker-Based)**

You already have Docker Compose set up. Leveraging it means zero extra complexity.

---

## 3. STEP-BY-STEP EXE CREATION

### Phase 1: Prepare Installers (NSIS Approach)

**Tools needed:**
- NSIS (Nullsoft Scriptable Install System) — Free, lightweight
- DockerDesktopInstaller for bundling (optional, or user downloads separately)

**Step 1: Create NSIS Installer Script**

File: `setup.nsi`

```nsis
!include "MUI2.nsh"

; Basic installer config
Name "Apple Esports ERP v2.0"
OutFile "AppleEsports.exe"
InstallDir "$PROGRAMFILES\AppleEsports"
InstallDirRegKey HKCU "Software\AppleEsports" "InstallDir"

; Pages
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_LANGUAGE "English"

; Installer sections
Section "Docker & Database (Server Mode)"
  ; Copy docker-compose.yml, .env files
  SetOutPath "$INSTDIR"
  File "docker-compose.yml"
  File ".env.example"
  File "setup-server.bat"
  
  ; Run setup script
  ExecWait "$INSTDIR\setup-server.bat"
SectionEnd

Section "Client App (Gaming PC Mode)"
  SetOutPath "$INSTDIR\client"
  File /r "client\dist\*.*"
  File "setup-client.bat"
  
  ; Create desktop shortcut
  CreateDirectory "$SMPROGRAMS\Apple Esports"
  CreateShortcut "$SMPROGRAMS\Apple Esports\Client.lnk" "$INSTDIR\client\AppleEsports-Client.exe"
  CreateShortcut "$DESKTOP\Apple Esports.lnk" "$INSTDIR\client\AppleEsports-Client.exe"
SectionEnd

; Uninstaller
Section "Uninstall"
  RMDir /r "$INSTDIR"
  Delete "$SMPROGRAMS\Apple Esports\*"
  Delete "$DESKTOP\Apple Esports.lnk"
SectionEnd
```

**Step 2: Create Setup Batch Files**

File: `setup-server.bat` (Server Mode setup)

```batch
@echo off
echo Installing Apple Esports ERP (Server Mode)...

REM Copy environment file
copy .env.example .env

REM Create necessary directories
mkdir data
mkdir backups

REM Start Docker Compose
echo Starting Docker containers...
docker compose up -d

REM Wait for services to be ready
echo Waiting for services to start...
timeout /t 30

REM Run database migrations
echo Running database migrations...
docker compose exec -T api dotnet ef database update

echo.
echo ✅ Installation complete!
echo ✅ API running at: http://localhost:5016
echo ✅ Dashboard at: http://localhost:3000
echo ✅ Database: PostgreSQL on localhost:5433
echo.
pause
```

File: `setup-client.bat` (Client Mode setup)

```batch
@echo off
echo Installing Apple Esports Client...

REM Create app directory
mkdir %APPDATA%\AppleEsports

REM Copy client config
copy client-config.json %APPDATA%\AppleEsports\

REM Create startup shortcut
setlocal enabledelayedexpansion
set "startupDir=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup"
echo Creating @echo off > "%startupDir%\AppleEsportsClient.bat"
echo cd /d "%CD%" >> "%startupDir%\AppleEsportsClient.bat"
echo npm run preview >> "%startupDir%\AppleEsportsClient.bat"

echo ✅ Client installed successfully!
echo Please enter the Server (Operator PC) IP address on first launch.
pause
```

**Step 3: Build Docker Images**

```bash
cd c:\Users\harsh\Desktop\FINAL APPLE ESPORTS GAMMING SOFTWARE

# Build API image
docker build -t appleesports:api-v2.0 ./AppleEsportsErp

# Build Client image
docker build -t appleesports:client-v2.0 ./client

# Tag for registry (optional, for owner's server)
docker tag appleesports:api-v2.0 your-registry.azurecr.io/appleesports:api-v2.0
docker tag appleesports:client-v2.0 your-registry.azurecr.io/appleesports:client-v2.0
```

**Step 4: Package EXE**

```bash
# Download NSIS
# Install NSIS from https://nsis.sourceforge.io/

# Create installer
"C:\Program Files (x86)\NSIS\makensis.exe" setup.nsi

# Output: AppleEsports.exe (ready to distribute)
```

---

## 4. PRODUCTION SERVER MIGRATION

### Current Problem

```
Your Local Machine
    ↓
Your GitHub Account
    ↓
Your Oracle Cloud Server (temporary)
    ↓
❌ Owner can't access directly
```

### Solution: Move To Owner's Server

```
Owner's Production Server
    ├── Fresh PostgreSQL
    ├── .NET API
    ├── React Dashboard
    └── Real database (not your Oracle instance)
```

### Migration Steps

#### Step 1: Prepare Owner's Server

Owner should set up a Linux server (Ubuntu 22.04+) with:

```bash
# Install Docker & Docker Compose
curl -fsSL https://get.docker.com -o get-docker.sh
sh get-docker.sh

# Add user to docker group
sudo usermod -aG docker $USER

# Install git
sudo apt update && sudo apt install -y git

# Create app directory
mkdir -p /opt/appleesports
cd /opt/appleesports
```

#### Step 2: Clone Repository On Owner's Server

```bash
cd /opt/appleesports

# Clone from GitHub (owner forks or you give access)
git clone https://github.com/harshal4172005/APPLE-ESPORTS-GAMMING-SOFTWARE-new.git .

# Or if owner owns the repo:
git clone https://github.com/owner/apple-esports.git .
```

#### Step 3: Set Up Environment Variables

File: `/opt/appleesports/.env`

```env
# Database
DB_USER=appleesports_user
DB_PASSWORD=SuperSecurePassword123!
DB_NAME=appleesports_prod

# API
JWT_SECRET=your-jwt-secret-key-here
JWT_REFRESH_SECRET=your-refresh-secret-key-here
ASPNETCORE_ENVIRONMENT=Production

# Email (for alerts)
SMTP_HOST=smtp.gmail.com
SMTP_PORT=587
SMTP_USER=alerts@appleesports.in
SMTP_PASSWORD=your-app-password

# Redis
REDIS_PASSWORD=RedisPassword123!

# Frontend
VITE_API_BASE_URL=https://api.appleesports.in
```

#### Step 4: Deploy On Owner's Server

```bash
cd /opt/appleesports

# Build images
docker compose build

# Start all services
docker compose up -d

# Run migrations
docker compose exec -T api dotnet ef database update

# Verify
docker compose ps
docker compose logs api | head -20
```

#### Step 5: Set Up Reverse Proxy (Nginx)

```bash
# Install Nginx
sudo apt install -y nginx

# Create config
sudo tee /etc/nginx/sites-available/appleesports > /dev/null <<EOF
server {
    listen 80;
    server_name appleesports.yourdomain.com;
    
    # Redirect to HTTPS
    return 301 https://\$server_name\$request_uri;
}

server {
    listen 443 ssl http2;
    server_name appleesports.yourdomain.com;
    
    ssl_certificate /path/to/cert.pem;
    ssl_certificate_key /path/to/key.pem;
    
    # API proxy
    location /api/ {
        proxy_pass http://localhost:5016;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
    }
    
    # Frontend
    location / {
        proxy_pass http://localhost:3000;
        proxy_set_header Host \$host;
    }
}
EOF

# Enable site
sudo ln -s /etc/nginx/sites-available/appleesports /etc/nginx/sites-enabled/
sudo nginx -t
sudo systemctl restart nginx
```

#### Step 6: SSL Certificate (Let's Encrypt)

```bash
sudo apt install -y certbot python3-certbot-nginx

sudo certbot certonly --nginx -d appleesports.yourdomain.com

# Auto-renewal
sudo systemctl enable certbot.timer
sudo systemctl start certbot.timer
```

#### Step 7: Set Up Database Backups

```bash
# Create backup directory
mkdir -p /opt/appleesports/backups

# Daily backup script
sudo tee /etc/cron.daily/appleesports-backup > /dev/null <<EOF
#!/bin/bash
cd /opt/appleesports
docker compose exec -T postgres pg_dump -U \$DB_USER \$DB_NAME | gzip > backups/backup-\$(date +%Y%m%d).sql.gz
find backups -mtime +30 -delete  # Keep 30 days
EOF

sudo chmod +x /etc/cron.daily/appleesports-backup
```

---

## 5. TESTING PLAN

### Local Testing (Before EXE Release)

- [ ] **Server Mode Test**: Install `AppleEsports.exe` in Server Mode on a test machine
  - [ ] Docker starts automatically
  - [ ] API is accessible at http://localhost:5016
  - [ ] Database is created
  - [ ] Operator dashboard loads
  
- [ ] **Client Mode Test**: Install on 2-3 gaming PC simulators
  - [ ] Client starts without database
  - [ ] Can connect to Server PC IP
  - [ ] Session requests work over LAN
  - [ ] Real-time updates work (SignalR)

### Production Testing (On Owner's Server)

- [ ] **Database**: All data migrated from Oracle Cloud
- [ ] **API**: All endpoints working (test with Postman)
- [ ] **Dashboard**: All features working (Sessions, Billing, EOD, etc.)
- [ ] **Authentication**: JWT tokens work with new server
- [ ] **Offline Capability**: Disable internet, verify LAN still works
- [ ] **Backups**: Automated backups run daily
- [ ] **SSL**: HTTPS certificate working
- [ ] **Performance**: Load test with 20+ concurrent users

---

## 6. DEPLOYMENT CHECKLIST

### Before Launch

- [ ] Owner has production server ready (Ubuntu 22.04+)
- [ ] Domain registered and DNS pointing to server
- [ ] SSL certificate obtained (Let's Encrypt)
- [ ] Database backups configured
- [ ] Email alerts configured
- [ ] All branches tested
- [ ] All staff trained

### Launch Day

- [ ] Freeze production server
- [ ] Migrate data from Oracle Cloud to Owner's server
- [ ] Run full database consistency check
- [ ] Verify all PCs can connect to new server
- [ ] Test a full session (start → billing → EOD)
- [ ] Announce to all branch operators

### Post-Launch

- [ ] Monitor server logs for errors
- [ ] Check backups are running
- [ ] Get feedback from operators
- [ ] Plan next update release

---

## 7. UPDATE & MAINTENANCE

### Monthly Updates

```bash
cd /opt/appleesports

# Pull latest code
git pull origin main

# Rebuild images
docker compose build

# Stop old containers
docker compose down

# Start new containers
docker compose up -d

# Run any new migrations
docker compose exec -T api dotnet ef database update
```

### Version Management

Tag releases in git:

```bash
git tag -a v2.0.0 -m "Release v2.0.0 - Production Launch"
git push origin v2.0.0
```

Build EXE for each version:

```bash
# Build Docker images with version tag
docker tag appleesports:api-v2.0 appleesports:api-v2.0.0
docker tag appleesports:client-v2.0 appleesports:client-v2.0.0

# Create versioned EXE
nsis setup.nsi  # Will create AppleEsports-v2.0.0.exe
```

---

## SUMMARY

### What Needs to Happen

1. **EXE Creation**: Package current Docker setup into NSIS installer ✅ Ready
2. **Database Migration**: Export data from Oracle Cloud → Owner's PostgreSQL
3. **Server Setup**: Owner sets up Linux server with Docker Compose
4. **Domain & SSL**: Owner registers domain and SSL certificate
5. **Testing**: Full end-to-end testing on production server
6. **Training**: Brief operators on new server access

### Timeline

- **Week 1**: Build EXE, test locally
- **Week 2**: Owner sets up server
- **Week 3**: Data migration & testing
- **Week 4**: Launch on production server

### Files to Prepare

- `setup.nsi` — NSIS installer script
- `setup-server.bat` — Server mode automation
- `setup-client.bat` — Client mode automation
- `.env.production` — Production environment variables
- `docker-compose.production.yml` — Production Docker config
- `README-DEPLOYMENT.md` — Owner's setup guide

---

## NEXT STEPS

**I can help you create:**

1. ✅ `setup.nsi` — NSIS installer
2. ✅ `setup-server.bat` & `setup-client.bat` — Automation scripts
3. ✅ `.env.production` — Production config template
4. ✅ `DEPLOYMENT-GUIDE.md` — Step-by-step for owner
5. ✅ Test the EXE build locally

**Which would you like me to start with?**
