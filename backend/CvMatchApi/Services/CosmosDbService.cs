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
}
