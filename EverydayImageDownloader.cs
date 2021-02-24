using System;
using System.IO;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net.Http;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Processing;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using System.Net;
using System.Dynamic;

namespace Everydays
{
    public static class EverydayImageDownloader
    {
        private static BlobContainerClient _containerClient;

        [FunctionName("EverydayImageDownloader")]
        public static async Task Run([BlobTrigger("everydays/data/{name}", Connection = "EverydaysStorageConnectionString")]Stream myBlob, string name, ILogger log)
        {
            // Create a BlobServiceClient object which will be used to create a container client
            BlobServiceClient blobServiceClient = new BlobServiceClient(Environment.GetEnvironmentVariable("EverydaysStorageConnectionString"));
            _containerClient = blobServiceClient.GetBlobContainerClient("everydays");

            StreamReader reader = new StreamReader(myBlob);
            string everydayText = reader.ReadToEnd();
            var everyday = JsonConvert.DeserializeObject<InstagramPost>(everydayText);

            var instagramClientId = Environment.GetEnvironmentVariable("InstagramClientId");
            var instagramClientSecret = Environment.GetEnvironmentVariable("InstagramClientSecret");

            if(everyday.Timestamp == "2017-12-18")
            {
                return;
            }

            var httpClientHandler = new HttpClientHandler
            {
                AllowAutoRedirect = true
            };

            using (var httpClient = new HttpClient(httpClientHandler))
            {
                var tokenResponse = await httpClient.GetAsync($"https://graph.facebook.com/oauth/access_token?client_id={instagramClientId}&client_secret={instagramClientSecret}&grant_type=client_credentials");
                dynamic tokenObject = JsonConvert.DeserializeObject<ExpandoObject>(await tokenResponse.Content.ReadAsStringAsync());
                
                var imageResponse = await httpClient.GetAsync($@"https://graph.facebook.com/instagram_oembed?url={WebUtility.UrlEncode(everyday.Permalink)}&access_token={tokenObject.access_token}");
                dynamic imageObject = JsonConvert.DeserializeObject<ExpandoObject>(await imageResponse.Content.ReadAsStringAsync());
                var imageUrl = imageObject.thumbnail_url;

                var image = await httpClient.GetAsync(imageUrl);
                var stream = await image.Content.ReadAsStreamAsync();

                using (Image<Rgba32> input = Image.Load<Rgba32>(stream, out IImageFormat format))
                {
                    var smallStream = ResizeImage(input, (30, 30), format);

                    smallStream.Position = 0;
                    var smallBlobClient = _containerClient.GetBlobClient($"imagesSmall/{everyday.Timestamp}.jpg");
                    await smallBlobClient.UploadAsync(smallStream, overwrite: true);
                }

                stream.Position = 0;
                var blobClient = _containerClient.GetBlobClient($"images/{everyday.Timestamp}.jpg");
                await blobClient.UploadAsync(stream, overwrite: true);
            }
        }

        public static Stream ResizeImage(Image<Rgba32> input, (int,int) dimensions, IImageFormat format)
        {
            var stream = new MemoryStream();
            var clonedImage = input.Clone(i => i.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Crop,
                    Size = new Size(dimensions.Item1, dimensions.Item2)
                }));

            clonedImage.Save(stream, format);
            return stream;
        }
    }
}
