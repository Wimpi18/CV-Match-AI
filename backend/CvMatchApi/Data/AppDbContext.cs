using CvMatchApi.Models;
using Microsoft.EntityFrameworkCore;

namespace CvMatchApi.Data;

/// <summary>
/// Database Context for CV-Match-AI application, managing relational tables in Azure SQL.
/// </summary>
public class AppDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AppDbContext"/> class with the specified options.
    /// </summary>
    /// <param name="options">The context options for configuration.</param>
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options) { }

    /// <summary>
    /// Gets or sets the collection of registered users in the database.
    /// </summary>
    public DbSet<User> Users { get; set; }

    /// <summary>
    /// Gets or sets the collection of job postings in the database.
    /// </summary>
    public DbSet<JobPosting> JobPostings { get; set; }

    /// <summary>
    /// Gets or sets the collection of usage logs in the database.
    /// </summary>
    public DbSet<UsageLog> UsageLogs { get; set; }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        if (modelBuilder == null)
        {
            throw new System.ArgumentNullException(nameof(modelBuilder));
        }

        base.OnModelCreating(modelBuilder);

        // Ensure email is unique in the database index
        modelBuilder.Entity<User>().HasIndex(u => u.Email).IsUnique();
    }
}
