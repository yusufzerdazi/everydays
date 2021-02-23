using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using HtmlAgilityPack;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System.IO;
using Newtonsoft.Json;
using System.Text;
using System.Net;

namespace Everydays
{
    public static class EverydaysScraper
    {
        private static BlobContainerClient _containerClient;
        private static HttpClientHandler _httpClientHandler;

        [FunctionName("EverydaysScraper")]
        public static async Task Run([TimerTrigger("0 0 0 * * *")] TimerInfo myTimer, ILogger log)
        {
            // First create a proxy object
            var proxy = new WebProxy
            {
                Address = new Uri($"http://pi.zerdazi.com:8118"),
                BypassProxyOnLocal = false
            };

            // Now create a client handler which uses that proxy
            _httpClientHandler = new HttpClientHandler
            {
                Proxy = proxy,
            };

            // Create a BlobServiceClient object which will be used to create a container client
            BlobServiceClient blobServiceClient = new BlobServiceClient(Environment.GetEnvironmentVariable("EverydaysStorageConnectionString"));
            _containerClient = blobServiceClient.GetBlobContainerClient("everydays");

            await ScrapeInstagram("https://instagram.com/everyda.ys", log);
            log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");
        }


        public static async Task<object> ScrapeInstagram(string url, ILogger log)
        {
            var existingEverydays = new List<string>();
            await foreach (BlobItem page in _containerClient.GetBlobsAsync(prefix: "data"))
            {
                existingEverydays.Add(Path.GetFileNameWithoutExtension(page.Name));
            }
            using (var client = new HttpClient(handler: _httpClientHandler, disposeHandler: false))
            {
                var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    // create html document
                    var htmlBody = await response.Content.ReadAsStringAsync();
                    var htmlDocument = new HtmlDocument();
                    htmlDocument.LoadHtml(htmlBody);

                    // select script tags
                    var scripts = htmlDocument.DocumentNode.SelectNodes("/html/body/script");

                    // preprocess result
                    var uselessString = "window._sharedData = ";
                    var scriptInnerText = scripts[0].InnerText
                        .Substring(uselessString.Length)
                        .Replace(";", "");

                    // serialize objects and fetch the user data
                    dynamic jsonStuff = JObject.Parse(scriptInnerText);
                    log.LogInformation((string)JsonConvert.SerializeObject(jsonStuff));
                    JArray igPosts = jsonStuff["entry_data"]["ProfilePage"][0]["graphql"]["user"]["edge_owner_to_timeline_media"]["edges"];

                    var posts = igPosts.Select(ParseInstagramPost).ToList();
                    var newPosts = posts.Where(p => !existingEverydays.Contains(p.Timestamp)).ToList();

                    foreach (var everyday in newPosts)
                    {
                        var serialised = JsonConvert.SerializeObject(everyday);
                        byte[] byteArray = Encoding.UTF8.GetBytes(serialised);
                        MemoryStream stream = new MemoryStream(byteArray);
                        var blobClient = _containerClient.GetBlobClient($"data/{everyday.Timestamp}.json");
                        await blobClient.UploadAsync(stream, overwrite: true);
                    }

                    return newPosts;
                }
                else
                {
                    throw new Exception($"Something wrong happened {response.StatusCode} - {response.ReasonPhrase} - {response.RequestMessage}");
                }
            }
        }

        public static InstagramPost ParseInstagramPost(dynamic post)
        {
            string captionText = post.node.edge_media_to_caption.edges[0].node.text;
            int uploadedDate = post.node.taken_at_timestamp;
            string permalink = $"https://instagram.com/p/{post.node.shortcode}";

            var timestamp = UnixTimeToDateTime(uploadedDate);
            var (caption, tags) = ParseTags(captionText);

            return new InstagramPost
            {
                Timestamp = $"{timestamp:yyyy-MM-dd}",
                Title = caption,
                Permalink = permalink,
                Tags = tags
            };
        }

        public static (string, List<string>) ParseTags(string caption)
        {
            var tagsRegex = new Regex(@"\#\w+");
            var tags = tagsRegex.Matches(caption);

            var strippedCaption = caption;
            foreach (Match tag in tags)
            {
                strippedCaption = strippedCaption.Replace(tag.Value, "");
            }
            strippedCaption = strippedCaption.Trim();

            return (strippedCaption, tags
                .Select(t => t.Value.Replace("#", ""))
                .Where(t => t != "everyday")
                .ToList());
        }

        /// <summary>
        /// Convert Unix time value to a DateTime object.
        /// </summary>
        /// <param name="unixtime">The Unix time stamp you want to convert to DateTime.</param>
        /// <returns>Returns a DateTime object that represents value of the Unix time.</returns>
        public static DateTime UnixTimeToDateTime(long unixtime)
        {
            System.DateTime dtDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc);
            dtDateTime = dtDateTime.AddSeconds(unixtime).ToLocalTime();
            return dtDateTime;
        }
    }
}
