using DotaPingMonitor.Models;
using Microsoft.EntityFrameworkCore;

namespace DotaPingMonitor.Data;

public class PingDbContext : DbContext
{
    public DbSet<PingRecord> PingRecords => Set<PingRecord>();
    public DbSet<CustomPingTarget> CustomPingTargets => Set<CustomPingTarget>();

    protected override void OnConfiguring(
        DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite(DatabaseService.ConnectionString);
    }
}