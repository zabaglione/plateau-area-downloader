using System;
using NUnit.Framework;
using UnityEngine;

namespace Zabaglione.PlateauAreaDownloader.Editor.Tests
{
    public class GoogleMapTilesTests
    {
        [Test]
        public void SessionRequestJson_UsesJapaneseAndAddsRoadmapLayerOnlyForTerrain()
        {
            Assert.That(GoogleMapTiles.SessionRequestJson("roadmap"),
                Is.EqualTo("{\"mapType\":\"roadmap\",\"language\":\"ja-JP\",\"region\":\"JP\"}"));
            Assert.That(GoogleMapTiles.SessionRequestJson("satellite"), Does.Not.Contain("layerTypes"));
            Assert.That(GoogleMapTiles.SessionRequestJson("terrain"),
                Is.EqualTo("{\"mapType\":\"terrain\",\"language\":\"ja-JP\",\"region\":\"JP\",\"layerTypes\":[\"layerRoadmap\"]}"));
        }

        [Test]
        public void TileUrl_UsesZxyPathAndEscapesQueryValues()
        {
            Assert.That(GoogleMapTiles.TileUrl("15/29105/12903", "a+b", "k&y"),
                Is.EqualTo("https://tile.googleapis.com/v1/2dtiles/15/29105/12903?session=a%2Bb&key=k%26y"));
        }

        [Test]
        public void ViewportUrl_FormatsCoordinatesWithInvariantCulture()
        {
            var culture = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                Assert.That(GoogleMapTiles.ViewportUrl("s", "k", 15, 139.5, 35.25, 139.75, 35.5),
                    Is.EqualTo("https://tile.googleapis.com/tile/v1/viewport?session=s&key=k&zoom=15" +
                               "&north=35.5&south=35.25&east=139.75&west=139.5"));
            }
            finally { System.Threading.Thread.CurrentThread.CurrentCulture = culture; }
        }

        [Test]
        public void SessionResponse_ParsesStringExpiryAndRenewsOneHourEarly()
        {
            var session = JsonUtility.FromJson<GoogleTileSession>(
                "{\"session\":\"token\",\"expiry\":\"1800000000\",\"tileWidth\":256,\"tileHeight\":256,\"imageFormat\":\"png\"}");
            Assert.That(session.session, Is.EqualTo("token"));
            Assert.That(GoogleMapTiles.IsUsable(session, DateTimeOffset.FromUnixTimeSeconds(1800000000 - 3601)), Is.True);
            Assert.That(GoogleMapTiles.IsUsable(session, DateTimeOffset.FromUnixTimeSeconds(1800000000 - 3600)), Is.False);
            Assert.That(GoogleMapTiles.IsUsable(new GoogleTileSession { session = "token", expiry = "bad" },
                DateTimeOffset.FromUnixTimeSeconds(0)), Is.False);
        }

        [Test]
        public void CanRetry_WaitsForRetryDelayAfterFailure()
        {
            var failedAt = DateTimeOffset.FromUnixTimeSeconds(1000);
            Assert.That(GoogleMapTiles.CanRetry(failedAt, failedAt.AddSeconds(9.9)), Is.False);
            Assert.That(GoogleMapTiles.CanRetry(failedAt, failedAt.AddSeconds(10)), Is.True);
        }

        [Test]
        public void ErrorMessage_ExtractsGoogleErrorMessageOrFallsBack()
        {
            Assert.That(GoogleMapTiles.ErrorMessage(
                "{\"error\":{\"code\":400,\"message\":\"API key not valid.\",\"status\":\"INVALID_ARGUMENT\"}}"),
                Is.EqualTo("API key not valid."));
            Assert.That(GoogleMapTiles.ErrorMessage("not json"), Is.EqualTo(""));
        }
    }
}
