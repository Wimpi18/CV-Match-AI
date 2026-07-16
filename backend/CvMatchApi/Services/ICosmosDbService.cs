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
}
