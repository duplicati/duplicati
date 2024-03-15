using Duplicati.WebserverCore.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace Duplicati.WebserverCore.Database;

public class MainDbContext(DbContextOptions<MainDbContext> options) : DbContext(options)
{
    public DbSet<Option> Options { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);
    }
}