using System;
using System.IO;
using System.Threading.Tasks;
using CvMatchApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CvMatchApi.Controllers;

/// <summary>
/// Controller handling safe PDF uploads.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="UploadController"/> class.
/// </remarks>
/// <param name="blobStorageService">The blob storage service instance.</param>
[Authorize]
[ApiController]
[Route("api/upload")]
public class UploadController(IBlobStorageService blobStorageService) : ControllerBase
{
    private readonly IBlobStorageService _blobStorageService = blobStorageService ?? throw new ArgumentNullException(nameof(blobStorageService));
    private const long MaxFileSizeBytes = 2 * 1024 * 1024; // 2MB

    /// <summary>
    /// Uploads a PDF CV to private Azure Blob Storage.
    /// </summary>
    /// <param name="file">The uploaded file.</param>
    /// <returns>A response containing the unique identifier of the uploaded file.</returns>
    /// <response code="200">Returns the unique file identifier.</response>
    /// <response code="400">If the file is not a valid PDF.</response>
    /// <response code="413">If the file size exceeds 2MB.</response>
    /// <response code="401">If the request is unauthorized.</response>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UploadFileAsync(IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { Message = "No file uploaded." });
        }

        // 1. Enforce 2MB size limit
        if (file.Length > MaxFileSizeBytes)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new { Message = "File size exceeds 2MB limit." });
        }

        // 2. Validate MIME type and extension
        string extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (file.ContentType != "application/pdf" || extension != ".pdf")
        {
            return BadRequest(new { Message = "Only PDF files (.pdf) are allowed." });
        }

        try
        {
            // 3. Rename with UUID to maintain privacy
            string uniqueFileName = $"{Guid.NewGuid()}.pdf";

            using var stream = file.OpenReadStream();
            await _blobStorageService.UploadAsync(stream, uniqueFileName, file.ContentType).ConfigureAwait(false);

            return Ok(new { FileId = uniqueFileName });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "An error occurred during file upload.", Detail = ex.Message });
        }
    }
}
