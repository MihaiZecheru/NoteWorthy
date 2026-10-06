using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NoteWorthy;

internal class AI
{
    private static readonly string API_KEY = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
        ?? throw new InvalidOperationException("GEMINI_API_KEY environment variable is not set.");

    private const string Model = "gemini-3.1-flash-lite";

    public static async Task<string> GetResponseAsync(string prompt)
    {
        using HttpClient client = new HttpClient();

        string url = $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent?key={API_KEY}";

        var payload = new
        {
            system_instruction = new
            {
                parts = new[]
                {
                    new { text = "You are a helpful assistant that provides clear and concise answers, acting as a search engine. Use [yellow]text[/] to highlight important info. Make sure you close the markup with the closing tag: [/] ONLY use square brackets if it's for markup; if a square bracket is necessary, use a curly brace instead NO MATTER WHAT." }
                }
            },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[]
                    {
                        new { text = prompt }
                    }
                }
            }
        };

        string json = JsonSerializer.Serialize(payload);
        using StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

        HttpResponseMessage response = await client.PostAsync(url, content);

        if (!response.IsSuccessStatusCode)
        {
            string errorContent = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"API request failed with status code {response.StatusCode}: {errorContent}");
        }

        string responseContent = await response.Content.ReadAsStringAsync();

        JsonNode? doc = JsonNode.Parse(responseContent);
        string? text = doc?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.GetValue<string>();

        return text?.Trim() ?? throw new Exception("Unable to parse text from Gemini response.");
    }
}