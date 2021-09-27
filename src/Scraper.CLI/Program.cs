using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace Scraper.CLI
{
    record StateInformation(string Capital, string State, string Url);

    public class UrlRewriteHelper 
    {
        private readonly Dictionary<string, string> _urlReplacements;

        public UrlRewriteHelper(Dictionary<string, string> urlReplacements)
        {
            _urlReplacements = urlReplacements;
        }

        public string Rewrite(string url)
        {
            foreach (var replacement in _urlReplacements)
            {
                if (url.Contains(replacement.Key))
                    url = url.Replace(replacement.Key, replacement.Value);
            }

            return url;
        }
    }
    public class DocumentLoader : IDisposable
    {
        private readonly BrowsingContext _browsingContext;
        private readonly UrlRewriteHelper _urlHelper;
        private readonly string _cacheFolder;
        private readonly HtmlParser _parser;

        public DocumentLoader(BrowsingContext browsingContext, UrlRewriteHelper urlHelper)
        {
            _browsingContext = browsingContext;
            _urlHelper = urlHelper;
            _cacheFolder = Path.GetDirectoryName(typeof(Program).Assembly.Location);
            _parser = new HtmlParser(new HtmlParserOptions());
        }

        public async Task<IDocument> OpenAsync(string url)
        {
            url = _urlHelper.Rewrite(url);
            
            var localFileUri = GetLocalFileUri(url);
            if (File.Exists(localFileUri))
                return await GetCachedDocument(localFileUri);

            var document = await _browsingContext.OpenAsync(url);
            var directoryInfo = new DirectoryInfo(Path.GetDirectoryName(localFileUri));
            if (!directoryInfo.Exists)
            {
                // ensure nested path creation
                directoryInfo.Create();
            }
            
            await File.WriteAllTextAsync(localFileUri, document.Source.Text);
            return document;
        }

        private async Task<IDocument> GetCachedDocument(string localFileUri)
        {
            using (var streamReader = new FileStream(localFileUri, FileMode.Open, FileAccess.Read))
            {
                return await _parser.ParseDocumentAsync(streamReader);
            }
        }

        private string GetLocalFileUri(string url)
        {
            var fileHash = Convert.ToBase64String(Encoding.UTF8.GetBytes(url));
            var fullPath = Path.Combine(_cacheFolder, "cache", $"hash-{fileHash}.html");
            return fullPath;
        }

        public void Dispose()
        {
            ((IDisposable)_browsingContext)?.Dispose();
        }
    }
    
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            var configuration = Configuration.Default
                .WithDefaultLoader()
                .WithCulture("de-DE");
            
            using var browsingContext = new BrowsingContext(configuration);
            var rewriteHelper = new UrlRewriteHelper(new Dictionary<string, string>()
            {
                { "about:///", "https://en.wikipedia.org/" }
            });
            
            using var documentLoader = new DocumentLoader(browsingContext, rewriteHelper);
            var url = "https://en.wikipedia.org/wiki/States_of_Germany";
            var page = await GetPageDocumentAsync(documentLoader, url);
            
            await ProcessPageAsync(page, documentLoader, rewriteHelper);

            Console.WriteLine("Process complete");
            
            return 0;
        }

        private static async Task ProcessPageAsync(IDocument page, DocumentLoader browsingContext, UrlRewriteHelper urlRewriteHelper)
        {
            var iterations = 1;
            using (new Measure($"Processing page {iterations} times"))
            { 
                for (int i = 0; i < iterations; i++)
                {
                    var stateUrls = GetStatePageLinks(page)
                        .Select(d => urlRewriteHelper.Rewrite(d.Href))
                        .ToArray();
                    
                    var informations = stateUrls.Select(url => GetStateInformationAsync(url, browsingContext)).ToArray();
                    await Task.WhenAll(informations);

                    foreach (var information in informations)
                    {
                        Console.WriteLine($"{information.Result.State} {information.Result.Capital} {information.Result.Url}");
                    }
                }
            }
        }

        private static async Task<StateInformation> GetStateInformationAsync(string url, DocumentLoader browsingContext)
        {
            // using (new Measure($"State information for {url}"))
            // {
                var subPage = await GetPageDocumentAsync(browsingContext, url);
                var stateName = subPage.QuerySelector("h1").Text();
                var tableHeaders = subPage.QuerySelectorAll("table.infobox th.infobox-label");
                var capitalHeader = tableHeaders.FirstOrDefault(d => d.Text().Contains("Capital"));
                if (capitalHeader == null)
                {
                    return new StateInformation("N/A", stateName, url);
                }
                else
                {
                    return new StateInformation(capitalHeader.NextSibling.FirstChild.Text(), stateName, url);
                }
            // }
        }

        private static IEnumerable<IHtmlAnchorElement> GetStatePageLinks(IDocument page)
        {
            using (new Measure($"Parsing anchors"))
            {
                return page.QuerySelectorAll<IHtmlAnchorElement>(".wikitable tr>td:nth-child(3)>a");
            }
        }

        private static async Task<IDocument> GetPageDocumentAsync(DocumentLoader context, string url)
        {
            // using (new Measure($"Loading page {url}"))
            // {
                return await context.OpenAsync(url);
            // }
        }
    }

    public class Measure : IDisposable
    {
        private readonly string _message;
        private Stopwatch _stopWatch;

        public Measure(string message)
        {
            _message = message;
            _stopWatch = new Stopwatch();
            _stopWatch.Restart();
        }

        public void Dispose()
        {
            Console.WriteLine($"{_message} -{_stopWatch.ElapsedMilliseconds}ms");
            _stopWatch.Stop();
            _stopWatch = null;
        }
    }
}
