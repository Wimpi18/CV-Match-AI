using System;
using Newtonsoft.Json;

namespace CvMatchApi.Models;

/// <summary>
/// Domain model representing an optimized Markdown resume and ATS score stored in Cosmos DB.
/// </summary>
public class OptimizedCvDocument
{
    /// <summary>
    /// Gets or sets the unique document identifier in Cosmos DB (e.g., "{email}_optimized_{logId}").
    /// </summary>
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the partition key in Cosmos DB (matches candidate's email).
    /// </summary>
    [JsonProperty("userId")]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the ID of the transaction usage log from Azure SQL.
    /// </summary>
    [JsonProperty("logId")]
    public int LogId { get; set; }

    /// <summary>
    /// Gets or sets the optimized CV content in Markdown format.
    /// </summary>
    [JsonProperty("optimizedCvMarkdown")]
    public string OptimizedCvMarkdown { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the ATS match score percentage (0-100).
    /// </summary>
    [JsonProperty("atsMatchScore")]
    public int AtsMatchScore { get; set; }

    /// <summary>
    /// Gets or sets the target job position title.
    /// </summary>
    [JsonProperty("jobTitle")]
    public string JobTitle { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the target job requirements description.
    /// </summary>
    [JsonProperty("jobDescription")]
    public string JobDescription { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the timestamp when this optimized resume was created.
    /// </summary>
    [JsonProperty("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
