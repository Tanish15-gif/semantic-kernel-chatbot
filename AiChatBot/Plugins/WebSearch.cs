using System.ComponentModel;
using HtmlAgilityPack;
using Microsoft.SemanticKernel;

namespace Ai.Plugins
{
    public class WebSearchPlugin
    {
        private readonly HttpClient _client;
        public WebSearchPlugin(HttpClient client)
        {
            _client = client;
        }
        [KernelFunction]
        [Description("Search Web for Latest Information")]
        public async Task<string> Search(string query)
        {
            var url = $"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(query)}";
            var html = await _client.GetStringAsync(url);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var result = doc.DocumentNode
                .SelectNodes("//a[@class='result__a']")
                ?.Take(3);
            if (result == null)
                return "No result Found";

            var output = new List<string>();

            foreach (var r in result)
            {
                string title = HtmlEntity.DeEntitize(r.InnerText.Trim());
                var container = r.ParentNode;

                var snippetNode =
                    container.SelectSingleNode(".//a[contains(@class,'result__snippet')]") ??
                    container.SelectSingleNode(".//div[contains(@class,'result__snippet')]") ??
                    container.SelectSingleNode(".//span");

                string snippet = snippetNode != null
                    ? HtmlEntity.DeEntitize(snippetNode.InnerText.Trim())
                    : "No description available";

                var rawLink = r.GetAttributeValue("href", "");

                var match = System.Text.RegularExpressions.Regex.Match(rawLink, @"uddg=([^&]+)");

                string link = match.Success
                    ? System.Web.HttpUtility.UrlDecode(match.Groups[1].Value)
                    : rawLink;

                string pageText = "";

                try
                {
                    if (link.StartsWith("http"))
                    {
                        using var pagects = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        var pageHtml = await _client.GetStringAsync(link, pagects.Token);

                        var pageDoc = new HtmlDocument();
                        pageDoc.LoadHtml(pageHtml);

                        pageText = HtmlEntity.DeEntitize(pageDoc.DocumentNode.InnerText);

                        pageText = System.Text.RegularExpressions.Regex.Replace(pageText, @"\s+", " ");

                        if (pageText.Length > 1500)
                            pageText = pageText.Substring(0, 1500);
                    }
                }
                catch
                {
                    pageText = "Could not read page content.";
                }

                output.Add($@"
                    Title: {title}
                    Snippet: {snippet}
                    Link: {link}
                    PageContent: {pageText}
                ");
            }

            return string.Join("\n\n", output);
        }
    }
}