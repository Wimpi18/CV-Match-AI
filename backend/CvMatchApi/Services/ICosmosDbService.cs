using System.Threading.Tasks;
using CvMatchApi.Models;

namespace CvMatchApi.Services;

/// <summary>
/// Service interface for interacting with the Cosmos DB structured profile container.
/// </summary>
public interface ICosmosDbService
{
    /// <summary>
    /// Upserts a structured user profile document into the resumes Cosmos DB container.
    /// </summary>
    /// <param name="profile">The user profile document containing personal information, experience, education, and skills.</param>
    Task UpsertProfileAsync(UserProfileDocument profile);

    /// <summary>
    /// Retrieves a structured user profile document from the resumes container using email.
    /// </summary>
    /// <param name="email">The candidate's email (document ID and partition key).</param>
    /// <returns>The user profile document, or null if not found.</returns>
    Task<UserProfileDocument?> GetProfileAsync(string email);

    /// <summary>
    /// Upserts an optimized Markdown CV document into the resumes container.
    /// </summary>
    /// <param name="doc">The optimized resume document.</param>
    Task UpsertOptimizedCvAsync(OptimizedCvDocument doc);
}
