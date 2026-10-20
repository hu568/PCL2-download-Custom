using System;
using System.IO;
using System.IO.Compression;
using Newtonsoft.Json.Linq;
using System.Net.Http;

class ZipTest
{
    static void Main(string[] args)
    {
        var j = JObject.Parse("{\"a\":[1,2,3]}");
        Console.WriteLine("json ok: " + j["a"].Count());
        using (var z = ZipFile.OpenRead(@"D:\CODE\PCL2-download-Custom\tools\nuget\newtonsoft.zip"))
        {
            Console.WriteLine("zip entries: " + z.Entries.Count + " first=" + z.Entries[0].FullName);
        }
        using (var hc = new HttpClient()) { System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12; var s = hc.GetStringAsync("https://api.modrinth.com/v2/search?limit=1").Result; Console.WriteLine("http ok len=" + s.Length); }
        Console.WriteLine("ALL OK");
    }
}
