using System;
using System.ComponentModel.DataAnnotations;

namespace CvMatchApi.Models;

/// <summary>
/// Domain model class representing a registered User in CV-Match-AI.
/// </summary>
public class User
{
    /// <summary>
    /// Gets or sets the unique primary key identifier of the user.
    /// </summary>
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the unique email address of the user.
    /// </summary>
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the full display name of the user.
    /// </summary>
    [Required]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the date and time when the user registration took place.
    /// </summary>
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
}
