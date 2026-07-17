using System.Collections.Generic;
using System.Threading.Tasks;

namespace CvMatchApi.Services;

/// <summary>
/// Service interface for matching raw skills extracted from CVs against the official taxonomy.
/// </summary>
public interface ISkillsCatalogService
{
    /// <summary>
    /// Matches a list of raw skills against the taxonomy, standardizing synonyms and identifying custom skills.
    /// </summary>
    /// <param name="rawSkills">A list of raw skills extracted from a CV.</param>
    /// <returns>A response containing normalized canonical skills and custom/unrecognized skills.</returns>
    Task<SkillMatchResponse> MatchSkillsAsync(List<string> rawSkills);

    /// <summary>
    /// Searches a job description text for occurrences of canonical skills or their synonyms in the taxonomy.
    /// </summary>
    /// <param name="text">The job description or vacancy text to scan.</param>
    /// <returns>A list of recognized canonical skill names found in the text.</returns>
    Task<List<string>> FindMatchingSkillsInTextAsync(string text);
}

/// <summary>
/// Represents the result of a skills matching and classification operation.
/// </summary>
/// <param name="CanonicalSkills">Clean list of recognized standardized skills.</param>
/// <param name="CustomSkills">List of unrecognized custom skills.</param>
public record SkillMatchResponse(List<string> CanonicalSkills, List<string> CustomSkills);
