using System.Collections.Generic;
using System.Threading.Tasks;

namespace CvMatchApi.Services;

/// <summary>
/// Service interface for structuring unstructured CV text and classified skills into a clean, canonical JSON profile using Azure OpenAI.
/// </summary>
public interface IProfileStructuringService
{
    /// <summary>
    /// Processes unstructured CV text alongside classified standard and custom skills using Azure OpenAI, returning a structured JSON string.
    /// </summary>
    /// <param name="cvText">The raw text extracted from the CV.</param>
    /// <param name="canonicalSkills">The normalized standard skills matched in the catalog.</param>
    /// <param name="customSkills">The custom skills identified.</param>
    /// <returns>A JSON string conforming to the structured profile schema.</returns>
    Task<string> StructureProfileAsync(string cvText, List<string> canonicalSkills, List<string> customSkills);
}
