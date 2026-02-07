using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

public sealed class CondivaDbContextFactory : IDesignTimeDbContextFactory<CondivaDbContext>
{
    public CondivaDbContext CreateDbContext(string[] args)
    {
        var basePath = Directory.GetCurrentDirectory();

        // When invoked from repo root, appsettings live under "Condiva.Api/".
        // When invoked from project directory, they live in the current directory.
        var appSettingsBasePath = Directory.Exists(Path.Combine(basePath, "Condiva.Api"))
            ? Path.Combine(basePath, "Condiva.Api")
            : basePath;

        var configuration = new ConfigurationBuilder()
            .SetBasePath(appSettingsBasePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Design-time default; not used at runtime.
            connectionString = "Data Source=condiva.db";
        }

        return new CondivaDbContext(
            new DbContextOptionsBuilder<CondivaDbContext>()
                .UseSqlite(connectionString)
                .Options);
    }
}

