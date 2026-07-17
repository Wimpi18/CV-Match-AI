using System;
using System.Threading.Tasks;
using CvMatchApi.Models;
using Microsoft.Azure.Cosmos;

namespace CvMatchApi.Services;

/// <summary>
/// Service class implementing Cosmos DB persistence for structured user CV profiles.
/// </summary>
public class CosmosDbService : ICosmosDbService
{
    private readonly Container _container;

    /// <summary>
    /// Initializes a new instance of the <see cref="CosmosDbService"/> class.
    /// </summary>
    /// <param name="cosmosClient">The Cosmos DB client.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="cosmosClient"/> is null.</exception>
    public CosmosDbService(CosmosClient cosmosClient)
    {
        if (cosmosClient == null)
        {
            throw new ArgumentNullException(nameof(cosmosClient));
        }

        var databaseName =
            Environment.GetEnvironmentVariable("COSMOS_DATABASE_NAME") ?? "cvmatch-store";
        var containerName =
            Environment.GetEnvironmentVariable("COSMOS_CONTAINER_NAME") ?? "resumes";
        _container = cosmosClient.GetContainer(databaseName, containerName);
    }

    /// <inheritdoc />
    public async Task UpsertProfileAsync(UserProfileDocument profile)
    {
        if (profile == null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        var partitionKey = new PartitionKey(profile.UserId);
        await _container.UpsertItemAsync(profile, partitionKey).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<UserProfileDocument?> GetProfileAsync(string email)
    {
        if (string.IsNullOrEmpty(email))
        {
            throw new ArgumentNullException(nameof(email));
        }

        try
        {
            var partitionKey = new PartitionKey(email);
            var response = await _container
                .ReadItemAsync<UserProfileDocument>(email, partitionKey)
                .ConfigureAwait(false);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task UpsertOptimizedCvAsync(OptimizedCvDocument doc)
    {
        if (doc == null)
        {
            throw new ArgumentNullException(nameof(doc));
        }

        var partitionKey = new PartitionKey(doc.UserId);
        await _container.UpsertItemAsync(doc, partitionKey).ConfigureAwait(false);
    }
}
