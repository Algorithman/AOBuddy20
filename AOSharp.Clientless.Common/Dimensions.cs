using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.IO;
using System.Runtime.CompilerServices;

namespace AOSharp.Clientless.Common
{
    public enum Dimension
    {
        RubiKa,
        RubiKa2019
    }

    public class DimensionInfo
    {
        private const string dimensionListUrl = "http://dimensions.anarchy-online.com:80/new-dimensions/dimensions_v3.txt";

        public static DimensionInfo RubiKa
        {
            get
            {
                DimensionInfo dimension = GetDimension("Rubi-Ka");
                dimension.ChatServerEndpoint = new DnsEndPoint("chat.d1.funcom.com", 7105);
                return dimension;
            }
        }

        public static DimensionInfo RubiKa2019
        {
            get
            {
                DimensionInfo dimension = GetDimension("Rubi-Ka 2019");
                dimension.ChatServerEndpoint = new DnsEndPoint("chat.d1.funcom.com", 7106);
                return dimension;
            }
        }

        public string Name { get; set; }
        public string Version { get; set; }
        public DnsEndPoint ChatServerEndpoint { get; set; }
        public DnsEndPoint GameServerEndpoint { get; set; }
        
        private static List<DimensionInfo> GetDimensions(
            string dimensionListUrl = dimensionListUrl)
        {
            return GetDimensionsAsync(dimensionListUrl).ToListAsync()
                .GetAwaiter().GetResult(); // IAsyncEnumerable extension
        }
        
        public static DimensionInfo GetDimension(string name, string dimensionListUrl = dimensionListUrl)
        {
            IEnumerable<DimensionInfo> dimensions = GetDimensions();

            DimensionInfo dimension = dimensions.FirstOrDefault(d => d.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase));

            if (dimension == null)
            {
                throw new Exception($"Unable to find a dimension named {name}. Possible values are {string.Join(",", dimensions.Select(d => d.Name))}");
            }

            return dimension;
        }

        private static readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        private static async IAsyncEnumerable<DimensionInfo> GetDimensionsAsync(
            string dimensionListUrl = dimensionListUrl,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            using var response = await _httpClient.GetAsync(
                dimensionListUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            DimensionInfo dimension = new();
            string host = "";
            int port = 0;

            string line;
            while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
            {
                if (line.StartsWith('#') || line.Length == 0) continue;

                string[] lineKv = line.Split('=');
                string key = lineKv[0].Trim();
                string value = lineKv.Length > 1 ? lineKv[1].Trim() : key;

                switch (key)
                {
                    case "displayname":
                        dimension.Name = value;
                        break;
                    case "connect":
                        host = value;
                        break;
                    case "ports":
                        port = int.Parse(value);
                        break;
                    case "version":
                        dimension.Version = value + "_EP1";
                        break;
                    case "STARTINFO":
                        dimension = new DimensionInfo();
                        break;
                    case "ENDINFO":
                        dimension.GameServerEndpoint = new DnsEndPoint(host, port);
                        yield return dimension;
                        break;
                }
            }
        }
    }
}
