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
        public static async Task Run([BlobTrigger("everydays/data/{name}", Connection = "EverydaysStorageConnectionString")]Stream myBlob, string name, ILogger log)
        {
            // Create a BlobServiceClient object which will be used to create a container client
            BlobServiceClient blobServiceClient = new BlobServiceClient(Environment.GetEnvironmentVariable("EverydaysStorageConnectionString"));
            _containerClient = blobServiceClient.GetBlobContainerClient("everydays");

            var everydaysClient = _containerClient.GetBlobClient($"everydays.json");
            var currentEverydays = await everydaysClient.DownloadAsync();
            var currentEverydaysReader = new StreamReader(currentEverydays.Value.Content);
            string currentEverydaysText = currentEverydaysReader.ReadToEnd();
            var everydays = JsonConvert.DeserializeObject<List<InstagramPost>>(currentEverydaysText);

            var newEverydayReader = new StreamReader(myBlob);
            string newEverydayText = newEverydayReader.ReadToEnd();
            var newEveryday = JsonConvert.DeserializeObject<InstagramPost>(newEverydayText);

            everydays.Add(newEveryday);

            var everydaysString = JsonConvert.SerializeObject(everydays);
            byte[] byteArray = Encoding.UTF8.GetBytes(everydaysString);
            MemoryStream stream = new MemoryStream(byteArray);

            var blobClient = _containerClient.GetBlobClient($"everydays.json");
            await blobClient.UploadAsync(stream, overwrite: true);
        }
    }
}
