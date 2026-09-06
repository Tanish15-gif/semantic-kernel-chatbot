using System.ComponentModel;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.SemanticKernel;

namespace Ai.Plugins
{
    /// <summary>
    /// Searches the web using DuckDuckGo with session cookies and fallback to Wikipedia.
    /// </summary>
    public class WebSearchPlugin
    {
        private readonly HttpClient _client;

        public WebSearchPlugin(HttpClient client)
        {
            _client = client;
        }

        [KernelFunction]
        [Description("Search the web for real-time information, news, current events, documentation, or facts.")]
        public async Task<string> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return "No search query provided.";

            // 1. Primary Engine: DuckDuckGo HTML Search
            try
            {
                var ddgResults = await SearchDuckDuckGoAsync(query);
                if (!string.IsNullOrWhiteSpace(ddgResults))
                    return ddgResults;
            }
            catch { /* Fall through to secondary engine */ }

            // 2. Secondary Engine: Wikipedia Knowledge Search
            try
            {
                var wikiResults = await SearchWikipediaAsync(query);
                if (!string.IsNullOrWhiteSpace(wikiResults))
                    return wikiResults;
            }
            catch { /* Fall through */ }

            return $"No search results found for: {query}";
        }

        private async Task<string> SearchDuckDuckGoAsync(string query)
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("q", query)
            });

            using var response = await _client.PostAsync("https://html.duckduckgo.com/html/", content);
            if (!response.IsSuccessStatusCode)
                return string.Empty;

            var html = await response.Content.ReadAsStringAsync();

            var titleMatches = Regex.Matches(
                html,
                @"<a\b(?=[^>]*class=['""]result__a['""])(?=[^>]*href=['""]([^'""]+)['""])[^>]*>(.*?)</a>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline
            );

            if (titleMatches.Count == 0)
                return string.Empty;

            var snippetMatches = Regex.Matches(
                html,
                @"<a\b(?=[^>]*class=['""]result__snippet['""])(?=[^>]*href=['""]([^'""]+)['""])[^>]*>(.*?)</a>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline
            );

            var results = new List<string>();
            int count = Math.Min(4, titleMatches.Count);

            for (int i = 0; i < count; i++)
            {
                string title = WebUtility.HtmlDecode(Regex.Replace(titleMatches[i].Groups[2].Value, @"<[^>]+>", "").Trim());
                string rawUrl = titleMatches[i].Groups[1].Value;

                // Extract clean target URL from DuckDuckGo redirect
                var urlMatch = Regex.Match(rawUrl, @"uddg=([^&]+)");
                string url = urlMatch.Success ? WebUtility.UrlDecode(urlMatch.Groups[1].Value) : rawUrl;

                string snippet = "No summary available.";
                if (i < snippetMatches.Count)
                {
                    snippet = WebUtility.HtmlDecode(Regex.Replace(snippetMatches[i].Groups[2].Value, @"<[^>]+>", "").Trim());
                }

                results.Add($"[{i + 1}] {title}\nURL: {url}\nSummary: {snippet}");
            }

            return string.Join("\n\n", results);
        }

        private async Task<string> SearchWikipediaAsync(string query)
        {
            using var wikiClient = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            wikiClient.DefaultRequestHeaders.Add("User-Agent", "RedQueenAi/1.0 (contact@redqueen.ai)");

            string url = $"https://en.wikipedia.org/w/api.php?action=query&list=search&srsearch={Uri.EscapeDataString(query)}&utf8=&format=json";
            var json = await wikiClient.GetStringAsync(url);

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("query", out var queryEl) &&
                queryEl.TryGetProperty("search", out var searchEl))
            {
                var wikiResults = new List<string>();
                int count = 0;

                foreach (var item in searchEl.EnumerateArray())
                {
                    if (count >= 3) break;

                    string title = item.GetProperty("title").GetString() ?? "";
                    string rawSnippet = item.GetProperty("snippet").GetString() ?? "";
                    string snippet = WebUtility.HtmlDecode(Regex.Replace(rawSnippet, @"<[^>]+>", "")).Trim();

                    wikiResults.Add($"[Wikipedia] {title}\nURL: https://en.wikipedia.org/wiki/{Uri.EscapeDataString(title.Replace(" ", "_"))}\nSummary: {snippet}");
                    count++;
                }

                if (wikiResults.Count > 0)
                    return string.Join("\n\n", wikiResults);
            }

            return string.Empty;
        }
    }
}
