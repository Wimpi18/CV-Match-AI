using System.IO;
using System.Threading.Tasks;

namespace CvMatchApi.Services;

/// <summary>
/// Service interface for interacting with Azure Blob Storage.
/// </summary>
public interface IBlobStorageService
{
    /// <summary>
    /// Uploads a file stream to the configured private blob container.
    /// </summary>
    /// <param name="stream">The stream of the file to upload.</param>
    /// <param name="fileName">The destination file name (normally a unique identifier).</param>
    /// <param name="contentType">The MIME type content type of the file.</param>
    /// <returns>A task that represents the asynchronous upload operation.</returns>
    Task UploadAsync(Stream stream, string fileName, string contentType);

    /// <summary>
    /// Downloads a file from the configured private blob container as a Stream.
    /// </summary>
    /// <param name="fileName">The name of the file to download.</param>
    /// <returns>A stream representing the downloaded file content.</returns>
    Task<Stream> DownloadAsync(string fileName);
}
