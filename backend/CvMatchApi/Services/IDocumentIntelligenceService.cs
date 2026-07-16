using System.IO;
using System.Threading.Tasks;

namespace CvMatchApi.Services;

/// <summary>
/// Service interface for extracting text content from PDF CV files using Azure AI Document Intelligence.
/// </summary>
public interface IDocumentIntelligenceService
{
    /// <summary>
    /// Processes a PDF file stream using the layout/read model and returns the extracted structured text.
    /// </summary>
    /// <param name="pdfStream">The PDF file stream to analyze.</param>
    /// <returns>A string containing the extracted text structured by page.</returns>
    Task<string> ExtractTextAsync(Stream pdfStream);
}
