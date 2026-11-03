using BukStatus.Core.Teams;
using Microsoft.EntityFrameworkCore;

namespace BukStatus.Core.Groups;

public class BukStatusDbContext(DbContextOptions<BukStatusDbContext> options) : DbContext(options)
{
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<MonthlyTarget> MonthlyTargets => Set<MonthlyTarget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Team>(team =>
        {
            team.Property(t => t.Name).HasMaxLength(100);
            team.Property(t => t.LeaderName).HasMaxLength(200);
            team.Property(t => t.BonusPercent).HasPrecision(5, 4);
            team.HasIndex(t => t.Name).IsUnique();
            team.HasMany(t => t.Members).WithOne().HasForeignKey(m => m.TeamId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TeamMember>(member =>
        {
            member.Property(m => m.DisplayName).HasMaxLength(200);
            member.HasIndex(m => m.PersonId).IsUnique(); // one team per person
        });

        modelBuilder.Entity<MonthlyTarget>(target =>
        {
            target.Property(t => t.CumulativePercent).HasPrecision(5, 4);
            target.HasIndex(t => new { t.Year, t.Month }).IsUnique();
        });

        modelBuilder.Entity<Group>(group =>
        {
            group.Property(g => g.Name).HasMaxLength(100);
            group.HasIndex(g => g.Name).IsUnique();
            group.HasMany(g => g.Members).WithOne().HasForeignKey(m => m.GroupId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GroupMember>(member =>
        {
            member.Property(m => m.DisplayName).HasMaxLength(200);
            member.HasIndex(m => new { m.GroupId, m.PersonId }).IsUnique();
        });
    }
}
