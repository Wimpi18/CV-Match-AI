using System;
using System.IO;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace CvMatchApi.Services;

/// <summary>
/// Service implementation for managing uploads to private Azure Blob Storage.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="BlobStorageService"/> class.
/// </remarks>
/// <param name="blobServiceClient">The Microsoft Azure Blob Service client.</param>
public class BlobStorageService(BlobServiceClient blobServiceClient) : IBlobStorageService
{
    private readonly BlobServiceClient _blobServiceClient =
        blobServiceClient ?? throw new ArgumentNullException(nameof(blobServiceClient));
    private readonly string _containerName =
        Environment.GetEnvironmentVariable("AZURE_STORAGE_CONTAINER_NAME") ?? "resumes-pdf";

    /// <inheritdoc />
    public async Task UploadAsync(Stream stream, string fileName, string contentType)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("Filename cannot be null or whitespace.", nameof(fileName));
        }

        // Get container client and create if not exists
        var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.None).ConfigureAwait(false);

        // Get blob client and upload
        var blobClient = containerClient.GetBlobClient(fileName);
        var options = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
        };

        await blobClient.UploadAsync(stream, options).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Stream> DownloadAsync(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("Filename cannot be null or whitespace.", nameof(fileName));
        }

        var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
        var blobClient = containerClient.GetBlobClient(fileName);

        var response = await blobClient.DownloadStreamingAsync().ConfigureAwait(false);
        return response.Value.Content;
    }
}
