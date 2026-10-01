using System;
using System.Globalization;
using UnityEngine;

namespace Zabaglione.PlateauAreaDownloader.Editor
{
    [Serializable]
    internal sealed class GoogleTileSession
    {
        public string session;
        public string expiry;
    }

    [Serializable]
    internal sealed class GoogleViewportInfo
    {
        public string copyright;
    }

    [Serializable]
    internal sealed class GoogleErrorResponse
    {
        public GoogleError error;
    }

    [Serializable]
    internal sealed class GoogleError
    {
        public string message;
    }

    internal static class GoogleMapTiles
    {
        internal const string Base = "https://tile.googleapis.com";
        internal const string TermsUrl = "https://cloud.google.com/maps-platform/terms";

        internal static readonly (string Code, string Label)[] MapTypes =
        {
            ("roadmap", "道路地図"), ("satellite", "航空写真"), ("terrain", "地形")
        };

        internal static string SessionRequestJson(string mapType) =>
            "{\"mapType\":\"" + mapType + "\",\"language\":\"ja-JP\",\"region\":\"JP\"" +
            (mapType == "terrain" ? ",\"layerTypes\":[\"layerRoadmap\"]" : "") + "}";

        internal static string TileUrl(string tileKey, string session, string apiKey) =>
            Base + "/v1/2dtiles/" + tileKey + "?session=" + Uri.EscapeDataString(session) +
            "&key=" + Uri.EscapeDataString(apiKey);

        internal static string ViewportUrl(string session, string apiKey, int zoom,
            double west, double south, double east, double north)
        {
            var c = CultureInfo.InvariantCulture;
            return Base + "/tile/v1/viewport?session=" + Uri.EscapeDataString(session) +
                   "&key=" + Uri.EscapeDataString(apiKey) + "&zoom=" + zoom.ToString(c) +
                   "&north=" + north.ToString("R", c) + "&south=" + south.ToString("R", c) +
                   "&east=" + east.ToString("R", c) + "&west=" + west.ToString("R", c);
        }

        // Renew one hour early so tiles requested near the expiry do not fail mid-session.
        internal static bool IsUsable(GoogleTileSession session, DateTimeOffset now) =>
            session != null && !string.IsNullOrEmpty(session.session) &&
            long.TryParse(session.expiry, NumberStyles.Integer, CultureInfo.InvariantCulture, out var expiry) &&
            now.ToUnixTimeSeconds() < expiry - 3600;

        // Shorter than the shared HttpClient timeout so a stalled request surfaces as a retryable failure.
        internal static readonly TimeSpan SessionTimeout = TimeSpan.FromSeconds(15);

        // A failed session is kept briefly so every visible tile does not repeat the same failing request.
        internal static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);

        internal static bool CanRetry(DateTimeOffset failedAt, DateTimeOffset now) => now - failedAt >= RetryDelay;

        internal static string ErrorMessage(string body)
        {
            try { return JsonUtility.FromJson<GoogleErrorResponse>(body)?.error?.message ?? ""; }
            catch (ArgumentException) { return ""; }
        }
    }
}
