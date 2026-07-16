using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Data.SqlClient;

namespace ConnectionValidator;

public static class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("==================================================");
        Console.WriteLine("        CV-Match-AI DB Connection Validator       ");
        Console.WriteLine("==================================================");

        // 1. Load env variables from .env file
        string envPath = FindEnvFile(Directory.GetCurrentDirectory());
        if (string.IsNullOrEmpty(envPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(
                "Error: .env file not found in current directory or parent directories."
            );
            Console.ResetColor();
            return;
        }

        Console.WriteLine($"Loading configuration from: {envPath}");
        LoadEnvFile(envPath);

        string? localSqlConn = Environment.GetEnvironmentVariable("LOCAL_SQL_CONNECTION_STRING");
        string? azureSqlConn = Environment.GetEnvironmentVariable("AZURE_SQL_CONNECTION_STRING");
        string? cosmosConn = Environment.GetEnvironmentVariable("COSMOS_CONNECTION_STRING");
        string? cosmosDbName = Environment.GetEnvironmentVariable("COSMOS_DATABASE_NAME");
        string? cosmosContainerName = Environment.GetEnvironmentVariable("COSMOS_CONTAINER_NAME");

        // 2. Validate environment variables presence
        bool envsMissing = false;
        if (string.IsNullOrWhiteSpace(localSqlConn))
        {
            Console.WriteLine("Missing: LOCAL_SQL_CONNECTION_STRING");
            envsMissing = true;
        }
        if (string.IsNullOrWhiteSpace(azureSqlConn))
        {
            Console.WriteLine("Missing: AZURE_SQL_CONNECTION_STRING");
            envsMissing = true;
        }
        if (string.IsNullOrWhiteSpace(cosmosConn))
        {
            Console.WriteLine("Missing: COSMOS_CONNECTION_STRING");
            envsMissing = true;
        }
        if (string.IsNullOrWhiteSpace(cosmosDbName))
        {
            Console.WriteLine("Missing: COSMOS_DATABASE_NAME");
            envsMissing = true;
        }
        if (string.IsNullOrWhiteSpace(cosmosContainerName))
        {
            Console.WriteLine("Missing: COSMOS_CONTAINER_NAME");
            envsMissing = true;
        }

        if (envsMissing)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\nError: One or more connection environment variables are missing.");
            Console.ResetColor();
            return;
        }

        Console.WriteLine("Environment variables successfully loaded!\n");

        // 3. Test Local SQL Server (Docker)
        Console.WriteLine("--- Testing Local SQL Server (Docker) ---");
        try
        {
            using var conn = new SqlConnection(localSqlConn);
            await conn.OpenAsync().ConfigureAwait(false);
            using var cmd = new SqlCommand("SELECT COUNT(*) FROM Skills;", conn);
            int count = (int)(await cmd.ExecuteScalarAsync().ConfigureAwait(false) ?? 0);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(
                $"[SUCCESS] Connected to Local SQL Server. Seeded skills count: {count}"
            );
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(
                $"[WARNING/OFFLINE] Local SQL Server could not be reached. (Check if Docker container is running)"
            );
            Console.WriteLine($"Detail: {ex.Message}");
            Console.ResetColor();
        }

        Console.WriteLine();

        // 4. Test Azure SQL Database
        Console.WriteLine("--- Testing Azure SQL Database ---");
        try
        {
            using var conn = new SqlConnection(azureSqlConn);
            await conn.OpenAsync().ConfigureAwait(false);
            using var cmd = new SqlCommand("SELECT DB_NAME();", conn);
            string? dbName = await cmd.ExecuteScalarAsync().ConfigureAwait(false) as string;

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(
                $"[SUCCESS] Connected to Azure SQL Database. Database name: {dbName}"
            );
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FAILURE] Connected to Azure SQL Database failed.");
            Console.WriteLine($"Detail: {ex.Message}");
            Console.ResetColor();
        }

        Console.WriteLine();

        // 5. Test Azure Cosmos DB Serverless
        Console.WriteLine("--- Testing Azure Cosmos DB ---");
        try
        {
            using var cosmosClient = new CosmosClient(cosmosConn);
            var database = cosmosClient.GetDatabase(cosmosDbName);
            var container = database.GetContainer(cosmosContainerName);

            // Perform a read on the container properties to verify existence
            var containerProps = await container.ReadContainerAsync().ConfigureAwait(false);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[SUCCESS] Connected to Azure Cosmos DB Serverless.");
            Console.WriteLine($"Database ID: {database.Id}");
            Console.WriteLine($"Container ID: {containerProps.Resource.Id}");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FAILURE] Connected to Azure Cosmos DB failed.");
            Console.WriteLine($"Detail: {ex.Message}");
            Console.ResetColor();
        }

        Console.WriteLine("\n==================================================");
        Console.WriteLine("        DB Connection Validation Complete         ");
        Console.WriteLine("==================================================");
    }

    private static string? FindEnvFile(string startDir)
    {
        string? current = startDir;
        while (!string.IsNullOrEmpty(current))
        {
            string testPath = Path.Combine(current, ".env");
            if (File.Exists(testPath))
            {
                return testPath;
            }
            current = Path.GetDirectoryName(current);
        }
        return null;
    }

    private static void LoadEnvFile(string path)
    {
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                continue;

            int idx = line.IndexOf('=');
            if (idx <= 0)
                continue;

            string key = line.Substring(0, idx).Trim();
            string val = line.Substring(idx + 1).Trim();

            // Strip surrounding quotes if present
            if (
                (val.StartsWith('"') && val.EndsWith('"'))
                || (val.StartsWith('\'') && val.EndsWith('\''))
            )
            {
                val = val.Substring(1, val.Length - 2);
            }

            Environment.SetEnvironmentVariable(key, val);
        }
    }
}
