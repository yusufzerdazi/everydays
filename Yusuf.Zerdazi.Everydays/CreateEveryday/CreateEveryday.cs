using System;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using System.Configuration;
using System.Threading.Tasks;
using System.Linq;
using System.Data;
using System.Data.SqlClient;
using System.Data.Entity;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.IO;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.Azure.WebJobs.Extensions.Http;

namespace Yusuf.Zerdazi.Everydays
{
    public static class CreateEveryday
    {
        private const string VideosUrl = "https://api.onedrive.com/v1.0/shares/u!aHR0cHM6Ly8xZHJ2Lm1zL2YvcyFBaWdMYzlWX1M5UlVoZVpLTzNkZXBUWEVKTloyUUE/driveItem/children";
        private const string AudioURL = "https://api.onedrive.com/v1.0/shares/u!aHR0cHM6Ly8xZHJ2Lm1zL2YvcyFBaWdMYzlWX1M5UlVoZEVTY091c25XbG16VVJtZHc/driveItem/children";
        private const string ImagesURL = "https://api.onedrive.com/v1.0/shares/u!aHR0cHM6Ly8xZHJ2Lm1zL2YvcyFBaWdMYzlWX1M5UlVoYzliSXozZlJic3c0ZnhsQWc/driveItem/children";
        private const bool ENABLE_UPDATES = false;

        [FunctionName("CreateEveryday")]
        public static async Task Run([HttpTrigger(AuthorizationLevel.Function, "post", Route = "create")]HttpRequestMessage req, TraceWriter log)
        {
            log.Info("Processing new everyday.");

            // parse query parameter
            PostData data = await req.Content.ReadAsAsync<PostData>();

            // Get items from folder.
            var videoFolderItemsJson = await GetItems(VideosUrl);
            var audioFolderItemsJson = await GetItems(AudioURL);
            var imagesFolderItemsJson = await GetItems(ImagesURL);

            var folderItems = DeserialiseItems(videoFolderItemsJson);
            folderItems.AddRange(DeserialiseItems(audioFolderItemsJson));
            folderItems.AddRange(DeserialiseItems(imagesFolderItemsJson));

            // Parse corresponding everydays.
            var folderEverydays = GetFolderEverydays(folderItems, data, log);
            log.Info("Parsed everydays.");

            try
            {
                using (var context = new EverydayContext())
                {
                    // Convert URLs to content URLs
                    foreach (var folderEveryday in folderEverydays)
                    {
                        // Get matching db everyday.
                        var everyday = context.Everydays.Where(x => x.Date == folderEveryday.Date)
                            .Include(x => x.Pieces.Select(y => y.Theme))
                            .FirstOrDefault();

                        if (everyday == null)
                        {
                            everyday = new Everyday()
                            {
                                Date = folderEveryday.Date,
                                Month = folderEveryday.Month,
                                MonthID = folderEveryday.MonthID,
                                Pieces = new List<Piece>()
                            };
                            log.Info($"New everyday with date: {everyday.Date}");
                            log.Info($"Month id is: {everyday.MonthID.ToString()}");
                            context.Everydays.Add(folderEveryday);
                        }

                        foreach (var folderPiece in folderEveryday.Pieces)
                        {
                            var piece = everyday.Pieces.Where(p => p.ThemeID == folderPiece.ThemeID).FirstOrDefault();

                            if (piece == null)
                            {
                                piece = folderPiece;
                                everyday.Pieces.Add(folderPiece);
                                log.Info($"New piece with title: {piece.Title}");
                            }
                            else if (ENABLE_UPDATES)
                            {
                                piece.Title = folderPiece.Title;
                                piece.URL = folderPiece.URL;
                                log.Info($"Updating piece with title: {piece.Title}");
                            }
                        }
                    }

                    context.SaveChanges();
                }
            }
            catch (System.Data.Entity.Infrastructure.DbUpdateException ex)
            {
                log.Info($"There was an error: {ex.Message}.");
            }
        }

        public static List<Everyday> GetFolderEverydays(List<Item> items, PostData data, TraceWriter log)
        {
            var folderEverydays = new List<Everyday>();
            using (var context = new EverydayContext())
            {
                foreach (var item in items)
                {
                    try
                    {
                        // Parse strings
                        var itemData = item.name.Split(new string[] { "  " }, StringSplitOptions.None);
                        var itemDate = itemData[0];
                        var title = Path.GetFileNameWithoutExtension(itemData[1]);
                        var extension = Path.GetExtension(itemData[1]).ToLower();

                        if (title != data.name) continue;
                        if (itemDate != data.date) continue;
                        if (extension != data.extension) continue;

                        // Parse dates.
                        var everydayDate = DateTime.Parse(itemDate).Date;
                        var everydayMonth = everydayDate.AddDays(-everydayDate.Day + 1).Date;

                        // Find matching month.
                        var month = context.Months.Where(x => everydayMonth == x.Start)
                            .Include(x => x.Themes)
                            .FirstOrDefault();
                        if (month == null) { continue; }

                        // Find matching medium.
                        var medium = Medium.Image;
                        try
                        {
                            medium = GetMediumFromExtension(extension);
                        }
                        catch (Exception)
                        {
                            continue;
                        }

                        // Find matching theme.        
                        var theme = month.Themes.Where(x => x.Medium == medium).FirstOrDefault();
                        if (theme == null) { continue; }

                        // Fetch matching everyday.
                        var itemEveryday = folderEverydays.Where(e => e.Date == everydayDate).FirstOrDefault();
                        if (itemEveryday == null)
                        {
                            itemEveryday = new Everyday()
                            {
                                Date = everydayDate,
                                MonthID = month.ID,
                                Pieces = new List<Piece>()
                            };

                            folderEverydays.Add(itemEveryday);
                        }

                        itemEveryday.Pieces.Add(new Piece()
                        {
                            ThemeID = theme.ID,
                            Title = title,
                            URL = GetUrlFromSharingUrl(item.webUrl)
                        });
                    }
                    catch (IndexOutOfRangeException)
                    {
                        // log.Info($"File name not formatted correctly: {item.name}");
                    }
                }
            }
            return folderEverydays;
        }

        public static async Task<string> GetItems(string URL)
        {
            using (var client = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate }))
            {
                client.BaseAddress = new Uri(URL);
                HttpResponseMessage response = await client.GetAsync("");
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
        }

        public static List<Item> DeserialiseItems(string json)
        {
            Folder folder = new Folder();
            using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                DataContractJsonSerializer ser = new DataContractJsonSerializer(folder.GetType());
                folder = ser.ReadObject(ms) as Folder;
            }
            return folder.value.ToList();
        }

        public static Medium GetMediumFromExtension(string extension)
        {
            switch (extension)
            {
                case (".mp4"):
                    return Medium.Video;
                case (".png"):
                case (".jpg"):
                    return Medium.Image;
                case (".mp3"):
                    return Medium.Sound;
                default:
                    throw new InvalidOperationException("No matching medium found.");
            }
        }

        public static string GetUrlFromSharingUrl(string sharingUrl)
        {
            string base64Value = System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(sharingUrl));
            string encodedUrl = "u!" + base64Value.TrimEnd('=').Replace('/', '_').Replace('+', '-');
            string resultUrl = string.Format("https://api.onedrive.com/v1.0/shares/{0}/root/content", encodedUrl);
            return resultUrl;
        }

        [DataContract]
        public class Folder
        {
            [DataMember]
            internal List<Item> value;
        }

        [DataContract]
        public class Item
        {
            [DataMember]
            internal string webUrl;
            [DataMember]
            internal string name;
        }

        // Context
        public class EverydayContext : DbContext
        {
            public EverydayContext()
                : base(ConfigurationManager.ConnectionStrings["yusufzerdazi"].ConnectionString)
            { }

            public DbSet<Everyday> Everydays { get; set; }
            public DbSet<Month> Months { get; set; }
            public DbSet<Theme> Themes { get; set; }
            public DbSet<Piece> Pieces { get; set; }
        }

        // Models
        public class Everyday
        {
            public Everyday()
            {

            }
            [Key]
            public int ID { get; set; }
            public int MonthID { get; set; }
            [ForeignKey("MonthID")]
            public virtual Month Month { get; set; }
            public DateTime Date { get; set; }
            public ICollection<Piece> Pieces { get; set; }
        }

        public class Month
        {
            public Month()
            {

            }
            [Key]
            public int ID { get; set; }
            public DateTime Start { get; set; }
            public ICollection<Theme> Themes { get; set; }
        }

        public class Piece
        {
            public Piece()
            {

            }

            [Key]
            public int ID { get; set; }
            [ForeignKey("ThemeID")]
            public Theme Theme { get; set; }
            public int ThemeID { get; set; }
            public string Title { get; set; }
            public string URL { get; set; }

            [ForeignKey("SourceID")]
            public Piece Source { get; set; }
            public int? SourceID { get; set; }

            [ForeignKey("EverydayID")]
            public Everyday Everyday { get; set; }
            public int EverydayID { get; set; }
        }

        public class Theme
        {
            public Theme()
            {

            }
            [Key]
            public int ID { get; set; }
            public string Title { get; set; }
            public Medium Medium { get; set; }
            [ForeignKey("MonthID")]
            public Month Month { get; set; }
            public int MonthID { get; set; }
        }

        public enum Medium
        {
            Image, Sound, Video
        }

        public class PostData
        {
            public string name { get; set; }
            public string date { get; set; }
            public string extension { get; set; }
        }
    }
}