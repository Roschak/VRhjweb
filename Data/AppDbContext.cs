using Microsoft.EntityFrameworkCore;

namespace HajjVR.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<JamaahProfile> JamaahProfiles => Set<JamaahProfile>();
    public DbSet<RitualProgress> RitualProgresses => Set<RitualProgress>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<JamaahDocument> Documents => Set<JamaahDocument>();
    public DbSet<ChatSession> ChatSessions => Set<ChatSession>();
    public DbSet<ChatMessageEntity> ChatMessages => Set<ChatMessageEntity>();
    public DbSet<Badge> Badges => Set<Badge>();
    public DbSet<UserBadge> UserBadges => Set<UserBadge>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<CrowdSnapshot> CrowdSnapshots => Set<CrowdSnapshot>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<AppUser>().HasIndex(u => u.UserName).IsUnique();
        mb.Entity<AppUser>().HasIndex(u => u.Email);
        mb.Entity<AppUser>()
            .HasOne(u => u.Profile).WithOne(p => p.User)
            .HasForeignKey<JamaahProfile>(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        mb.Entity<AppSetting>().HasKey(s => s.Key);
        mb.Entity<RitualProgress>().HasIndex(p => new { p.UserId, p.Ritual }).IsUnique();
        mb.Entity<ChatSession>()
            .HasMany(s => s.Messages).WithOne(m => m.Session)
            .HasForeignKey(m => m.SessionId).OnDelete(DeleteBehavior.Cascade);
        mb.Entity<CrowdSnapshot>().HasIndex(c => c.Timestamp);
        mb.Entity<UserBadge>().HasIndex(b => new { b.UserId, b.BadgeId }).IsUnique();
    }
}
