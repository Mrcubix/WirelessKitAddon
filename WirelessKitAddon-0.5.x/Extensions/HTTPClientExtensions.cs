using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace WirelessKitAddon.Extensions
{
    public static class HTTPClientExtensions
    {
        public static async Task<byte[]> DownloadFile(this HttpClient client, string url)
        {
            using var stream = await client.GetStreamAsync(url);
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);

            return memoryStream.ToArray();
        }

        public static async Task<string> GetStringFromFile(this HttpClient client, string url)
        {
            using var stream = await client.GetStreamAsync(url);
            using var reader = new StreamReader(stream);

            return await reader.ReadToEndAsync();
        }
    }
}