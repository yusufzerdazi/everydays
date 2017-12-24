#r "System.Configuration"
#r "System.Data"

using System;
using System.Configuration;
using System.Threading.Tasks;
using System.Linq;
using System.Data.Entity;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Net;

public static async Task<HttpResponseMessage> Run(HttpRequestMessage req, TraceWriter log)
{
    log.Info("Processing new everyday.");

    // Get request body.
    PostData data = await req.Content.ReadAsAsync<PostData>();
    var splitFile = data.name.Split(new string[] { "  " }, StringSplitOptions.None);
    var splitName = splitFile[1].Split('.');

    // Get file information.
    var date = splitFile[0];
    var title = splitName[0];
    var extension = splitName[1].ToLower();

    // Try to parse the date.
    var everydayDate = DateTime.Parse(date).Date;
    var everydayMonth = everydayDate.AddDays(-everydayDate.Day + 1).Date;

    log.Info("Parsed data.");
    try
    {
        using (var context = new EverydayContext())
        {
            var everyday = context.Everydays.Where(x => x.Date == everydayDate)
                .Include(x => x.Pieces)
                .FirstOrDefault();
            var month = context.Months.Where(x => everydayMonth == x.Start)
                .Include(x => x.Themes)
                .First();
            var medium = GetMediumFromExtension(extension);
            var theme = month.Themes.Where(x => x.Medium == medium).First();

            if (everyday == null)
            {
                log.Info("Creating new everyday.");
                everyday = new Everyday() { Date = everydayDate, Month = month, Pieces = new List<Piece>() };
                context.Everydays.Add(everyday);
            }
            else
            {
                log.Info("Updating existing everyday.");
            }

            var piece = everyday.Pieces.Where(x => x.Theme == theme).FirstOrDefault();

            if (piece == null)
            {
                log.Info("Creating new piece.");
                piece = new Piece();
                everyday.Pieces.Add(piece);
            }
            else
            {
                log.Info("Updating existing piece.");
            }

            piece.Theme = theme;
            piece.Title = title;
            piece.URL = GetUrlFromSharingUrl(data.url);

            context.SaveChanges();
        }
    }
    catch (System.Data.Entity.Infrastructure.DbUpdateException ex)
    {
        log.Info($"There was an error: {ex.Message}.");
    }

    return req.CreateResponse(HttpStatusCode.OK, "Successfully created Everyday with title " + title);
}

public static Medium GetMediumFromExtension(string extension)
{
    switch (extension)
    {
        case ("mp4"):
        case ("webm"):
            return Medium.Video;
        case ("png"):
        case ("jpg"):
            return Medium.Image;
        case ("mp3"):
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

// Deserialise POST data.
public class PostData
{
    public string name { get; set; }
    public string url { get; set; }
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