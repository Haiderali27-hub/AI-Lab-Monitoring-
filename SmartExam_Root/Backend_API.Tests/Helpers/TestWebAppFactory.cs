using Backend_API.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend_API.Tests.Helpers;

public class TestWebAppFactory : WebApplicationFactory<Program>
{
    public SeededData SeededData { get; private set; } = null!;

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

        // Use InMemory database for testing
        builder.UseSetting("UseInMemoryDatabase", "true");

        // Disable real email in tests
        builder.UseSetting("Email:SenderEmail", "test@test.com");
        builder.UseSetting("Email:AppPassword", "fake_password");
    }
}
