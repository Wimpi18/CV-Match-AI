using System;
using System.Text.Json.Serialization;

namespace CvMatchApi.Models;

/// <summary>
/// Domain model representing a structured user CV profile stored in Cosmos DB.
/// </summary>
public class UserProfileDocument
{
    /// <summary>
    /// Gets or sets the unique document identifier in Cosmos DB (matches candidate's email).
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the partition key in Cosmos DB (matches candidate's email).
    /// </summary>
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the candidate's personal information.
    /// </summary>
    [JsonPropertyName("personalInfo")]
    public object PersonalInfo { get; set; } = new();

    /// <summary>
    /// Gets or sets the candidate's professional work experience.
    /// </summary>
    [JsonPropertyName("experience")]
    public object Experience { get; set; } = new();

    /// <summary>
    /// Gets or sets the candidate's academic education history.
    /// </summary>
    [JsonPropertyName("education")]
    public object Education { get; set; } = new();

    /// <summary>
    /// Gets or sets the candidate's categorized standard and custom skills.
    /// </summary>
    [JsonPropertyName("skills")]
    public object Skills { get; set; } = new();

    /// <summary>
    /// Gets or sets the raw unstructured text extracted from the source PDF CV.
    /// </summary>
    [JsonPropertyName("rawText")]
    public string RawText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the timestamp when this profile was last generated/updated.
    /// </summary>
    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
