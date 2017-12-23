#r "System.Configuration"
#r "System.Data"

using System;
using System.Configuration;
using System.Threading.Tasks;
using System.Linq;
using System.Data.Entity;
using System.ComponentModel.DataAnnotations;

public static string Run(string inputFile, string name, string date, string extension, TraceWriter log)
{
    // Try to parse the date.
    var everydayDate = DateTime.Parse(date);

    try
    {
        using (var context = new EverydayContext())
        {
        }
    }
    catch (System.Data.Entity.Infrastructure.DbUpdateException ex)
    {
        log.Info($"There was an error: {ex.Message}.");
    }

    return "1";
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
    public Theme Theme { get; set; }
    public string Title { get; set; }
    public string URL { get; set; }
    public Piece Source { get; set; }
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
}

public enum Medium
{
    Image, Sound, Video
}