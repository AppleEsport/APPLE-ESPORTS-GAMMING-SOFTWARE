using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using AppleEsportsErp.Infrastructure.Data;
using AppleEsportsErp.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// This script queries wallet transactions to verify the safeguard fix
// Usage: dotnet run < connection string from .env or appsettings.json >

class Program
{
    static async Task Main(string[] args)
    {
        // Load configuration
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("AppleEsportsErp/src/AppleEsportsErp.Api/appsettings.json", optional: true)
            .AddJsonFile("AppleEsportsErp/src/AppleEsportsErp.Api/appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        // Get connection string (local dev or production)
        string? connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=gamecafe_erp;Username=postgres;Password=CHANGE_ME_IN_PRODUCTION";

        // Override with environment variable if provided
        if (Environment.GetEnvironmentVariable("DB_CONNECTION_STRING") != null)
            connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");

        Console.WriteLine("=== Wallet Transactions Analysis ===\n");
        Console.WriteLine($"Connecting to database...\n");

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(connectionString)
                .LogTo(Console.WriteLine)
                .Build();

            using (var dbContext = new AppDbContext(options))
            {
                // Count transactions by amount
                Console.WriteLine(">>> STEP 1: Count wallet transactions by amount\n");

                var amount1010Count = await dbContext.WalletTransactions
                    .Where(w => w.Amount == 1010)
                    .CountAsync();

                var amount1000Count = await dbContext.WalletTransactions
                    .Where(w => w.Amount == 1000)
                    .CountAsync();

                var totalCount = await dbContext.WalletTransactions.CountAsync();

                Console.WriteLine($"Total wallet transactions: {totalCount}");
                Console.WriteLine($"Transactions with Amount = 1010: {amount1010Count}");
                Console.WriteLine($"Transactions with Amount = 1000: {amount1000Count}");
                Console.WriteLine();

                // Get dates of 1010 transactions
                if (amount1010Count > 0)
                {
                    Console.WriteLine(">>> STEP 2: Details of ₹1010 transactions (max 10 shown)\n");
                    var transactions1010 = await dbContext.WalletTransactions
                        .Where(w => w.Amount == 1010)
                        .OrderBy(w => w.CreatedAt)
                        .Take(10)
                        .Select(w => new
                        {
                            w.Id,
                            w.MemberId,
                            w.Amount,
                            w.BonusAmount,
                            w.CashAmount,
                            w.OnlineAmount,
                            w.Action,
                            w.PaymentType,
                            w.CreatedAt
                        })
                        .ToListAsync();

                    foreach (var tx in transactions1010)
                    {
                        Console.WriteLine($"ID: {tx.Id}");
                        Console.WriteLine($"  Amount: ₹{tx.Amount} | Bonus: ₹{tx.BonusAmount} | Cash: ₹{tx.CashAmount} | Online: ₹{tx.OnlineAmount}");
                        Console.WriteLine($"  Action: {tx.Action} | PaymentType: {tx.PaymentType}");
                        Console.WriteLine($"  CreatedAt: {tx.CreatedAt:yyyy-MM-dd HH:mm:ss}");
                        Console.WriteLine();
                    }
                }
                else
                {
                    Console.WriteLine(">>> STEP 2: No ₹1010 transactions found\n");
                }

                // Get sample 1000 transactions
                if (amount1000Count > 0)
                {
                    Console.WriteLine(">>> STEP 3: Details of ₹1000 transactions (most recent, max 10 shown)\n");
                    var transactions1000 = await dbContext.WalletTransactions
                        .Where(w => w.Amount == 1000)
                        .OrderByDescending(w => w.CreatedAt)
                        .Take(10)
                        .Select(w => new
                        {
                            w.Id,
                            w.MemberId,
                            w.Amount,
                            w.BonusAmount,
                            w.CashAmount,
                            w.OnlineAmount,
                            w.Action,
                            w.PaymentType,
                            w.CreatedAt
                        })
                        .ToListAsync();

                    foreach (var tx in transactions1000)
                    {
                        Console.WriteLine($"ID: {tx.Id}");
                        Console.WriteLine($"  Amount: ₹{tx.Amount} | Bonus: ₹{tx.BonusAmount} | Cash: ₹{tx.CashAmount} | Online: ₹{tx.OnlineAmount}");
                        Console.WriteLine($"  Action: {tx.Action} | PaymentType: {tx.PaymentType}");
                        Console.WriteLine($"  CreatedAt: {tx.CreatedAt:yyyy-MM-dd HH:mm:ss}");
                        Console.WriteLine();
                    }
                }
                else
                {
                    Console.WriteLine(">>> STEP 3: No ₹1000 transactions found\n");
                }

                // Check transaction dates relative to fix deployment (Aug 3, 2026)
                Console.WriteLine(">>> STEP 4: Date analysis\n");
                var fixDeploymentDate = new DateTimeOffset(2026, 8, 3, 0, 47, 29, TimeSpan.FromHours(5.5)); // India time
                Console.WriteLine($"Safeguard fix deployed: {fixDeploymentDate:yyyy-MM-dd HH:mm:ss}\n");

                if (amount1010Count > 0)
                {
                    var transactions1010Dates = await dbContext.WalletTransactions
                        .Where(w => w.Amount == 1010)
                        .Select(w => w.CreatedAt)
                        .OrderByDescending(w => w)
                        .ToListAsync();

                    var latestBefore = transactions1010Dates.FirstOrDefault();
                    var earliestBefore = transactions1010Dates.LastOrDefault();

                    Console.WriteLine($"₹1010 transactions:");
                    Console.WriteLine($"  Latest: {latestBefore:yyyy-MM-dd HH:mm:ss}");
                    Console.WriteLine($"  Earliest: {earliestBefore:yyyy-MM-dd HH:mm:ss}");
                    Console.WriteLine($"  All BEFORE fix? {(latestBefore < fixDeploymentDate ? "YES ✓" : "NO ✗")}");
                    Console.WriteLine();
                }

                if (amount1000Count > 0)
                {
                    var transactions1000Dates = await dbContext.WalletTransactions
                        .Where(w => w.Amount == 1000)
                        .Select(w => w.CreatedAt)
                        .OrderBy(w => w)
                        .ToListAsync();

                    var earliestAfter = transactions1000Dates.FirstOrDefault();
                    var latestAfter = transactions1000Dates.LastOrDefault();

                    Console.WriteLine($"₹1000 transactions:");
                    Console.WriteLine($"  Earliest: {earliestAfter:yyyy-MM-dd HH:mm:ss}");
                    Console.WriteLine($"  Latest: {latestAfter:yyyy-MM-dd HH:mm:ss}");
                    Console.WriteLine($"  Any AFTER fix? {(latestAfter > fixDeploymentDate ? "YES ✓" : "NO ✗")}");
                    Console.WriteLine();
                }

                // Summary
                Console.WriteLine(">>> SUMMARY\n");
                Console.WriteLine($"✓ ₹1010 records exist: {amount1010Count > 0}");
                Console.WriteLine($"✓ ₹1000 records exist: {amount1000Count > 0}");
                if (amount1010Count > 0)
                {
                    Console.WriteLine($"✓ ₹1010 are historical (before {fixDeploymentDate:yyyy-MM-dd}): {(await dbContext.WalletTransactions.Where(w => w.Amount == 1010 && w.CreatedAt >= fixDeploymentDate).CountAsync() == 0 ? "YES" : "NO")}");
                }
                if (amount1000Count > 0)
                {
                    Console.WriteLine($"✓ ₹1000 includes new transactions (after {fixDeploymentDate:yyyy-MM-dd}): {(await dbContext.WalletTransactions.Where(w => w.Amount == 1000 && w.CreatedAt >= fixDeploymentDate).CountAsync() > 0 ? "YES" : "NO")}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
            if (ex.InnerException != null)
                Console.WriteLine($"Inner: {ex.InnerException.Message}");
            Environment.Exit(1);
        }
    }
}
