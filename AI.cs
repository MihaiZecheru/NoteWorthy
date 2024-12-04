using System.Text;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace NoteWorthy;

internal class AI
{
    private static readonly string API_KEY = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? throw new InvalidOperationException("OPENAI_API_KEY environment variable is not set.");
    public static async Task<string> GetResponseAsync(string prompt)
    {
        using HttpClient client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", API_KEY);

        var requestBody = $@"
        {{
            ""model"": ""gpt-4"",
            ""messages"": [
            {{ ""role"": ""system"", ""content"": ""You are a helpful assistant that provides clear and concise answers, acting as a search engine. Use [yellow]text[/] to highlight important info. Make sure you close the markup with the closing tag: [/]"" }},
                {{ ""role"": ""user"", ""content"": ""{prompt}"" }}
            ],
            ""temperature"": 0.2
        }}";

        StringContent httpContent = new StringContent(requestBody, Encoding.UTF8, "application/json");

        HttpResponseMessage response = await client.PostAsync("https://api.openai.com/v1/chat/completions", httpContent);

        if (!response.IsSuccessStatusCode)
        {
            string errorContent = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"API request failed with status code {response.StatusCode}: {errorContent}");
        }

        string responseContent = await response.Content.ReadAsStringAsync();

        string pattern = "\"content\": \"(.*?)\",";
        Match match = Regex.Match(responseContent, pattern);

        if (match.Success)
        {
            return match.Groups[1].Value.Replace("\\n", "\n").Replace("\\\"", "\"").Trim();
        }
        else
        {
            throw new Exception("Unable to parse the API response.");
        }
    }
}
