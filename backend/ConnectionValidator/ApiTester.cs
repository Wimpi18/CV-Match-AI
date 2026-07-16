using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Data.SqlClient;

namespace ConnectionValidator;

public class ApiTester
{
    private readonly HttpClient _client;
    private readonly string _baseUrl = "http://localhost:5008";

    public ApiTester()
    {
        // Disable auto-redirect so we can inspect Google OAuth redirection responses
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        _client = new HttpClient(handler);
    }

    public async Task RunTestsAsync()
    {
        Console.WriteLine("\n==================================================");
        Console.WriteLine("        Starting API Authentication & Upload Tests ");
        Console.WriteLine("==================================================");

        // Check if API is running first
        try
        {
            var ping = await _client.GetAsync($"{_baseUrl}/").ConfigureAwait(false);
            if (ping.StatusCode != HttpStatusCode.OK)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(
                    $"Error: API returned status {ping.StatusCode}. Is it fully booted?"
                );
                Console.ResetColor();
                return;
            }
        }
        catch (Exception)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(
                $"Error: API is not running at {_baseUrl}. Please start the API first."
            );
            Console.ResetColor();
            return;
        }

        // Test 1: Verify Google OAuth Redirect
        await TestGoogleRedirectAsync().ConfigureAwait(false);

        // Test 2: Verify Google Callback, DB registration, and JWT creation
        string? oauthToken = await TestGoogleCallbackAndRegistrationAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(oauthToken))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[FAILURE] Google OAuth flow failed. Skipping authorization tests.");
            Console.ResetColor();
            return;
        }

        // Test 3: Verify JWT Session Validity and Expiration (Exactly 24 hours)
        await VerifyJwtExpirationAsync(oauthToken).ConfigureAwait(false);

        // Test 4: Verify User table registration in Azure SQL
        await VerifyUserInDatabaseAsync().ConfigureAwait(false);

        // Test 5: Verify upload with Google OAuth JWT token
        await TestValidPdfUploadAsync(oauthToken).ConfigureAwait(false);

        // Test 6: Verify upload fails with invalid JWT
        await TestUnauthorizedUploadAsync().ConfigureAwait(false);

        // Test 7: Verify upload fails with invalid MIME type (TXT)
        await TestInvalidMimeUploadAsync(oauthToken).ConfigureAwait(false);

        // Test 8: Verify upload fails with large file size (> 2MB)
        await TestLargePdfUploadAsync(oauthToken).ConfigureAwait(false);

        // --- NEW TESTS FOR CREDIT CONTROL MIDDLEWARE ---
        Console.WriteLine("\n--- Testing CV Optimization & Credit Control Middleware ---");

        // Clear usage logs from previous runs for a deterministic test
        await ClearUsageLogsAsync().ConfigureAwait(false);

        // Test 9: Verify optimize without JWT returns 401 Unauthorized
        await TestUnauthorizedOptimizeAsync().ConfigureAwait(false);

        // Test 10-12: Execute 3 successful optimizations (200 OK)
        await TestOptimizeCvAsync(oauthToken, 1).ConfigureAwait(false);
        await TestOptimizeCvAsync(oauthToken, 2).ConfigureAwait(false);
        await TestOptimizeCvAsync(oauthToken, 3).ConfigureAwait(false);

        // Test 13: Execute 4th optimization and expect 429 Too Many Requests
        await TestExceededCreditsOptimizeAsync(oauthToken).ConfigureAwait(false);

        // Test 14: Verify 3 usage logs are registered in Azure SQL DB
        await VerifyUsageLogCountInDatabaseAsync(3).ConfigureAwait(false);

        // --- NEW TESTS FOR SKILLS CATALOG MATCHING ---
        Console.WriteLine("\n--- Testing Skills Catalog Crossing with SQL Server ---");

        // Test 15: Verify match without JWT returns 401 Unauthorized
        await TestUnauthorizedSkillsMatchAsync().ConfigureAwait(false);

        // Test 16: Verify matching standardizes synonyms and custom classification
        await TestSkillsMatchCrossingAsync(oauthToken).ConfigureAwait(false);

        // Test 17: Verify matching removes duplicate canonical results
        await TestSkillsMatchDuplicatesAsync(oauthToken).ConfigureAwait(false);

        // --- NEW TESTS FOR PROFILE STRUCTURING AND COSMOS DB ---
        Console.WriteLine("\n--- Testing Profile Structuring with OpenAI & Cosmos DB ---");

        // Test 18: Verify process profile without JWT returns 401 Unauthorized
        await TestUnauthorizedProfileProcessAsync().ConfigureAwait(false);

        // Test 19: Verify profile structuring and storage returning 200 OK
        await TestProfileProcessAndStorageAsync(oauthToken).ConfigureAwait(false);

        // Test 20: Connect to Cosmos DB and verify profile document
        await VerifyProfileInCosmosDbAsync().ConfigureAwait(false);

        // Test 21: Succeeding upload overwrites previous active profile
        await TestProfileOverwriteAsync(oauthToken).ConfigureAwait(false);

        Console.WriteLine("==================================================");
        Console.WriteLine("        API Authentication & Upload Tests Complete ");
        Console.WriteLine("==================================================");
    }

    private async Task TestGoogleRedirectAsync()
    {
        Console.Write("Test 1: GET /api/auth/login (Google Consent redirect)... ");
        var response = await _client.GetAsync($"{_baseUrl}/api/auth/login").ConfigureAwait(false);
        if (
            response.StatusCode == HttpStatusCode.Redirect
            || response.StatusCode == HttpStatusCode.Found
        )
        {
            var location = response.Headers.Location?.ToString();
            if (location != null && location.Contains("accounts.google.com"))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[SUCCESS] Redirected to Google Consent screen.");
                Console.ResetColor();
                return;
            }
        }
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[FAILURE] Returned {response.StatusCode} or Location is incorrect.");
        Console.ResetColor();
    }

    private async Task<string?> TestGoogleCallbackAndRegistrationAsync()
    {
        Console.Write("Test 2: GET /api/auth/callback?code=test-google-code... ");
        var response = await _client
            .GetAsync($"{_baseUrl}/api/auth/callback?code=test-google-code")
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var json = await response
                .Content.ReadFromJsonAsync<JsonElement>()
                .ConfigureAwait(false);
            if (json.TryGetProperty("token", out var tokenProp))
            {
                string token = tokenProp.GetString()!;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[SUCCESS] User registered & signed JWT session received.");
                Console.ResetColor();
                return token;
            }
        }
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[FAILURE] Returned {response.StatusCode}.");
        Console.ResetColor();
        return null;
    }

    private async Task VerifyJwtExpirationAsync(string token)
    {
        Console.Write("Test 3: Checking JWT claims and 24-hour expiration... ");
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3)
            {
                throw new ArgumentException("Invalid JWT format.");
            }

            string payloadBase64 = parts[1];
            payloadBase64 = payloadBase64.PadRight(
                payloadBase64.Length + (4 - payloadBase64.Length % 4) % 4,
                '='
            );
            byte[] payloadBytes = Convert.FromBase64String(payloadBase64);
            string payloadJson = Encoding.UTF8.GetString(payloadBytes);

            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("exp", out var expProp))
            {
                long expSeconds = expProp.GetInt64();
                var expDateTime = DateTimeOffset.FromUnixTimeSeconds(expSeconds).UtcDateTime;
                double hoursLeft = (expDateTime - DateTime.UtcNow).TotalHours;

                if (hoursLeft > 23.9 && hoursLeft <= 24.0)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine(
                        $"[SUCCESS] Expires in {hoursLeft:F2} hours (Correct 24h lifespan)."
                    );
                    Console.ResetColor();
                    return;
                }
                else
                {
                    throw new Exception($"Lifespan is {hoursLeft:F2} hours instead of 24.");
                }
            }
            throw new Exception("Claim 'exp' not found.");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FAILURE] JWT check failed: {ex.Message}");
            Console.ResetColor();
        }
    }

    private async Task VerifyUserInDatabaseAsync()
    {
        Console.Write("Test 4: Verifying user record in Azure SQL 'Users' table... ");
        string? connStr = Environment.GetEnvironmentVariable("AZURE_SQL_CONNECTION_STRING");
        if (string.IsNullOrEmpty(connStr))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(
                "[FAILURE] Azure SQL Connection string environment variable is missing."
            );
            Console.ResetColor();
            return;
        }

        try
        {
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync().ConfigureAwait(false);
            using var cmd = new SqlCommand(
                "SELECT Name, RegisteredAt FROM Users WHERE Email = 'test-google-oauth@example.com';",
                conn
            );
            using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            if (await reader.ReadAsync().ConfigureAwait(false))
            {
                string name = reader.GetString(0);
                DateTime regTime = reader.GetDateTime(1);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[SUCCESS] User '{name}' found. RegisteredAt: {regTime} (UTC).");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[FAILURE] User record not found in database.");
                Console.ResetColor();
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FAILURE] Database check failed: {ex.Message}");
            Console.ResetColor();
        }
    }

    private async Task TestValidPdfUploadAsync(string token)
    {
        Console.Write("Test 5: Uploading valid PDF (1KB) with Google JWT... ");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/upload");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(new byte[1024]);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        content.Add(fileContent, "file", "google_cv_upload.pdf");
        request.Content = content;

        var response = await _client.SendAsync(request).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var json = await response
                .Content.ReadFromJsonAsync<JsonElement>()
                .ConfigureAwait(false);
            if (json.TryGetProperty("fileId", out var fileIdProp))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[SUCCESS] 200 OK. Unique file ID: {fileIdProp.GetString()}");
                Console.ResetColor();
                return;
            }
        }

        string err = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[FAILURE] Status: {response.StatusCode}, Detail: {err}");
        Console.ResetColor();
    }

    private async Task TestUnauthorizedUploadAsync()
    {
        Console.Write("Test 6: Requesting /api/upload with invalid JWT... ");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/upload");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            "invalid-token-string"
        );

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(new byte[100]);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        content.Add(fileContent, "file", "test.pdf");
        request.Content = content;

        var response = await _client.SendAsync(request).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[SUCCESS] Rejected with 401 Unauthorized.");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FAILURE] Returned {response.StatusCode} instead of 401.");
            Console.ResetColor();
        }
    }

    private async Task TestInvalidMimeUploadAsync(string token)
    {
        Console.Write("Test 7: Uploading non-PDF file (TXT) with Google JWT... ");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/upload");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(new byte[500]);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("text/plain");
        content.Add(fileContent, "file", "cv.txt");
        request.Content = content;

        var response = await _client.SendAsync(request).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[SUCCESS] Rejected with 400 Bad Request.");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FAILURE] Returned {response.StatusCode} instead of 400.");
            Console.ResetColor();
        }
    }

    private async Task TestLargePdfUploadAsync(string token)
    {
        Console.Write("Test 8: Uploading large PDF (2.1MB) with Google JWT... ");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/upload");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var content = new MultipartFormDataContent();
        var size = (int)(2.1 * 1024 * 1024);
        var fileContent = new ByteArrayContent(new byte[size]);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        content.Add(fileContent, "file", "large_cv.pdf");
        request.Content = content;

        var response = await _client.SendAsync(request).ConfigureAwait(false);
        if (
            response.StatusCode == HttpStatusCode.RequestEntityTooLarge
            || response.StatusCode == (HttpStatusCode)413
        )
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[SUCCESS] Rejected with 413 Payload Too Large.");
            Console.ResetColor();
        }
        else
        {
            string err = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(
                $"[FAILURE] Returned {response.StatusCode} instead of 413. Detail: {err}"
            );
            Console.ResetColor();
        }
    }

    private async Task ClearUsageLogsAsync()
    {
        string? connStr = Environment.GetEnvironmentVariable("AZURE_SQL_CONNECTION_STRING");
        if (string.IsNullOrEmpty(connStr))
            return;

        try
        {
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync().ConfigureAwait(false);

            // Delete previous usage logs of the test user
            string sql =
                "DELETE FROM UsageLogs WHERE UserId IN (SELECT Id FROM Users WHERE Email = 'test-google-oauth@example.com');";
            using var cmd = new SqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARNING] Could not clear previous usage logs: {ex.Message}");
        }
    }

    private async Task TestUnauthorizedOptimizeAsync()
    {
        Console.Write("Test 9: POST /api/cv/optimize without JWT... ");
        var body = new { JobTitle = "Software Engineer", JobDescription = "C# and SQL" };
        var response = await _client
            .PostAsJsonAsync($"{_baseUrl}/api/cv/optimize", body)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[SUCCESS] Rejected with 401 Unauthorized.");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FAILURE] Returned {response.StatusCode} instead of 401.");
            Console.ResetColor();
        }
    }

    private async Task TestOptimizeCvAsync(string token, int runNumber)
    {
        Console.Write(
            $"Test {9 + runNumber}: POST /api/cv/optimize (Request #{runNumber}) with JWT... "
        );
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/cv/optimize");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var body = new
        {
            JobTitle = $"Fullstack Dev #{runNumber}",
            JobDescription = "React and .NET",
        };
        request.Content = JsonContent.Create(body);

        var response = await _client.SendAsync(request).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var json = await response
                .Content.ReadFromJsonAsync<JsonElement>()
                .ConfigureAwait(false);
            if (json.TryGetProperty("logId", out _))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[SUCCESS] 200 OK. Usage log created.");
                Console.ResetColor();
                return;
            }
        }

        string err = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[FAILURE] Status: {response.StatusCode}, Detail: {err}");
        Console.ResetColor();
    }

    private async Task TestExceededCreditsOptimizeAsync(string token)
    {
        Console.Write("Test 13: POST /api/cv/optimize (Request #4 - Exceed limit) with JWT... ");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/cv/optimize");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var body = new { JobTitle = "Cloud Architect", JobDescription = "Azure and Terraform" };
        request.Content = JsonContent.Create(body);

        var response = await _client.SendAsync(request).ConfigureAwait(false);
        if (
            response.StatusCode == HttpStatusCode.TooManyRequests
            || response.StatusCode == (HttpStatusCode)429
        )
        {
            var json = await response
                .Content.ReadFromJsonAsync<JsonElement>()
                .ConfigureAwait(false);
            if (
                json.TryGetProperty("message", out var msgProp)
                || json.TryGetProperty("Message", out msgProp)
            )
            {
                string msg = msgProp.GetString()!;
                if (msg == "Límite de generación gratuito alcanzado (Máximo 3 CVs)")
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine(
                        "[SUCCESS] Rejected with 429 Too Many Requests and expected error message."
                    );
                    Console.ResetColor();
                    return;
                }
                else
                {
                    throw new Exception($"Message was '{msg}' instead of expected warning.");
                }
            }
        }

        string err = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[FAILURE] Status: {response.StatusCode}, Detail: {err}");
        Console.ResetColor();
    }

    private async Task VerifyUsageLogCountInDatabaseAsync(int expectedCount)
    {
        Console.Write(
            $"Test 14: Verifying exactly {expectedCount} logs exist in Azure SQL 'UsageLogs' table... "
        );
        string? connStr = Environment.GetEnvironmentVariable("AZURE_SQL_CONNECTION_STRING");
        if (string.IsNullOrEmpty(connStr))
            return;

        try
        {
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync().ConfigureAwait(false);
            string sql =
                "SELECT COUNT(*) FROM UsageLogs WHERE UserId IN (SELECT Id FROM Users WHERE Email = 'test-google-oauth@example.com');";
            using var cmd = new SqlCommand(sql, conn);
            int count = (int)(await cmd.ExecuteScalarAsync().ConfigureAwait(false) ?? 0);

            if (count == expectedCount)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[SUCCESS] Found exactly {count} usage records.");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[FAILURE] Found {count} records instead of {expectedCount}.");
                Console.ResetColor();
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FAILURE] Database check failed: {ex.Message}");
            Console.ResetColor();
        }
    }

    private async Task TestUnauthorizedSkillsMatchAsync()
    {
        Console.Write("Test 15: POST /api/skills/match without JWT... ");
        var body = new { RawSkills = new[] { "ReactJS" } };
        var response = await _client
            .PostAsJsonAsync($"{_baseUrl}/api/skills/match", body)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[SUCCESS] Rejected with 401 Unauthorized.");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FAILURE] Returned {response.StatusCode} instead of 401.");
            Console.ResetColor();
        }
    }

    private async Task TestSkillsMatchCrossingAsync(string token)
    {
        Console.Write("Test 16: POST /api/skills/match (ReactJS, JS, docker, Flutter)... ");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/skills/match");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var body = new { RawSkills = new[] { "ReactJS", "JS", "docker", "Flutter" } };
        request.Content = JsonContent.Create(body);

        var response = await _client.SendAsync(request).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var json = await response
                .Content.ReadFromJsonAsync<JsonElement>()
                .ConfigureAwait(false);
            if (
                json.TryGetProperty("canonicalSkills", out var canonProp)
                && json.TryGetProperty("customSkills", out var custProp)
            )
            {
                var canonicalList = JsonSerializer.Deserialize<List<string>>(
                    canonProp.GetRawText()
                )!;
                var customList = JsonSerializer.Deserialize<List<string>>(custProp.GetRawText())!;

                bool matchCanon =
                    canonicalList.Contains("React")
                    && canonicalList.Contains("JavaScript")
                    && canonicalList.Contains("Docker");
                bool matchCust = customList.Contains("Flutter");

                if (matchCanon && matchCust && canonicalList.Count == 3 && customList.Count == 1)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine(
                        "[SUCCESS] Standardized ReactJS, JS, docker -> React, JavaScript, Docker, and Flutter -> custom."
                    );
                    Console.ResetColor();
                    return;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine(
                        $"[FAILURE] Items mismatch. Canonical: {string.Join(',', canonicalList)}, Custom: {string.Join(',', customList)}"
                    );
                    Console.ResetColor();
                    return;
                }
            }
        }

        string err = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[FAILURE] Status: {response.StatusCode}, Detail: {err}");
        Console.ResetColor();
    }

    private async Task TestSkillsMatchDuplicatesAsync(string token)
    {
        Console.Write("Test 17: POST /api/skills/match (ReactJS, react.js - duplicates check)... ");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/skills/match");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var body = new { RawSkills = new[] { "ReactJS", "react.js" } };
        request.Content = JsonContent.Create(body);

        var response = await _client.SendAsync(request).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var json = await response
                .Content.ReadFromJsonAsync<JsonElement>()
                .ConfigureAwait(false);
            if (json.TryGetProperty("canonicalSkills", out var canonProp))
            {
                var canonicalList = JsonSerializer.Deserialize<List<string>>(
                    canonProp.GetRawText()
                )!;

                if (canonicalList.Count == 1 && canonicalList.Contains("React"))
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine(
                        "[SUCCESS] Deduplicated 'ReactJS' and 'react.js' to single 'React'."
                    );
                    Console.ResetColor();
                    return;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine(
                        $"[FAILURE] Duplicate entries returned: {string.Join(',', canonicalList)}"
                    );
                    Console.ResetColor();
                    return;
                }
            }
        }

        string err = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[FAILURE] Status: {response.StatusCode}, Detail: {err}");
        Console.ResetColor();
    }

    private async Task TestUnauthorizedProfileProcessAsync()
    {
        Console.Write("Test 18: POST /api/profile/process without JWT... ");
        var body = new
        {
            CvText = "Test CV Text",
            CanonicalSkills = new[] { "React" },
            CustomSkills = new[] { "Flutter" },
        };
        var response = await _client
            .PostAsJsonAsync($"{_baseUrl}/api/profile/process", body)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[SUCCESS] Rejected with 401 Unauthorized.");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FAILURE] Returned {response.StatusCode} instead of 401.");
            Console.ResetColor();
        }
    }

    private async Task TestProfileProcessAndStorageAsync(string token)
    {
        Console.Write("Test 19: POST /api/profile/process (Valid CV structure) with JWT... ");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_baseUrl}/api/profile/process"
        );
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var body = new
        {
            CvText = "Google Test User. Phone: +123456. Location: Seattle. Work: Tech Corp, Software Engineer since 2024.",
            CanonicalSkills = new List<string> { "React", "JavaScript", "Docker" },
            CustomSkills = new List<string> { "Flutter" },
        };
        request.Content = JsonContent.Create(body);

        var response = await _client.SendAsync(request).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var json = await response
                .Content.ReadFromJsonAsync<JsonElement>()
                .ConfigureAwait(false);
            if (json.TryGetProperty("profile", out var profProp))
            {
                if (
                    profProp.TryGetProperty("id", out var idProp)
                    && idProp.GetString() == "test-google-oauth@example.com"
                )
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("[SUCCESS] Profile processed successfully and returned.");
                    Console.ResetColor();
                    return;
                }
            }
        }

        string err = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[FAILURE] Status: {response.StatusCode}, Detail: {err}");
        Console.ResetColor();
    }

    private async Task VerifyProfileInCosmosDbAsync()
    {
        Console.Write("Test 20: Connect to Cosmos DB and verify profile document... ");
        var connectionString = Environment.GetEnvironmentVariable("COSMOS_CONNECTION_STRING");
        if (string.IsNullOrEmpty(connectionString))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[FAILURE] COSMOS_CONNECTION_STRING is missing in env.");
            Console.ResetColor();
            return;
        }

        try
        {
            using var cosmosClient = new CosmosClient(connectionString);
            var db = cosmosClient.GetDatabase("cvmatch-store");
            var container = db.GetContainer("resumes");

            var email = "test-google-oauth@example.com";
            var response = await container
                .ReadItemAsync<JsonElement>(email, new PartitionKey(email))
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                var doc = response.Resource;
                if (doc.TryGetProperty("userId", out var userProp) && userProp.GetString() == email)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine(
                        "[SUCCESS] Document found in Cosmos DB resumes container with correct keys."
                    );
                    Console.ResetColor();
                    return;
                }
            }

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FAILURE] Cosmos returned status {response.StatusCode}.");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FAILURE] Cosmos DB read failed: {ex.Message}");
            Console.ResetColor();
        }
    }

    private async Task TestProfileOverwriteAsync(string token)
    {
        Console.Write("Test 21: Succeeding upload overwrites previous active profile... ");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_baseUrl}/api/profile/process"
        );
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var body = new
        {
            CvText = "Google Test User. Updated Profile. Phone: +123456.",
            CanonicalSkills = new List<string> { "React" },
            CustomSkills = new List<string>(),
        };
        request.Content = JsonContent.Create(body);

        var response = await _client.SendAsync(request).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.OK)
        {
            // Verify in Cosmos DB that text was updated
            var connectionString = Environment.GetEnvironmentVariable("COSMOS_CONNECTION_STRING");
            if (!string.IsNullOrEmpty(connectionString))
            {
                try
                {
                    using var cosmosClient = new CosmosClient(connectionString);
                    var container = cosmosClient.GetContainer("cvmatch-store", "resumes");
                    var email = "test-google-oauth@example.com";
                    var cosmosResp = await container
                        .ReadItemAsync<JsonElement>(email, new PartitionKey(email))
                        .ConfigureAwait(false);
                    if (cosmosResp.StatusCode == HttpStatusCode.OK)
                    {
                        var doc = cosmosResp.Resource;
                        if (
                            doc.TryGetProperty("rawText", out var textProp)
                            && textProp.GetString() == body.CvText
                        )
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine(
                                "[SUCCESS] Document successfully updated/overwritten in Cosmos DB resumes container."
                            );
                            Console.ResetColor();
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[FAILURE] Overwrite verify failed: {ex.Message}");
                    Console.ResetColor();
                    return;
                }
            }
        }

        string err = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[FAILURE] Status: {response.StatusCode}, Detail: {err}");
        Console.ResetColor();
    }
}
