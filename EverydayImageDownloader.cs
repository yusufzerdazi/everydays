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
            var smallImageUrl = $"{everyday.Permalink}/media/?size=t";
            var mediumImageUrl = $"{everyday.Permalink}/media/?size=m";

            if(everyday.Timestamp == "2017-12-18")
            {
                return;
            }

            using (var httpClient = new HttpClient())
            {
                var smallMetadata = await httpClient.GetAsync(smallImageUrl);
                var smallImage = await httpClient.GetAsync(smallMetadata.RequestMessage.RequestUri);
                var smallStream = await smallImage.Content.ReadAsStreamAsync();

                var mediumMetadata = await httpClient.GetAsync(mediumImageUrl);
                var mediumImage = await httpClient.GetAsync(mediumMetadata.RequestMessage.RequestUri);
                var mediumStream = await mediumImage.Content.ReadAsStreamAsync();

                using (Image<Rgba32> input = Image.Load<Rgba32>(smallStream, out IImageFormat format))
                {
                    ResizeImage(input, (30, 30), format);
                    MemoryStream stream = new MemoryStream();
                    input.Save(stream, format);

                    stream.Position = 0;
                    var smallBlobClient = _containerClient.GetBlobClient($"imagesSmall/{everyday.Timestamp}.jpg");
                    await smallBlobClient.UploadAsync(stream, overwrite: true);
                }

                mediumStream.Position = 0;
                var blobClient = _containerClient.GetBlobClient($"images/{everyday.Timestamp}.jpg");
                await blobClient.UploadAsync(mediumStream, overwrite: true);
            }
        }

        public static void ResizeImage(Image<Rgba32> input, (int,int) dimensions, IImageFormat format)
        {
            input.Mutate(x => x.Resize(dimensions.Item1, dimensions.Item2));
        }
    }
}
