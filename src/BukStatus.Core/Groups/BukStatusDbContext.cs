using BukStatus.Core.Pot;
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
    public DbSet<PotSettings> PotSettings => Set<PotSettings>();

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

        modelBuilder.Entity<PotSettings>(settings =>
        {
            settings.Property(s => s.Id).ValueGeneratedNever();
            settings.Property(s => s.Title).HasMaxLength(100);
            settings.Property(s => s.Subtitle).HasMaxLength(200);
            settings.Property(s => s.Milestones).HasMaxLength(100);
            settings.Property(s => s.SamvirkPercent).HasPrecision(5, 2);
            settings.Property(s => s.ExtraContributions).HasPrecision(18, 2);
            settings.Property(s => s.ExtraDescriptionFilter).HasMaxLength(200);
            settings.Property(s => s.ExtraExcludedPersonIds).HasMaxLength(2000);
            settings.Ignore(s => s.MilestoneList);
            settings.Ignore(s => s.ExcludedPersonIdList);
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
