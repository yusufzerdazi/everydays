using System;
using System.IO;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Everydays;
using System.Net.Http;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using System.Collections.Generic;
using Azure.Storage.Blobs.Models;
using System.Text;

namespace Everydays
{
    public static class EverydayConsolidator
    {
        private static BlobContainerClient _containerClient;

        [FunctionName("EverydayConsolidator")]
        public static async Task Run([TimerTrigger("0 0 1 * * *")] TimerInfo myTimer, ILogger log)
        {
            log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");
            // Create a BlobServiceClient object which will be used to create a container client
            BlobServiceClient blobServiceClient = new BlobServiceClient(Environment.GetEnvironmentVariable("EverydaysStorageConnectionString"));
            _containerClient = blobServiceClient.GetBlobContainerClient("everydays");

            var everydays = new List<InstagramPost>();
            await foreach(var blob in _containerClient.GetBlobsAsync(prefix: "data")){
                log.LogInformation($"Downloading: {blob.Name}");
                var everydayBlobClient = _containerClient.GetBlobClient(blob.Name);
                var blobContent = await everydayBlobClient.DownloadAsync();
                var everydayReader = new StreamReader(blobContent.Value.Content);
                string everydayText = everydayReader.ReadToEnd();
                everydays.Add(JsonConvert.DeserializeObject<InstagramPost>(everydayText));
            }

            var everydaysString = JsonConvert.SerializeObject(everydays);
            byte[] byteArray = Encoding.UTF8.GetBytes(everydaysString);
            MemoryStream stream = new MemoryStream(byteArray);

            log.LogInformation($"Updating consolidated blob.");
            var blobClient = _containerClient.GetBlobClient($"everydays.json");
            await blobClient.UploadAsync(stream, overwrite: true);
        }
    }
}
