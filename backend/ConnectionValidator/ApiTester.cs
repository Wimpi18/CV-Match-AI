using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace ConnectionValidator;

public class ApiTester
{
    private readonly HttpClient _client;
    private readonly string _baseUrl = "http://localhost:5008";

    public ApiTester()
    {
        _client = new HttpClient();
    }

    public async Task RunTestsAsync()
    {
        Console.WriteLine("\n==================================================");
        Console.WriteLine("        Starting API Upload Endpoint Tests       ");
        Console.WriteLine("==================================================");

        // Check if API is running first
        try
        {
            var ping = await _client.GetAsync($"{_baseUrl}/").ConfigureAwait(false);
            if (ping.StatusCode != HttpStatusCode.OK)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error: API returned status {ping.StatusCode}. Is it fully booted?");
                Console.ResetColor();
                return;
            }
        }
        catch (Exception)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: API is not running at {_baseUrl}. Please start the API first.");
            Console.ResetColor();
            return;
        }

        // Test 1: Verify Unauthorized Request
        await TestUnauthorizedUploadAsync().ConfigureAwait(false);

        // Test 2: Obtain Token
        string? token = await ObtainJwtTokenAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(token))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[FAILURE] Could not obtain JWT token. Skipping remaining tests.");
            Console.ResetColor();
            return;
        }

        // Test 3: Upload Valid PDF (< 2MB)
        await TestValidPdfUploadAsync(token).ConfigureAwait(false);

        // Test 4: Upload Invalid File Type (TXT)
        await TestInvalidMimeUploadAsync(token).ConfigureAwait(false);

        // Test 5: Upload Large PDF (> 2MB)
        await TestLargePdfUploadAsync(token).ConfigureAwait(false);

        Console.WriteLine("==================================================");
        Console.WriteLine("        API Upload Endpoint Tests Complete        ");
        Console.WriteLine("==================================================");
    }

    private async Task TestUnauthorizedUploadAsync()
    {
        Console.Write("Test 1: Requesting /api/upload without JWT... ");
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(new byte[100]);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        content.Add(fileContent, "file", "test.pdf");

        var response = await _client.PostAsync($"{_baseUrl}/api/upload", content).ConfigureAwait(false);
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

    private async Task<string?> ObtainJwtTokenAsync()
    {
        Console.Write("Test 2: Requesting test token from /api/auth/token... ");
        var response = await _client.PostAsync($"{_baseUrl}/api/auth/token", null).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var json = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
            if (json.TryGetProperty("token", out var tokenProp))
            {
                string token = tokenProp.GetString()!;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[SUCCESS] Token received.");
                Console.ResetColor();
                return token;
            }
        }
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[FAILURE] Returned {response.StatusCode}.");
        Console.ResetColor();
        return null;
    }

    private async Task TestValidPdfUploadAsync(string token)
    {
        Console.Write("Test 3: Uploading valid PDF (1KB) with JWT... ");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/upload");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(new byte[1024]); // 1KB
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        content.Add(fileContent, "file", "cv_base.pdf");
        request.Content = content;

        var response = await _client.SendAsync(request).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var json = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
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

    private async Task TestInvalidMimeUploadAsync(string token)
    {
        Console.Write("Test 4: Uploading non-PDF file (TXT) with JWT... ");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/upload");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(new byte[500]);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("text/plain");
        content.Add(fileContent, "file", "cv_base.txt");
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
        Console.Write("Test 5: Uploading large PDF (2.1MB) with JWT... ");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/upload");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var content = new MultipartFormDataContent();
        // 2.1 MB = 2.1 * 1024 * 1024 bytes
        var size = (int)(2.1 * 1024 * 1024);
        var fileContent = new ByteArrayContent(new byte[size]);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        content.Add(fileContent, "file", "huge_cv.pdf");
        request.Content = content;

        var response = await _client.SendAsync(request).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.RequestEntityTooLarge || response.StatusCode == (HttpStatusCode)413)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[SUCCESS] Rejected with 413 Payload Too Large.");
            Console.ResetColor();
        }
        else
        {
            string err = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FAILURE] Returned {response.StatusCode} instead of 413. Detail: {err}");
            Console.ResetColor();
        }
    }
}
