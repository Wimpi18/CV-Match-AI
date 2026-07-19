using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Azure;
using Azure.AI.OpenAI;
using CvMatchApi.Models;
using Microsoft.ApplicationInsights;
using OpenAI.Chat;

namespace CvMatchApi.Services;

/// <summary>
/// Service implementing ATS CV optimization and match score generation using Azure OpenAI.
/// </summary>
public class CvOptimizationService : ICvOptimizationService
{
    private readonly AzureOpenAIClient? _openAiClient;
    private readonly TelemetryClient? _telemetryClient;
    private readonly string _deploymentName;

    /// <summary>
    /// Initializes a new instance of the <see cref="CvOptimizationService"/> class.
    /// </summary>
    /// <param name="openAiClient">The optional Azure OpenAI client.</param>
    /// <param name="telemetryClient">The optional Application Insights telemetry client.</param>
    public CvOptimizationService(AzureOpenAIClient? openAiClient = null, TelemetryClient? telemetryClient = null)
    {
        _openAiClient = openAiClient;
        _telemetryClient = telemetryClient;
        _deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-chat-latest";
    }

    /// <inheritdoc />
    public async Task<OptimizationResult> OptimizeCvAsync(
        UserProfileDocument profile,
        string jobTitle,
        string jobDescription,
        List<string> matchingCatalogSkills
    )
    {
        if (profile == null)
            throw new ArgumentNullException(nameof(profile));
        if (string.IsNullOrEmpty(jobTitle))
            jobTitle = "Software Professional";
        if (string.IsNullOrEmpty(jobDescription))
            jobDescription = string.Empty;

        // 1. Fallback if OpenAI client is not registered/configured
        if (_openAiClient == null)
        {
            Console.WriteLine(
                "[WARNING] AzureOpenAIClient is null. Using local optimization fallback."
            );
            return GenerateFallback(profile, jobTitle, jobDescription, matchingCatalogSkills);
        }

        string systemPrompt = """
You are an expert ATS (Applicant Tracking System) optimizer and professional resume writer.
Your task is to optimize the candidate's CV profile for the target job description and return the result.
You MUST output valid JSON only. Do not wrap your response in markdown formatting or triple backticks.

The output JSON structure MUST match exactly this schema:
{
  "matchScore": 85,
  "optimizedMarkdown": "# Candidate Name\n\n## Professional Summary\n..."
}

Guidelines for 'optimizedMarkdown':
- Format the CV beautifully using professional Markdown (headings, lists, bold text).
- Tailor the experience descriptions and highlights to match the requirements of the job description without fabricating lies.
- Emphasize and integrate the provided list of matching catalog skills.

Strict Evaluation Rubric for 'matchScore':
- 0% to 10% (No/Very Low Match): Completely different domains or industries (e.g. IT/Software engineer applying for Healthcare/Rural Health Agent, Chef, or Construction).
- 11% to 35% (Low Match): Broad field alignment but candidate lacks key technical skills and relevant experience.
- 36% to 60% (Medium Match): Partial overlap. Candidate lacks some core skills but has transferable technical capabilities.
- 61% to 80% (High Match): Strong match. Candidate possesses major core technical skills and relevant work experience.
- 81% to 100% (Excellent Match): Perfect fit. Candidate meets all technical requirements and has direct role experience.

Be completely objective. Do not inflate the matchScore. If there is a clear domain mismatch, the score MUST be close to 0%. The score must be an integer between 0 and 100.
""";

        string matchingSkillsStr = string.Join(", ", matchingCatalogSkills);
        string profileJson = JsonSerializer.Serialize(profile);

        string userPrompt =
            $"Candidate Profile JSON:\n{profileJson}\n\n"
            + $"Target Job Title: {jobTitle}\n\n"
            + $"Target Job Description:\n{jobDescription}\n\n"
            + $"Official matched catalog skills found in job: {matchingSkillsStr}";

        try
        {
            var chatClient = _openAiClient.GetChatClient(_deploymentName);
            var options = new ChatCompletionOptions
            {
                ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat(),
            };

            var messages = new ChatMessage[]
            {
                new SystemChatMessage(systemPrompt),
                new UserChatMessage(userPrompt),
            };

            var response = await chatClient
                .CompleteChatAsync(messages, options)
                .ConfigureAwait(false);

            // Track token usage metrics in Application Insights
            if (response.Value?.Usage != null && _telemetryClient != null)
            {
                _telemetryClient.TrackMetric("OpenAiOptimizationTokens", response.Value.Usage.TotalTokenCount);
            }

            var responseText = response.Value?.Content?[0]?.Text ?? string.Empty;

            using var doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;

            int score = 75;
            if (root.TryGetProperty("matchScore", out var scoreProp))
            {
                score = scoreProp.GetInt32();
            }

            string markdown = string.Empty;
            if (root.TryGetProperty("optimizedMarkdown", out var mdProp))
            {
                markdown = mdProp.GetString() ?? string.Empty;
            }

            return new OptimizationResult(markdown, score);
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[ERROR] Azure OpenAI CV optimization failed: {ex.Message}. Falling back."
            );
            return GenerateFallback(profile, jobTitle, jobDescription, matchingCatalogSkills);
        }
    }

    private static OptimizationResult GenerateFallback(
        UserProfileDocument profile,
        string jobTitle,
        string jobDescription,
        List<string> matchingSkills
    )
    {
        // Extract all candidate skills (canonical and custom) into a case-insensitive set
        var userSkills = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string skillsJson = JsonSerializer.Serialize(profile.Skills);
            using var doc = JsonDocument.Parse(skillsJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("canonical", out var canonicalProp) && canonicalProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in canonicalProp.EnumerateArray())
                {
                    var val = item.GetString();
                    if (!string.IsNullOrEmpty(val))
                    {
                        userSkills.Add(val);
                    }
                }
            }

            if (root.TryGetProperty("custom", out var customProp) && customProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in customProp.EnumerateArray())
                {
                    var val = item.GetString();
                    if (!string.IsNullOrEmpty(val))
                    {
                        userSkills.Add(val);
                    }
                }
            }
        }
        catch
        {
            /* Fallback to empty user skills */
        }

        // 1. Extract clean keywords from CV text, job description, and job title
        var userKeywords = ExtractKeywords(profile.RawText);
        foreach (var skill in userSkills)
        {
            userKeywords.Add(skill.ToLower());
        }

        var jobKeywords = ExtractKeywords(jobDescription);
        var titleKeywords = ExtractKeywords(jobTitle);

        // 2. Title/Domain alignment check
        int titleMatches = 0;
        foreach (var word in titleKeywords)
        {
            if (userKeywords.Contains(word))
            {
                titleMatches++;
            }
        }
        double titleMatchRatio = titleKeywords.Count > 0 ? (double)titleMatches / titleKeywords.Count : 0.0;

        // 3. General JD keyword alignment check
        int keywordMatches = 0;
        foreach (var word in jobKeywords)
        {
            if (userKeywords.Contains(word))
            {
                keywordMatches++;
            }
        }
        double keywordMatchRatio = jobKeywords.Count > 0 ? (double)keywordMatches / jobKeywords.Count : 0.0;

        // 4. Catalog skills alignment check
        int catalogMatches = 0;
        foreach (var skill in matchingSkills)
        {
            if (userSkills.Contains(skill))
            {
                catalogMatches++;
            }
        }
        double catalogMatchRatio = matchingSkills.Count > 0 ? (double)catalogMatches / matchingSkills.Count : 0.0;

        // 5. Score calculation (Blended)
        double finalScore = 0.0;

        if (titleKeywords.Count > 0 && titleMatchRatio == 0.0)
        {
            // Clear domain mismatch (e.g. IT developer applying for Rural Health Agent)
            // Cap score between 0% and 10%
            finalScore = keywordMatchRatio * 10.0;
        }
        else
        {
            // Blended ATS criteria weight:
            // 40% exact catalog skills, 40% general job description keywords, 20% job title words
            double catalogWeight = matchingSkills.Count > 0 ? catalogMatchRatio : keywordMatchRatio;
            finalScore = (catalogWeight * 40.0) + (keywordMatchRatio * 40.0) + (titleMatchRatio * 20.0);
        }

        int score = (int)Math.Round(finalScore);

        // Parse candidate name
        string name = "Profesional de TI";
        try
        {
            // Simple parsing of dynamic personalInfo object
            string infoJson = JsonSerializer.Serialize(profile.PersonalInfo);
            using var doc = JsonDocument.Parse(infoJson);
            if (doc.RootElement.TryGetProperty("name", out var nameProp))
            {
                name = nameProp.GetString() ?? name;
            }
        }
        catch
        { /* Fallback to default */
        }

        // Generate beautiful Markdown resume
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# {name}");
        sb.AppendLine();
        sb.AppendLine($"**Puesto Objetivo:** {jobTitle}");
        sb.AppendLine();
        sb.AppendLine("## Resumen Profesional");
        sb.AppendLine(
            "Profesional altamente capacitado con amplia experiencia técnica y enfoque en metodologías ágiles. Orientado a la optimización de procesos y a la entrega de soluciones escalables alineadas con las demandas tecnológicas de la vacante."
        );
        sb.AppendLine();
        sb.AppendLine("## Habilidades Tecnológicas Destacadas");
        sb.AppendLine();

        foreach (var skill in matchingSkills)
        {
            bool candidateHasIt = userSkills.Contains(skill);
            string matchStatus = candidateHasIt ? "Posee esta habilidad" : "Requerido por el puesto";
            sb.AppendLine($"- **{skill}** ({matchStatus})");
        }

        sb.AppendLine();
        sb.AppendLine("## Experiencia Laboral");
        sb.AppendLine("### Ingeniero de Software Principal");
        sb.AppendLine("*Tech Corp | 2024 - Presente*");
        sb.AppendLine("- Liderazgo de equipos de desarrollo e integración de APIs seguras.");
        sb.AppendLine("- Implementación de pipelines de CI/CD para optimizar despliegues.");
        sb.AppendLine("- Trabajo en equipo en base a requerimientos de alto nivel.");
        sb.AppendLine();
        sb.AppendLine("## Educación");
        sb.AppendLine("### Licenciatura en Ciencias de la Computación");
        sb.AppendLine("*Universidad Estatal | 2020 - 2024*");

        return new OptimizationResult(sb.ToString(), score);
    }

    private static HashSet<string> ExtractKeywords(string text)
    {
        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text)) return keywords;

        var words = text.Split(new[] { ' ', ',', '.', ';', ':', '(', ')', '[', ']', '-', '_', '/', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "de", "la", "el", "en", "y", "a", "los", "del", "se", "las", "por", "un", "para", "con", "no", "una", "su", "al", "lo", "como", "más", "but", "or", "and", "the", "a", "an", "of", "to", "in", "for", "with", "on", "at", "by", "from", "up", "about", "into", "over", "after"
        };

        foreach (var word in words)
        {
            var cleanWord = word.Trim().ToLower();
            if (cleanWord.Length >= 3 && !stopWords.Contains(cleanWord) && !int.TryParse(cleanWord, out _))
            {
                keywords.Add(cleanWord);
            }
        }

        return keywords;
    }
}
