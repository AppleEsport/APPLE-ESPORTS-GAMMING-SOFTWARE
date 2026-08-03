# Script to query wallet transactions via dotnet and the existing DbContext
# This script compiles and runs a database query to check for wallet amounts

$projectPath = "AppleEsportsErp\src\AppleEsportsErp.Api"
$scriptDir = Get-Location

Write-Host "Building API project to get database context..." -ForegroundColor Cyan
dotnet build $projectPath -c Debug --quiet

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed" -ForegroundColor Red
    exit 1
}

# Create a temporary query program
$tempProgram = @"
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using AppleEsportsErp.Infrastructure.Data;

var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var connectionString = config.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Port=5432;Database=gamecafe_erp;Username=postgres;Password=CHANGE_ME_IN_PRODUCTION";

Console.WriteLine("=== Wallet Transactions Analysis ===\n");
Console.WriteLine("Connecting to database...\n");

var services = new ServiceCollection();
services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

var sp = services.BuildServiceProvider();
using (var dbContext = sp.GetRequiredService<AppDbContext>())
{
    try
    {
        // STEP 1: Count
        Console.WriteLine(">>> STEP 1: Count wallet transactions by amount\n");
        var count1010 = dbContext.WalletTransactions.Where(w => w.Amount == 1010).Count();
        var count1000 = dbContext.WalletTransactions.Where(w => w.Amount == 1000).Count();
        var totalCount = dbContext.WalletTransactions.Count();

        Console.WriteLine(\$"Total wallet transactions: {totalCount}\");
        Console.WriteLine(\$"Transactions with Amount = 1010: {count1010}\");
        Console.WriteLine(\$"Transactions with Amount = 1000: {count1000}\");
        Console.WriteLine();

        // STEP 2: Show 1010 transactions
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
                Console.WriteLine(\$"ID: {tx.Id}\");
                Console.WriteLine(\$"  Amount: ₹{tx.Amount} | Bonus: ₹{tx.BonusAmount} | Cash: ₹{tx.CashAmount} | Online: ₹{tx.OnlineAmount}\");
                Console.WriteLine(\$"  Action: {tx.Action} | PaymentType: {tx.PaymentType}\");
                Console.WriteLine(\$"  CreatedAt: {tx.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC\");
                Console.WriteLine();
            }
        }

        // STEP 3: Show 1000 transactions
        if (count1000 > 0)
        {
            Console.WriteLine(">>> STEP 3: Details of ₹1000 transactions (most recent 5 shown)\n");
            var txs1000 = dbContext.WalletTransactions
                .Where(w => w.Amount == 1000)
                .OrderByDescending(w => w.CreatedAt)
                .Take(5)
                .ToList();

            foreach (var tx in txs1000)
            {
                Console.WriteLine(\$"ID: {tx.Id}\");
                Console.WriteLine(\$"  Amount: ₹{tx.Amount} | Bonus: ₹{tx.BonusAmount} | Cash: ₹{tx.CashAmount} | Online: ₹{tx.OnlineAmount}\");
                Console.WriteLine(\$"  Action: {tx.Action} | PaymentType: {tx.PaymentType}\");
                Console.WriteLine(\$"  CreatedAt: {tx.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC\");
                Console.WriteLine();
            }
        }

        // STEP 4: Date analysis
        Console.WriteLine(">>> STEP 4: Date analysis\n");
        var fixDate = new DateTimeOffset(2026, 8, 3, 0, 47, 29, TimeSpan.FromHours(5.5));
        Console.WriteLine(\$"Safeguard fix deployed: {fixDate:yyyy-MM-dd HH:mm:ss} (India Time)\n\");

        if (count1010 > 0)
        {
            var latest1010 = dbContext.WalletTransactions.Where(w => w.Amount == 1010).Max(w => w.CreatedAt);
            var earliest1010 = dbContext.WalletTransactions.Where(w => w.Amount == 1010).Min(w => w.CreatedAt);
            Console.WriteLine(\$"₹1010 transactions: {earliest1010:yyyy-MM-dd} to {latest1010:yyyy-MM-dd}\");
            Console.WriteLine(\$"  All BEFORE fix? {(latest1010 < fixDate ? "YES ✓" : "NO ✗")}\");
            Console.WriteLine();
        }

        if (count1000 > 0)
        {
            var earliest1000 = dbContext.WalletTransactions.Where(w => w.Amount == 1000).Min(w => w.CreatedAt);
            var latest1000 = dbContext.WalletTransactions.Where(w => w.Amount == 1000).Max(w => w.CreatedAt);
            Console.WriteLine(\$"₹1000 transactions: {earliest1000:yyyy-MM-dd} to {latest1000:yyyy-MM-dd}\");
            Console.WriteLine(\$"  Any AFTER fix? {(latest1000 >= fixDate ? "YES ✓" : "NO ✗")}\");
        }

        // SUMMARY
        Console.WriteLine("\n>>> SUMMARY\n");
        Console.WriteLine(\$"✓ ₹1010 historical records exist: {count1010 > 0}\");
        Console.WriteLine(\$"✓ ₹1000 new transactions exist: {count1000 > 0}\");
    }
    catch (Exception ex)
    {
        Console.WriteLine(\$"ERROR: {ex.Message}\");
        throw;
    }
}
"@

Write-Host "Running query..." -ForegroundColor Cyan
dotnet interactive run-script --script-path (New-Item -Path $env:TEMP -Name "query.csx" -ItemType File -Value $scriptDir\$scriptDir\QueryWalletTransactions.cs -Force).FullName
