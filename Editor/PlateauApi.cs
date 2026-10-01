using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Zabaglione.PlateauAreaDownloader.Editor
{
    internal static class CityGmlTypes
    {
        internal static readonly (string Code, string Label)[] All =
        {
            ("bldg", "建築物"), ("tran", "道路"), ("dem", "地形"),
            ("brid", "橋梁"), ("tun", "トンネル"), ("rwy", "鉄道"),
            ("squr", "広場"), ("trk", "徒歩道"), ("wwy", "航路"),
            ("frn", "都市設備"), ("cons", "その他の構造物"),
            ("luse", "土地利用"), ("veg", "植生"), ("wtr", "水部"),
            ("urf", "都市計画決定情報"), ("area", "区域"),
            ("fld", "洪水浸水想定区域"), ("ifld", "内水浸水想定区域"),
            ("htd", "高潮浸水想定区域"), ("tnm", "津波浸水想定区域"),
            ("lsld", "土砂災害警戒区域"), ("rfld", "ため池ハザードマップ"),
            ("ubld", "地下街"), ("unf", "地下埋設物"),
            ("gen", "汎用都市オブジェクト"), ("ext", "拡張製品仕様書の地物")
        };

        internal static string LabelFor(string code) =>
            All.FirstOrDefault(item => item.Code == code).Label ?? code;
    }

    [Serializable]
    internal sealed class PhotonResponse
    {
        public PhotonFeature[] features;
    }

    [Serializable]
    internal sealed class PhotonFeature
    {
        public PhotonGeometry geometry;
        public PhotonProperties properties;
    }

    [Serializable]
    internal sealed class PhotonGeometry
    {
        public double[] coordinates;
    }

    [Serializable]
    internal sealed class PhotonProperties
    {
        public string name;
        public string city;
        public string district;
        public string state;
        public string country;
        public string osm_value;
        public string street;
    }

    [Serializable]
    internal sealed class CatalogResponse
    {
        public CatalogCity[] cities;
    }

    [Serializable]
    internal sealed class CatalogCity
    {
        public string cityCode;
        public string cityName;
        public int year;
        public string spec;
        public string url;
        public CatalogFiles files;
        public string[] metadataZipUrls;

        public IEnumerable<CatalogGml> FilesFor(string type)
        {
            if (files == null) return Array.Empty<CatalogGml>();
            switch (type)
            {
                case "bldg": return files.bldg ?? Array.Empty<CatalogGml>();
                case "tran": return files.tran ?? Array.Empty<CatalogGml>();
                case "dem": return files.dem ?? Array.Empty<CatalogGml>();
                case "brid": return files.brid ?? Array.Empty<CatalogGml>();
                case "tun": return files.tun ?? Array.Empty<CatalogGml>();
                case "rwy": return files.rwy ?? Array.Empty<CatalogGml>();
                case "squr": return files.squr ?? Array.Empty<CatalogGml>();
                case "trk": return files.trk ?? Array.Empty<CatalogGml>();
                case "wwy": return files.wwy ?? Array.Empty<CatalogGml>();
                case "frn": return files.frn ?? Array.Empty<CatalogGml>();
                case "cons": return files.cons ?? Array.Empty<CatalogGml>();
                case "luse": return files.luse ?? Array.Empty<CatalogGml>();
                case "veg": return files.veg ?? Array.Empty<CatalogGml>();
                case "wtr": return files.wtr ?? Array.Empty<CatalogGml>();
                case "urf": return files.urf ?? Array.Empty<CatalogGml>();
                case "area": return files.area ?? Array.Empty<CatalogGml>();
                case "fld": return files.fld ?? Array.Empty<CatalogGml>();
                case "ifld": return files.ifld ?? Array.Empty<CatalogGml>();
                case "htd": return files.htd ?? Array.Empty<CatalogGml>();
                case "tnm": return files.tnm ?? Array.Empty<CatalogGml>();
                case "lsld": return files.lsld ?? Array.Empty<CatalogGml>();
                case "rfld": return files.rfld ?? Array.Empty<CatalogGml>();
                case "ubld": return files.ubld ?? Array.Empty<CatalogGml>();
                case "unf": return files.unf ?? Array.Empty<CatalogGml>();
                case "gen": return files.gen ?? Array.Empty<CatalogGml>();
                case "ext": return files.ext ?? Array.Empty<CatalogGml>();
                default: return Array.Empty<CatalogGml>();
            }
        }
    }

    [Serializable]
    internal sealed class CatalogFiles
    {
        public CatalogGml[] bldg;
        public CatalogGml[] tran;
        public CatalogGml[] dem;
        public CatalogGml[] brid;
        public CatalogGml[] tun;
        public CatalogGml[] rwy;
        public CatalogGml[] squr;
        public CatalogGml[] trk;
        public CatalogGml[] wwy;
        public CatalogGml[] frn;
        public CatalogGml[] cons;
        public CatalogGml[] luse;
        public CatalogGml[] veg;
        public CatalogGml[] wtr;
        public CatalogGml[] urf;
        public CatalogGml[] area;
        public CatalogGml[] fld;
        public CatalogGml[] ifld;
        public CatalogGml[] htd;
        public CatalogGml[] tnm;
        public CatalogGml[] lsld;
        public CatalogGml[] rfld;
        public CatalogGml[] ubld;
        public CatalogGml[] unf;
        public CatalogGml[] gen;
        public CatalogGml[] ext;
    }

    [Serializable]
    internal sealed class CatalogGml
    {
        public string code;
        public int maxLod;
        public string url;
        public long fileSize;
    }

    [Serializable]
    internal sealed class PackCreated
    {
        public string id;
    }

    [Serializable]
    internal sealed class PackStatus
    {
        public string status;
        public float progress;
        public string error;
    }

    [Serializable]
    internal sealed class PackRequest
    {
        public string[] urls;
    }

    internal static class PlateauApi
    {
        internal const string DefaultApiBase = "https://api.plateauview.mlit.go.jp";
        internal const string DefaultPhotonBase = "https://photon.komoot.io";
        internal const string DefaultGsiTiles = "https://cyberjapandata.gsi.go.jp/xyz/std";

        private static readonly HttpClient Client = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PLATEAU-Area-Downloader/0.1");
            return client;
        }

        internal static async Task<PhotonFeature[]> SearchPlacesAsync(
            string query, string photonBase, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(query)) return Array.Empty<PhotonFeature>();
            var url = photonBase.TrimEnd('/') + "/api/?q=" + Uri.EscapeDataString(query.Trim()) +
                      "&limit=8";
            var json = await GetStringAsync(url, token);
            return JsonUtility.FromJson<PhotonResponse>(json)?.features ?? Array.Empty<PhotonFeature>();
        }

        internal static async Task<CatalogCity[]> SearchCityGmlAsync(
            double west, double south, double east, double north,
            IEnumerable<string> types, string apiBase, CancellationToken token)
        {
            var selectedTypes = types.Distinct().ToArray();
            if (selectedTypes.Length == 0) return Array.Empty<CatalogCity>();
            var c = CultureInfo.InvariantCulture;
            var extent = string.Join(",", new[] { west, south, east, north }.Select(v => v.ToString("R", c)));
            var url = apiBase.TrimEnd('/') + "/datacatalog/citygml/r:" + extent +
                      "?types=" + string.Join(",", selectedTypes);
            var json = await GetStringAsync(url, token);
            return JsonUtility.FromJson<CatalogResponse>(json)?.cities ?? Array.Empty<CatalogCity>();
        }

        internal static async Task<string> CreatePackAsync(string[] urls, string apiBase, CancellationToken token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, apiBase.TrimEnd('/') + "/citygml/pack");
            request.Content = new StringContent(JsonUtility.ToJson(new PackRequest { urls = urls }),
                Encoding.UTF8, "application/json");
            using var response = await SendWithThrottleAsync(request, token);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) throw new HttpRequestException("Pack creation failed: " + (int)response.StatusCode + " " + body);
            var id = JsonUtility.FromJson<PackCreated>(body)?.id;
            if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("Pack response omitted id.");
            return id;
        }

        internal static async Task<PackStatus> GetPackStatusAsync(string id, string apiBase, CancellationToken token)
        {
            var url = apiBase.TrimEnd('/') + "/citygml/pack/" + Uri.EscapeDataString(id) + "/status";
            var json = await GetStringAsync(url, token);
            return JsonUtility.FromJson<PackStatus>(json) ?? throw new InvalidOperationException("Invalid pack status.");
        }

        internal static Task<byte[]> GetTileAsync(string url, CancellationToken token) =>
            GetTileAsync(url, token, Client);

        internal static async Task<byte[]> GetTileAsync(string url, CancellationToken token, HttpClient client)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                response.EnsureSuccessStatusCode();
                // Disposing the response also interrupts transports whose body read ignores cancellation.
                using var cancellation = timeout.Token.Register(response.Dispose);
                using var stream = await response.Content.ReadAsStreamAsync();
                using var output = new System.IO.MemoryStream();
                var buffer = new byte[81920];
                int count;
                while ((count = await stream.ReadAsync(buffer, 0, buffer.Length, timeout.Token)) != 0)
                    output.Write(buffer, 0, count);
                timeout.Token.ThrowIfCancellationRequested();
                return output.ToArray();
            }
            catch (Exception) when (timeout.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
                throw new TimeoutException("Tile request timed out after 15 seconds");
            }
        }

        internal static async Task<GoogleTileSession> CreateGoogleSessionAsync(
            string mapType, string apiKey, CancellationToken token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post,
                GoogleMapTiles.Base + "/v1/createSession?key=" + Uri.EscapeDataString(apiKey));
            request.Content = new StringContent(GoogleMapTiles.SessionRequestJson(mapType),
                Encoding.UTF8, "application/json");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(GoogleMapTiles.SessionTimeout);
            string body;
            try { body = await SendGoogleAsync(request, timeout.Token); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new TimeoutException("Session request timed out after " +
                                           GoogleMapTiles.SessionTimeout.TotalSeconds + " seconds");
            }
            var session = JsonUtility.FromJson<GoogleTileSession>(body);
            if (string.IsNullOrEmpty(session?.session)) throw new InvalidOperationException("Invalid session response.");
            return session;
        }

        internal static async Task<string> GetGoogleCopyrightAsync(string url, CancellationToken token)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                return JsonUtility.FromJson<GoogleViewportInfo>(await SendGoogleAsync(url, timeout.Token))?.copyright ?? "";
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new TimeoutException("Viewport request timed out after 15 seconds");
            }
        }

        private static async Task<string> SendGoogleAsync(string url, CancellationToken token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            return await SendGoogleAsync(request, token);
        }

        // Errors omit the URL so the API key never reaches the UI or logs.
        private static async Task<string> SendGoogleAsync(HttpRequestMessage request, CancellationToken token)
        {
            using var response = await Client.SendAsync(request, token);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException("HTTP " + (int)response.StatusCode + " " + GoogleMapTiles.ErrorMessage(body));
            return body;
        }

        private static async Task<string> GetStringAsync(string url, CancellationToken token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await SendWithThrottleAsync(request, token);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) throw new HttpRequestException("HTTP " + (int)response.StatusCode + " for " + url);
            return body;
        }

        private static async Task<HttpResponseMessage> SendWithThrottleAsync(HttpRequestMessage request, CancellationToken token)
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                using var retry = CloneRequest(request);
                var response = await Client.SendAsync(retry, HttpCompletionOption.ResponseContentRead, token);
                if (response.StatusCode != (HttpStatusCode)429 || attempt == 2) return response;
                var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(3 * (attempt + 1));
                response.Dispose();
                await Task.Delay(delay, token);
            }
            throw new InvalidOperationException("Request retry exhausted.");
        }

        private static HttpRequestMessage CloneRequest(HttpRequestMessage source)
        {
            var clone = new HttpRequestMessage(source.Method, source.RequestUri);
            if (source.Content != null)
            {
                var bytes = source.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                clone.Content = new ByteArrayContent(bytes);
                foreach (var header in source.Content.Headers)
                    clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            return clone;
        }
    }
}
