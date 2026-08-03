using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using AppleEsportsErp.Infrastructure.Data;

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings__DefaultConnection not found");

Console.WriteLine("=== Wallet Transactions Query ===\n");
Console.WriteLine($"Connecting to database...\n");

var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseNpgsql(connectionString)
    .LogTo(Console.WriteLine, LogLevel.Error)
    .Build();

using (var dbContext = new AppDbContext(options))
{
    try
    {
        // STEP 1: Count transactions
        Console.WriteLine(">>> STEP 1: Count wallet transactions by amount\n");

        var count1010 = dbContext.WalletTransactions.Count(w => w.Amount == 1010);
        var count1000 = dbContext.WalletTransactions.Count(w => w.Amount == 1000);
        var total = dbContext.WalletTransactions.Count();

        Console.WriteLine($"Total wallet transactions: {total}");
        Console.WriteLine($"Transactions with Amount = ₹1010: {count1010}");
        Console.WriteLine($"Transactions with Amount = ₹1000: {count1000}");
        Console.WriteLine();

        // STEP 2: Show 1010 transactions (oldest first)
        if (count1010 > 0)
        {
            Console.WriteLine(">>> STEP 2: Details of ₹1010 transactions (oldest 5 shown)\n");
            var txs1010 = dbContext.WalletTransactions
                .Where(w => w.Amount == 1010)
                .OrderBy(w => w.CreatedAt)
                .Take(5)
                .ToList();

            foreach (var tx in txs1010)
            {
                Console.WriteLine($"ID: {tx.Id}");
                Console.WriteLine($"  Amount: ₹{tx.Amount} | Bonus: ₹{tx.BonusAmount} | Cash: ₹{tx.CashAmount} | Online: ₹{tx.OnlineAmount}");
                Console.WriteLine($"  Action: {tx.Action} | PaymentType: {tx.PaymentType}");
                Console.WriteLine($"  CreatedAt: {tx.CreatedAt:yyyy-MM-dd HH:mm:ss UTC}");
                Console.WriteLine();
            }
        }
        else
        {
            Console.WriteLine(">>> STEP 2: No ₹1010 transactions found\n");
        }

        // STEP 3: Show 1000 transactions (newest first)
        if (count1000 > 0)
        {
            Console.WriteLine(">>> STEP 3: Details of ₹1000 transactions (newest 5 shown)\n");
            var txs1000 = dbContext.WalletTransactions
                .Where(w => w.Amount == 1000)
                .OrderByDescending(w => w.CreatedAt)
                .Take(5)
                .ToList();

            foreach (var tx in txs1000)
            {
                Console.WriteLine($"ID: {tx.Id}");
                Console.WriteLine($"  Amount: ₹{tx.Amount} | Bonus: ₹{tx.BonusAmount} | Cash: ₹{tx.CashAmount} | Online: ₹{tx.OnlineAmount}");
                Console.WriteLine($"  Action: {tx.Action} | PaymentType: {tx.PaymentType}");
                Console.WriteLine($"  CreatedAt: {tx.CreatedAt:yyyy-MM-dd HH:mm:ss UTC}");
                Console.WriteLine();
            }
        }
        else
        {
            Console.WriteLine(">>> STEP 3: No ₹1000 transactions found\n");
        }

        // STEP 4: Date analysis
        Console.WriteLine(">>> STEP 4: Date analysis\n");
        var fixDeployDate = new DateTimeOffset(2026, 8, 3, 0, 47, 29, TimeSpan.FromHours(5.5));
        Console.WriteLine($"Safeguard fix deployed: {fixDeployDate:yyyy-MM-dd HH:mm:ss} (India Time)\n");

        if (count1010 > 0)
        {
            var latest1010 = dbContext.WalletTransactions
                .Where(w => w.Amount == 1010)
                .Max(w => w.CreatedAt);
            var earliest1010 = dbContext.WalletTransactions
                .Where(w => w.Amount == 1010)
                .Min(w => w.CreatedAt);

            Console.WriteLine($"₹1010 transactions:");
            Console.WriteLine($"  Date range: {earliest1010:yyyy-MM-dd} to {latest1010:yyyy-MM-dd}");
            Console.WriteLine($"  Latest date: {latest1010:yyyy-MM-dd HH:mm:ss UTC}");
            Console.WriteLine($"  All BEFORE fix? {(latest1010 < fixDeployDate ? "✓ YES" : "✗ NO")}");
            Console.WriteLine();
        }

        if (count1000 > 0)
        {
            var earliest1000 = dbContext.WalletTransactions
                .Where(w => w.Amount == 1000)
                .Min(w => w.CreatedAt);
            var latest1000 = dbContext.WalletTransactions
                .Where(w => w.Amount == 1000)
                .Max(w => w.CreatedAt);

            Console.WriteLine($"₹1000 transactions:");
            Console.WriteLine($"  Date range: {earliest1000:yyyy-MM-dd} to {latest1000:yyyy-MM-dd}");
            Console.WriteLine($"  Latest date: {latest1000:yyyy-MM-dd HH:mm:ss UTC}");
            Console.WriteLine($"  Any AFTER fix? {(latest1000 >= fixDeployDate ? "✓ YES" : "✗ NO")}");
        }

        // SUMMARY
        Console.WriteLine("\n>>> SUMMARY\n");
        if (count1010 > 0)
        {
            Console.WriteLine($"✓ {count1010} historical ₹1010 record(s) found (pre-fix data)");
        }
        else
        {
            Console.WriteLine($"✓ NO ₹1010 records found (all data is clean)");
        }

        if (count1000 > 0)
        {
            Console.WriteLine($"✓ {count1000} ₹1000 record(s) found (correct amounts)");
        }

        Console.WriteLine($"\n✓ Database connection: Successful");
        Console.WriteLine($"✓ Total transactions analyzed: {total}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"ERROR: {ex.Message}");
        Console.WriteLine($"Inner: {ex.InnerException?.Message}");
        Environment.Exit(1);
    }
}
