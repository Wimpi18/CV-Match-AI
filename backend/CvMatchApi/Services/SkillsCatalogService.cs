using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace CvMatchApi.Services;

/// <summary>
/// Service that connects to local legacy SQL Server to perform skills cross-referencing and canonical normalization.
/// </summary>
public class SkillsCatalogService : ISkillsCatalogService
{
    /// <inheritdoc />
    public async Task<SkillMatchResponse> MatchSkillsAsync(List<string> rawSkills)
    {
        if (rawSkills == null)
        {
            throw new ArgumentNullException(nameof(rawSkills));
        }

        var canonicalSkills = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var customSkills = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        List<SkillCatalogItem> catalog = new();
        bool connectedToSql = false;

        var connStr = Environment.GetEnvironmentVariable("LOCAL_SQL_CONNECTION_STRING");
        if (!string.IsNullOrEmpty(connStr))
        {
            try
            {
                // 1. Attempt connection and query local SQL Server (Docker)
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync().ConfigureAwait(false);
                using var cmd = new SqlCommand(
                    "SELECT CanonicalName, Category, Synonyms FROM Skills;",
                    conn
                );
                using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    catalog.Add(
                        new SkillCatalogItem(
                            reader.GetString(0),
                            reader.GetString(1),
                            reader.GetString(2)
                        )
                    );
                }
                connectedToSql = true;
            }
            catch (Exception ex)
            {
                // Fallback gracefully if Docker/Local SQL is offline
                Console.WriteLine(
                    $"[WARNING] Local SQL Server connection failed. Using in-memory fallback. Detail: {ex.Message}"
                );
            }
        }

        if (!connectedToSql)
        {
            // 2. Load fallback list with identical seed data from db/init.sql
            catalog = GetFallbackCatalog();
        }

        // 3. Normalization algorithm
        foreach (var raw in rawSkills)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string cleanedRaw = raw.Trim();
            bool matched = false;

            foreach (var item in catalog)
            {
                // Direct match on Canonical name
                if (
                    string.Equals(
                        item.CanonicalName,
                        cleanedRaw,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    canonicalSkills.Add(item.CanonicalName);
                    matched = true;
                    break;
                }

                // Check Synonyms list (comma-separated values)
                var synonyms = item.Synonyms.Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                );
                foreach (var syn in synonyms)
                {
                    if (string.Equals(syn, cleanedRaw, StringComparison.OrdinalIgnoreCase))
                    {
                        canonicalSkills.Add(item.CanonicalName);
                        matched = true;
                        break;
                    }
                }

                if (matched)
                {
                    break;
                }
            }

            // 4. Classify as custom skill if not found in catalog
            if (!matched)
            {
                customSkills.Add(cleanedRaw);
            }
        }

        return new SkillMatchResponse(
            new List<string>(canonicalSkills),
            new List<string>(customSkills)
        );
    }

    private static List<SkillCatalogItem> GetFallbackCatalog()
    {
        return new List<SkillCatalogItem>
        {
            new("JavaScript", "Frontend", "js, javascript, es6, es7"),
            new("TypeScript", "Frontend", "ts, typescript"),
            new("Angular", "Frontend", "angular, angularjs, ng, angular19, angular20"),
            new("React", "Frontend", "react, reactjs, react.js, nextjs, next.js"),
            new("Vue.js", "Frontend", "vue, vuejs, vue.js, nuxt"),
            new("C#", "Backend", "c#, csharp, .net, dotnet, asp.net, dotnet core"),
            new("Python", "Backend", "python, py, django, flask, fastapi"),
            new("Node.js", "Backend", "node, nodejs, node.js, express, nestjs"),
            new("SQL Server", "Database", "sql server, mssql, t-sql, microsoft sql server"),
            new("Azure SQL", "Database", "azure sql, azure sql database"),
            new("Cosmos DB", "Database", "cosmos, cosmosdb, cosmos db, nosql"),
            new("Docker", "DevOps", "docker, container, containers"),
            new("Kubernetes", "DevOps", "k8s, kubernetes, helm"),
            new("AWS", "Cloud", "aws, amazon web services, s3, ec2, lambda"),
            new("Azure", "Cloud", "azure, microsoft azure, app services, functions"),
        };
    }

    private record SkillCatalogItem(string CanonicalName, string Category, string Synonyms);
}
