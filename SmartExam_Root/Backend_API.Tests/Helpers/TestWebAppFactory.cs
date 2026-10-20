using Backend_API.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend_API.Tests.Helpers;

public class TestWebAppFactory : WebApplicationFactory<Program>
{
    public SeededData SeededData { get; private set; } = null!;

    /// <summary>Module 5: this factory's evidence folder (screenshots), removed when the factory is disposed.</summary>
    public string EvidencePath { get; } = Path.Combine(Path.GetTempPath(), "smartexam-evidence-tests", Guid.NewGuid().ToString("N"));

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        try
        {
            if (Directory.Exists(EvidencePath)) Directory.Delete(EvidencePath, recursive: true);
        }
        catch (IOException)
        {
            // A file still open by a late request: the OS temp cleanup will get it.
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove the real PostgreSQL DbContext
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor != null) services.Remove(descriptor);

            // Replace with in-memory database
            var dbName = $"TestDb_{Guid.NewGuid()}"; // unique per factory instance
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(dbName));

            // Seed test data
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
            SeededData = TestSeeder.SeedAsync(db).GetAwaiter().GetResult();
        });

        // Use test JWT secret matching appsettings
        builder.UseSetting("Jwt:Secret", "SmartExam_SuperSecretKey_ChangeThis_AtLeast32Chars!!");
        builder.UseSetting("Jwt:Issuer", "SmartExam");
        builder.UseSetting("Jwt:Audience", "SmartExamUsers");
        builder.UseSetting("Jwt:ExpiryMinutes", "480");

        // Tests log in many times from the same (null) IP — don't let the login limiter throttle them
        builder.UseSetting("RateLimiting:LoginPermitLimit", "10000");

        // Use InMemory database for testing
        builder.UseSetting("UseInMemoryDatabase", "true");

        // Module 9: pin the secure defaults so a local appsettings.Development.json (e.g. one that
        // enables demo auto-registration) can never change what the tests assert.
        builder.UseSetting("Labs:AutoRegister", "false");
        builder.UseSetting("Exams:RequireDesktopClientForStart", "false");

        // Module 6: the timeout sweeper is off by default so tests that move an exam's window into the past
        // (late save / late submit) are not raced by it; the sweeper tests turn it on with their own interval.
        builder.UseSetting("Submission:TimeoutSweepSeconds", "0");

        // Module 5: captures go to a throwaway folder per factory (deleted on dispose), never the project tree.
        // The disconnect watchdog and the retention job are off unless a test turns them on with its own timing,
        // so a slow test is never raced by a "Disconnected" flag or a cleanup.
        builder.UseSetting("Storage:EvidencePath", EvidencePath);
        builder.UseSetting("Monitoring:DisconnectSweepSeconds", "0");
        builder.UseSetting("Storage:CleanupIntervalSeconds", "0");

        // Disable real email in tests
        builder.UseSetting("Email:SenderEmail", "test@test.com");
        builder.UseSetting("Email:AppPassword", "fake_password");
    }
}
