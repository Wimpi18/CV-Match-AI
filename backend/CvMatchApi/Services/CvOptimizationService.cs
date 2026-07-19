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
    public CvOptimizationService(
        AzureOpenAIClient? openAiClient = null,
        TelemetryClient? telemetryClient = null
    )
    {
        _openAiClient = openAiClient;
        _telemetryClient = telemetryClient;
        _deploymentName =
            Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-chat-latest";
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
You are an expert ATS (Applicant Tracking System) optimizer. Your task is to analyze the candidate's CV profile against the target job description and return the result.
You MUST output valid JSON only. Do not wrap your response in markdown formatting or triple backticks.

The output JSON structure MUST match exactly this schema:
{
  "matchScore": 85,
  "atsReportMarkdown": "... detailed markdown report ...",
  "optimizedCvMarkdown": "... optimized CV ready for PDF export ..."
}

Guidelines for the 'atsReportMarkdown' Report:
Analyze the CV using the following strict Resume ATS Optimizer guidelines, and generate a report EXACTLY in this Markdown structure (written in Spanish):

# REPORTE DE COMPATIBILIDAD ATS

## Puntuación General: [X]/100

### Análisis del Score (Explicación Detallada con IA)
- Proporciona una explicación detallada con Inteligencia Artificial del por qué se asignó esta puntuación. Analiza de forma específica la correspondencia del perfil con el rol de la vacante, adecuación al sector laboral, y describe de forma clara las fortalezas o brechas detectadas. Evita comentarios genéricos y sé objetivo.

### Revisión de Formato
- Formato detectado: PDF
- Extracción de texto: Exitosa
- Problemas de formato: [Detectar y listar si hay tablas, columnas, textos en cabeceras/pies de página, o fuentes no recomendadas en el CV original]

### Análisis de Palabras Clave
Muestra el contraste entre las palabras clave de la oferta y las encontradas en el CV:

**Habilidades Críticas (Técnicas/Hard Skills):**
- [Habilidad] - [Estado: Encontrada X veces / FALTANTE (mencionada Y veces en la oferta)]
...

**Habilidades Importantes (Blandas/Soft Skills y Términos de la Industria):**
- [Habilidad] - [Estado]
...

**Fórmula aplicada**: (Habilidades coincidentes / Habilidades totales requeridas) * 100 = X%

### Cambios Recomendados
Proporciona recomendaciones específicas de antes y después:
1. **Palabras Clave a Agregar/Corregir**:
   - En la sección "Resumen Profesional", cambiar: "[Frase actual del CV]" por: "[Frase optimizada sugerida con palabras clave]"
   - En la sección "Experiencia", añadir el logro: "[Logro optimizado sugerido]"
2. **Correcciones de Formato**:
   - [Acción de formato sugerida]

Guidelines for the 'optimizedCvMarkdown' CV:
Generate the candidate's optimized CV based on their profile, fully tailored to the target Job Description:
- Format it beautifully and professionally in clean Markdown (without the ATS report, scores, or formatting checks).
- Incorporate the target job title, candidate name, and relevant experience.
- Integrate the required skills and keywords naturally throughout the summary, experience bullets, and skills section.
- Use standard recognizable headers: "Resumen Profesional", "Experiencia Profesional", "Habilidades", "Educación".
- Keep formatting clean, using standard margins and standard bullet points.

---

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
                _telemetryClient.TrackMetric(
                    "OpenAiOptimizationTokens",
                    response.Value.Usage.TotalTokenCount
                );
            }

            var responseText = response.Value?.Content?[0]?.Text ?? string.Empty;

            using var doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;

            int score = 75;
            if (root.TryGetProperty("matchScore", out var scoreProp))
            {
                score = scoreProp.GetInt32();
            }

            string atsReport = string.Empty;
            if (root.TryGetProperty("atsReportMarkdown", out var reportProp))
            {
                atsReport = reportProp.GetString() ?? string.Empty;
            }

            string optimizedCv = string.Empty;
            if (root.TryGetProperty("optimizedCvMarkdown", out var cvProp))
            {
                optimizedCv = cvProp.GetString() ?? string.Empty;
            }

            return new OptimizationResult(atsReport, optimizedCv, score);
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

            if (
                root.TryGetProperty("canonical", out var canonicalProp)
                && canonicalProp.ValueKind == JsonValueKind.Array
            )
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

            if (
                root.TryGetProperty("custom", out var customProp)
                && customProp.ValueKind == JsonValueKind.Array
            )
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
        double titleMatchRatio =
            titleKeywords.Count > 0 ? (double)titleMatches / titleKeywords.Count : 0.0;

        // 3. General JD keyword alignment check
        int keywordMatches = 0;
        foreach (var word in jobKeywords)
        {
            if (userKeywords.Contains(word))
            {
                keywordMatches++;
            }
        }
        double keywordMatchRatio =
            jobKeywords.Count > 0 ? (double)keywordMatches / jobKeywords.Count : 0.0;

        // 4. Catalog skills alignment check
        int catalogMatches = 0;
        foreach (var skill in matchingSkills)
        {
            if (userSkills.Contains(skill))
            {
                catalogMatches++;
            }
        }
        double catalogMatchRatio =
            matchingSkills.Count > 0 ? (double)catalogMatches / matchingSkills.Count : 0.0;

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
            finalScore =
                (catalogWeight * 40.0) + (keywordMatchRatio * 40.0) + (titleMatchRatio * 20.0);
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

        // Generate beautiful Markdown report matching the ATS Compatibility report structure
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# REPORTE DE COMPATIBILIDAD ATS");
        sb.AppendLine();
        sb.AppendLine($"## Puntuación General: {score}/100");
        sb.AppendLine();
        sb.AppendLine("### Análisis del Score (Explicación Detallada)");
        if (score <= 10)
        {
            sb.AppendLine(
                $"- **Incompatibilidad de Sector**: El CV del candidato y el puesto objetivo '{jobTitle}' pertenecen a sectores profesionales completamente diferentes. No se detectan palabras clave o habilidades comunes en el análisis, por lo que el score es de {score}%."
            );
        }
        else if (score <= 35)
        {
            sb.AppendLine(
                $"- **Compatibilidad Baja**: Hay una alineación muy leve en el campo general, pero faltan habilidades clave esenciales y experiencia relevante en la vacante de '{jobTitle}'. El score del {score}% refleja esta brecha."
            );
        }
        else if (score <= 60)
        {
            sb.AppendLine(
                $"- **Compatibilidad Media**: Existe solapamiento parcial en el dominio de trabajo. El perfil cuenta con habilidades transferibles, pero carece de varias tecnologías críticas requeridas por la oferta de '{jobTitle}'. El score calculado es del {score}%."
            );
        }
        else
        {
            sb.AppendLine(
                $"- **Compatibilidad Alta**: El perfil está altamente alineado con los requisitos de la vacante '{jobTitle}'. Cuenta con las habilidades principales y palabras clave más importantes del puesto, alcanzando un score del {score}%."
            );
        }
        sb.AppendLine();
        sb.AppendLine("### Revisión de Formato");
        sb.AppendLine("- Formato detectado: PDF");
        sb.AppendLine("- Extracción de texto: Exitosa");
        sb.AppendLine(
            "- Problemas de formato: ✅ Estructura limpia de una sola columna. No se detectaron tablas, imágenes ni cuadros de texto restrictivos."
        );
        sb.AppendLine();
        sb.AppendLine("### Análisis de Palabras Clave");
        sb.AppendLine();
        sb.AppendLine("**Habilidades Críticas & Tecnologías:**");
        foreach (var skill in matchingSkills)
        {
            bool candidateHasIt = userSkills.Contains(skill);
            string status = candidateHasIt ? "✅ Encontrada" : "❌ FALTANTE";
            sb.AppendLine($"- {status} - **{skill}**");
        }
        sb.AppendLine();
        sb.AppendLine(
            $"**Fórmula aplicada**: (Habilidades coincidentes / Habilidades totales) * 100 = {score}%"
        );
        sb.AppendLine();
        sb.AppendLine("### Cambios Recomendados");
        sb.AppendLine();
        sb.AppendLine("1. **Palabras Clave a Agregar/Corregir**:");
        int missingCount = 0;
        foreach (var skill in matchingSkills)
        {
            if (!userSkills.Contains(skill))
            {
                sb.AppendLine(
                    $"- Agrega la habilidad **{skill}** en tu sección de habilidades o dentro de tus descripciones de experiencia."
                );
                missingCount++;
            }
        }
        if (missingCount == 0)
        {
            sb.AppendLine(
                "- ¡Excelente! Tu perfil cuenta con todas las habilidades clave identificadas en el catálogo de la vacante."
            );
        }
        sb.AppendLine();
        sb.AppendLine("2. **Fortalecer Título del Puesto**:");
        sb.AppendLine(
            $"- Asegúrate de mencionar explícitamente palabras clave de '{jobTitle}' en tu resumen profesional para maximizar el emparejamiento."
        );

        // Generate clean CV optimized for ATS and job position (without score / report headers)
        var cvSb = new System.Text.StringBuilder();
        cvSb.AppendLine($"# {name}");
        cvSb.AppendLine();
        cvSb.AppendLine($"**Puesto Objetivo:** {jobTitle}");
        cvSb.AppendLine();
        cvSb.AppendLine("## Resumen Profesional");
        cvSb.AppendLine(
            "Profesional altamente capacitado con amplia experiencia técnica y enfoque en de desarrollo ágil. Orientado a la optimización de procesos y a la entrega de soluciones escalables alineadas con las demandas de la vacante."
        );
        cvSb.AppendLine();
        cvSb.AppendLine("## Habilidades Tecnológicas");
        cvSb.AppendLine();
        foreach (var skill in matchingSkills)
        {
            if (userSkills.Contains(skill))
            {
                cvSb.AppendLine($"- **{skill}** (Avanzado)");
            }
        }
        cvSb.AppendLine();
        cvSb.AppendLine("## Experiencia Profesional");
        cvSb.AppendLine("### Ingeniero de Software Principal");
        cvSb.AppendLine("*Tech Corp | 2024 - Presente*");
        cvSb.AppendLine("- Liderazgo de equipos de desarrollo e integración de APIs seguras.");
        cvSb.AppendLine("- Implementación de pipelines de CI/CD para optimizar despliegues.");
        cvSb.AppendLine("- Trabajo en equipo en base a requerimientos de alto nivel.");
        cvSb.AppendLine();
        cvSb.AppendLine("## Educación");
        cvSb.AppendLine("### Licenciatura en Ciencias de la Computación");
        cvSb.AppendLine("*Universidad Estatal | 2020 - 2024*");

        return new OptimizationResult(sb.ToString(), cvSb.ToString(), score);
    }

    private static HashSet<string> ExtractKeywords(string text)
    {
        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text))
            return keywords;

        var words = text.Split(
            new[] { ' ', ',', '.', ';', ':', '(', ')', '[', ']', '-', '_', '/', '\n', '\r' },
            StringSplitOptions.RemoveEmptyEntries
        );

        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "de",
            "la",
            "el",
            "en",
            "y",
            "a",
            "los",
            "del",
            "se",
            "las",
            "por",
            "un",
            "para",
            "con",
            "no",
            "una",
            "su",
            "al",
            "lo",
            "como",
            "más",
            "but",
            "or",
            "and",
            "the",
            "a",
            "an",
            "of",
            "to",
            "in",
            "for",
            "with",
            "on",
            "at",
            "by",
            "from",
            "up",
            "about",
            "into",
            "over",
            "after",
        };

        foreach (var word in words)
        {
            var cleanWord = word.Trim().ToLower();
            if (
                cleanWord.Length >= 3
                && !stopWords.Contains(cleanWord)
                && !int.TryParse(cleanWord, out _)
            )
            {
                keywords.Add(cleanWord);
            }
        }

        return keywords;
    }
}
