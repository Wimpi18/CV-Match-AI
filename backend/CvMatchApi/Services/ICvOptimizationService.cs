using System.Collections.Generic;
using System.Threading.Tasks;
using CvMatchApi.Models;

namespace CvMatchApi.Services;

/// <summary>
/// Service interface for generating optimized Markdown resumes and ATS matching scores using Azure OpenAI.
/// </summary>
public interface ICvOptimizationService
{
    /// <summary>
    /// Generates an optimized Markdown resume and an ATS match score based on target job description.
    /// </summary>
    /// <param name="profile">The candidate's structured profile document.</param>
    /// <param name="jobTitle">The target job position title.</param>
    /// <param name="jobDescription">The target job requirements description.</param>
    /// <param name="matchingCatalogSkills">Standardized matching skills from SQL Server taxonomy.</param>
    /// <returns>A result containing the optimized Markdown CV and ATS match score.</returns>
    Task<OptimizationResult> OptimizeCvAsync(
        UserProfileDocument profile,
        string jobTitle,
        string jobDescription,
        List<string> matchingCatalogSkills
    );
}

/// <summary>
/// Represents the result of a CV optimization operation.
/// </summary>
/// <param name="OptimizedCvMarkdown">The resume formatted in Markdown.</param>
/// <param name="AtsMatchScore">The ATS match score (0-100).</param>
public record OptimizationResult(string OptimizedCvMarkdown, int AtsMatchScore);
