using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CvMatchApi.Models;

/// <summary>
/// Domain model class representing a CV generation usage log for credit checking.
/// </summary>
public class UsageLog
{
    /// <summary>
    /// Gets or sets the unique primary key identifier of the usage log.
    /// </summary>
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the associated user's primary key identifier.
    /// </summary>
    [Required]
    public int UserId { get; set; }

    /// <summary>
    /// Gets or sets the navigation property for the associated user.
    /// </summary>
    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the CV generation took place.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets details or metadata about the CV generation event.
    /// </summary>
    public string Description { get; set; } = string.Empty;
}
