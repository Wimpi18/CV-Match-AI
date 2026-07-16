using System;
using Newtonsoft.Json;

namespace CvMatchApi.Models;

/// <summary>
/// Domain model representing a structured user CV profile stored in Cosmos DB.
/// </summary>
public class UserProfileDocument
{
    /// <summary>
    /// Gets or sets the unique document identifier in Cosmos DB (matches candidate's email).
    /// </summary>
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the partition key in Cosmos DB (matches candidate's email).
    /// </summary>
    [JsonProperty("userId")]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the candidate's personal information.
    /// </summary>
    [JsonProperty("personalInfo")]
    public object PersonalInfo { get; set; } = new();

    /// <summary>
    /// Gets or sets the candidate's professional work experience.
    /// </summary>
    [JsonProperty("experience")]
    public object Experience { get; set; } = new();

    /// <summary>
    /// Gets or sets the candidate's academic education history.
    /// </summary>
    [JsonProperty("education")]
    public object Education { get; set; } = new();

    /// <summary>
    /// Gets or sets the candidate's categorized standard and custom skills.
    /// </summary>
    [JsonProperty("skills")]
    public object Skills { get; set; } = new();

    /// <summary>
    /// Gets or sets the raw unstructured text extracted from the source PDF CV.
    /// </summary>
    [JsonProperty("rawText")]
    public string RawText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the timestamp when this profile was last generated/updated.
    /// </summary>
    [JsonProperty("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
