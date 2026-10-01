using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SOR.Infrastructure.Persistence;

/// <summary>
/// Fabryka kontekstu używana wyłącznie przez narzędzia czasu projektowania
/// (<c>dotnet ef migrations add</c>, <c>dotnet ef database update</c>).
/// </summary>
public sealed class SorDbContextDesignTimeFactory : IDesignTimeDbContextFactory<SorDbContext>
{
    public SorDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SorDbContext>();
        optionsBuilder.UseSqlite("Data Source=sor-design-time.db");

        return new SorDbContext(optionsBuilder.Options);
    }
}