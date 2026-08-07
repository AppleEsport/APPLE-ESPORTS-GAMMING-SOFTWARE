@echo off
REM ================================================================
REM Apple Esports ERP - Server Mode Setup (Operator PC)
REM Runs on first installation to set up Docker containers
REM ================================================================

setlocal enabledelayedexpansion

echo.
echo ================================================================
echo  Apple Esports ERP - Server Mode Installation
echo ================================================================
echo.

REM Check if Docker is installed
docker --version >nul 2>&1
if errorlevel 1 (
    echo ERROR: Docker is not installed or not in PATH
    echo Please install Docker Desktop from https://www.docker.com/products/docker-desktop
    echo.
    pause
    exit /b 1
)

REM Get the installation directory
set "INSTDIR=%~dp0"
cd /d "%INSTDIR%"

echo [1/5] Creating necessary directories...
if not exist "data" mkdir data
if not exist "backups" mkdir backups
if not exist "logs" mkdir logs

echo [2/5] Checking for .env file...
if not exist ".env" (
    if exist ".env.example" (
        copy ".env.example" ".env"
        echo Created .env from template. Please edit .env with your settings.
    ) else (
        echo ERROR: .env.example not found
        pause
        exit /b 1
    )
)

echo [3/5] Pulling Docker images and building containers...
docker compose pull
if errorlevel 1 (
    echo ERROR: Failed to pull Docker images
    pause
    exit /b 1
)

echo [4/5] Starting Docker containers...
docker compose up -d
if errorlevel 1 (
    echo ERROR: Failed to start containers
    pause
    exit /b 1
)

echo [5/5] Waiting for services to be ready (30 seconds)...
timeout /t 30 /nobreak

echo.
echo Running database migrations...
docker compose exec -T api dotnet ef database update
if errorlevel 1 (
    echo WARNING: Database migrations failed. Please check the logs.
    echo Continuing anyway...
)

echo.
echo ================================================================
echo  Installation Complete!
echo ================================================================
echo.
echo Your Apple Esports ERP server is now running.
echo.
echo Access the dashboard at:
echo   http://localhost:3000
echo.
echo API is running at:
echo   http://localhost:5016
echo.
echo Database: PostgreSQL on localhost:5432
echo.
echo To stop the server, run:
echo   docker compose down
echo.
echo To view logs, run:
echo   docker compose logs -f
echo.
echo To restart, run:
echo   docker compose up -d
echo.
echo ================================================================
echo.
pause
