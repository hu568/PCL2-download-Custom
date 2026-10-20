using System;
using System.Net.Http;
using System.Web.Script.Serialization;
using System.IO.Compression;

class Program
{
    static async System.Threading.Tasks.Task<int> Main(string[] args)
    {
        var dict = new { name = "x", ver = 42 };
        var json = new JavaScriptSerializer().Serialize(dict);
        Console.WriteLine($"hello {json} - C# modern");
        using (var hc = new HttpClient())
        {
            hc.DefaultRequestHeaders.UserAgent.ParseAdd("PCL-Download/1.0");
        }
        using (var z = ZipFile.OpenRead(@"C:\CODE\PCL2-download-Custom\tools\nuget\refasm.zip")) { Console.WriteLine("zip entries: " + z.Entries.Count); }
        await System.Threading.Tasks.Task.Delay(10);
        return 0;
    }
}
