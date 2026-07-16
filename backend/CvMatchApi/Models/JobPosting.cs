using System;
using System.ComponentModel.DataAnnotations;

namespace CvMatchApi.Models;

/// <summary>
/// Domain model class representing a Job Posting/Description in CV-Match-AI.
/// </summary>
public class JobPosting
{
    /// <summary>
    /// Gets or sets the unique primary key identifier of the job posting.
    /// </summary>
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the title of the job position.
    /// </summary>
    [Required]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the detailed description and requirements of the job.
    /// </summary>
    [Required]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the date and time when the job posting was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
