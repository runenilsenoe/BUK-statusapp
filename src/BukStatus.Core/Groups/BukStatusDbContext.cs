using Microsoft.EntityFrameworkCore;

namespace BukStatus.Core.Groups;

public class BukStatusDbContext(DbContextOptions<BukStatusDbContext> options) : DbContext(options)
{
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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
