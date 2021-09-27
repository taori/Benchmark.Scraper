using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;

namespace Scraper.CLI
{
    record StateInformation(string Capital, string State, string Url);
    
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            var configuration = Configuration.Default
                .WithDefaultLoader()
                .WithCulture("de-DE");
            
            using var context = new BrowsingContext(configuration);
            var url = "https://en.wikipedia.org/wiki/States_of_Germany";
            var page = await GetPageDocumentAsync(context, url);
            
            await ProcessPageAsync(page, context);

            Console.WriteLine("Process complete");
            
            return 0;
        }

        private static async Task ProcessPageAsync(IDocument page, BrowsingContext browsingContext)
        {
            var iterations = 1;
            using (new Measure($"Processing page {iterations} times"))
            { 
                for (int i = 0; i < iterations; i++)
                {
                    var stateUrls = GetStatePageLinks(page)
                        .Select(d => d.Href)
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

        private static async Task<StateInformation> GetStateInformationAsync(string url, BrowsingContext browsingContext)
        {
            using (new Measure($"State information for {url}"))
            {
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
            }
        }

        private static IEnumerable<IHtmlAnchorElement> GetStatePageLinks(IDocument page)
        {
            using (new Measure($"Parsing anchors"))
            {
                return page.QuerySelectorAll<IHtmlAnchorElement>(".wikitable tr>td:nth-child(3)>a");
            }
        }

        private static async Task<IDocument> GetPageDocumentAsync(BrowsingContext context, string url)
        {
            using (new Measure($"Loading page {url}"))
            {
                return await context.OpenAsync(url);
            }
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
