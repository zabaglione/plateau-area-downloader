using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Zabaglione.PlateauAreaDownloader.Editor.Tests
{
    public class AntimeridianTests
    {
        [TestCase(180, -180)]
        [TestCase(-180, -180)]
        [TestCase(181, -179)]
        [TestCase(-181, 179)]
        [TestCase(540, -180)]
        [TestCase(-540, -180)]
        [TestCase(200, -160)]
        [TestCase(139.7454, 139.7454)]
        public void Longitude_WrapsWithoutChangingJapaneseCoordinates(double input, double expected)
        {
            Assert.That(GeoBounds.NormalizeLongitude(input), Is.EqualTo(expected).Within(1e-10));
        }

        [Test]
        public void Longitude_RejectsNonFiniteValues()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => GeoBounds.NormalizeLongitude(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => GeoBounds.NormalizeLongitude(double.PositiveInfinity));
        }

        [TestCase(179, 181, 179, -179, 2)]
        [TestCase(-181, -179, 179, -179, 2)]
        [TestCase(189, 210, -171, -150, 21)]
        [TestCase(170, 180, 170, 180, 10)]
        [TestCase(180, 180, -180, -180, 0)]
        [TestCase(-200, 200, -180, 180, 360)]
        public void UnwrappedBounds_PreserveExtent(double west, double east, double expectedWest, double expectedEast, double span)
        {
            var bounds = GeoBounds.FromUnwrapped(west, -1, east, 1);
            Assert.That(bounds.West, Is.EqualTo(expectedWest));
            Assert.That(bounds.East, Is.EqualTo(expectedEast));
            Assert.That(bounds.LongitudeSpan, Is.EqualTo(span));
        }

        [TestCase(179.999)]
        [TestCase(-179.999)]
        [TestCase(180)]
        [TestCase(-180)]
        public void FromCenter_WrapsOneKilometerSquare(double longitude)
        {
            var bounds = GeoBounds.FromCenter(0, longitude);
            Assert.That(bounds.IsValid, Is.True);
            Assert.That(bounds.CrossesAntimeridian, Is.True);
            Assert.That(bounds.LongitudeSpan, Is.EqualTo(1000 / 6378137d * 180 / Math.PI).Within(1e-10));
            Assert.That(bounds.Contains(0, longitude), Is.True);
        }

        [Test]
        public void CrossingBounds_ContainAndIntersectBothSidesWithoutIncludingGreenwich()
        {
            var bounds = new GeoBounds(170, -10, -170, 10);
            Assert.That(bounds.CenterLongitude, Is.EqualTo(-180));
            foreach (var longitude in new[] { 175d, -175d, 180d, -180d })
                Assert.That(bounds.Contains(0, longitude, false), Is.True);
            Assert.That(bounds.Contains(0, 0), Is.False);
            Assert.That(bounds.Contains(0, 170, false), Is.False);
            Assert.That(bounds.Intersects(new GeoBounds(174, -1, 176, 1), false), Is.True);
            Assert.That(bounds.Intersects(new GeoBounds(-176, -1, -174, 1), false), Is.True);
            Assert.That(bounds.Intersects(new GeoBounds(-1, -1, 1, 1)), Is.False);
            Assert.That(bounds.SplitAtAntimeridian().Length, Is.EqualTo(2));
            Assert.That(new GeoBounds(170, -1, 180, 1).Intersects(new GeoBounds(-180, -1, -170, 1)), Is.True);
            Assert.That(new GeoBounds(170, -1, 180, 1).Intersects(new GeoBounds(-180, -1, -170, 1), false), Is.False);
        }

        [TestCase(180, -179, -180, -179)]
        [TestCase(179, -180, 179, 180)]
        public void SplitAtAntimeridian_ExcludesZeroWidthSeamHalves(double west, double east, double expectedWest, double expectedEast)
        {
            var parts = new GeoBounds(west, -1, east, 1).SplitAtAntimeridian();
            Assert.That(parts.Length, Is.EqualTo(1));
            Assert.That(parts[0].West, Is.EqualTo(expectedWest));
            Assert.That(parts[0].East, Is.EqualTo(expectedEast));
        }

        [Test]
        public void MeshEnumeration_SplitsCrossingBoundsAndDoesNotIncludeJapan()
        {
            var crossing = new GeoBounds(179.99, 35, -179.99, 35.001);
            var expected = JapanMeshCode.EnumerateIntersecting(new GeoBounds(179.99, 35, 180, 35.001), false);
            CollectionAssert.AreEquivalent(expected, JapanMeshCode.EnumerateIntersecting(crossing, false));
            Assert.That(JapanMeshCode.EnumerateIntersecting(crossing, false), Does.Not.Contain("53393599"));
        }

        [Test]
        public void WorldCoordinates_WrapRepeatedPansButCanKeepSelectionEndpointsUnwrapped()
        {
            var world = MapZoomMath.ToWorld(0, 181, 5.5);
            Assert.That(MapZoomMath.FromWorld(world.x, world.y, 5.5).longitude, Is.EqualTo(-179).Within(1e-10));
            Assert.That(MapZoomMath.FromWorld(world.x, world.y, 5.5, false).longitude, Is.EqualTo(181).Within(1e-10));
            var scale = MapZoomMath.WorldScale(5.5);
            Assert.That(MapZoomMath.FromWorld(world.x + 5 * scale, world.y, 5.5).longitude,
                Is.EqualTo(-179).Within(1e-10));
        }

        private sealed class CatalogHandler : HttpMessageHandler
        {
            internal readonly List<string> Paths = new List<string>();
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                Paths.Add(request.RequestUri.AbsolutePath);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent("{\"cities\":[]}") });
            }
        }

        [UnityTest]
        public IEnumerator CatalogSearch_UsesTwoOrderedRequestsOnlyForCrossingBounds()
        {
            var handler = new CatalogHandler();
            using var client = new HttpClient(handler);
            var request = PlateauApi.SearchCityGmlAsync(179, -1, -179, 1, new[] { "bldg" },
                "https://example.test", CancellationToken.None, client);
            while (!request.IsCompleted) yield return null;
            Assert.That(request.GetAwaiter().GetResult(), Is.Empty);
            CollectionAssert.AreEqual(new[] { "/datacatalog/citygml/r:179,-1,180,1", "/datacatalog/citygml/r:-180,-1,-179,1" }, handler.Paths);
            handler.Paths.Clear();
            request = PlateauApi.SearchCityGmlAsync(139, 35, 140, 36, new[] { "bldg" },
                "https://example.test", CancellationToken.None, client);
            while (!request.IsCompleted) yield return null;
            request.GetAwaiter().GetResult();
            Assert.That(handler.Paths.Count, Is.EqualTo(1));
            foreach (var bounds in new[] { new GeoBounds(180, -1, -179, 1), new GeoBounds(179, -1, -180, 1) })
            {
                handler.Paths.Clear();
                request = PlateauApi.SearchCityGmlAsync(bounds.West, bounds.South, bounds.East, bounds.North,
                    new[] { "bldg" }, "https://example.test", CancellationToken.None, client);
                while (!request.IsCompleted) yield return null;
                request.GetAwaiter().GetResult();
                Assert.That(handler.Paths.Count, Is.EqualTo(1), "A nonzero seam range included a zero-width catalog request");
            }
        }

        [Test]
        public void Manifest_DeduplicatesGmlReturnedByBothCatalogHalves()
        {
            var city = new CatalogCity { cityCode = "test", cityName = "Validation", url = "https://example.test/city.zip",
                metadataZipUrls = new[] { "https://example.test/meta.zip" } };
            var gml = new CatalogGml { url = "https://example.test/file.gml", code = "53393599", fileSize = 123 };
            var selected = new[] { (city, "bldg", gml), (city, "bldg", gml) };
            var manifest = PackDownloader.CreateManifest("Validation", 179, -1, -179, 1, selected, "https://example.test");
            Assert.That(manifest.selectedGmls.Length, Is.EqualTo(1));
            Assert.That(manifest.gmlBytes, Is.EqualTo(123));
            Assert.That(manifest.metadataUrls.Length, Is.EqualTo(1));
        }
    }

    public class AntimeridianWindowTests
    {
        private const string Prefs = "Zabaglione.PlateauAreaDownloader.";
        private AreaDownloaderWindow window;
        private string savedProvider, savedType;
        private readonly List<GeoBounds> viewports = new List<GeoBounds>();

        [UnitySetUp]
        public IEnumerator Setup()
        {
            savedProvider = EditorPrefs.GetString(Prefs + "mapProvider", "gsi");
            savedType = EditorPrefs.GetString(Prefs + "googleMapType", "roadmap");
            EditorPrefs.SetString(Prefs + "mapProvider", "gsi");
            var texture = new Texture2D(2, 2);
            var png = texture.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(texture);
            window = ScriptableObject.CreateInstance<AreaDownloaderWindow>();
            window.CreateGoogleSession = (type, key, token) => Task.FromResult(new GoogleTileSession
                { session = "validation-session", expiry = "4102444800" });
            window.FetchTile = (url, token) => Task.FromResult(png);
            window.FetchGoogleCopyright = (url, token) =>
            {
                var query = new Uri(url).Query.TrimStart('?').Split('&').Select(item => item.Split('='))
                    .ToDictionary(item => item[0], item => item[1]);
                double Read(string name) => double.Parse(query[name], CultureInfo.InvariantCulture);
                viewports.Add(new GeoBounds(Read("west"), Read("south"), Read("east"), Read("north")));
                return Task.FromResult("Validation copyright");
            };
            window.Show();
            window.position = new Rect(30, 60, 760, 700);
            yield return new WaitForSecondsRealtime(0.5f);
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            window.Close();
            EditorPrefs.SetString(Prefs + "mapProvider", savedProvider);
            EditorPrefs.SetString(Prefs + "googleMapType", savedType);
            viewports.Clear();
            yield return null;
        }

        private VisualElement Map => window.rootVisualElement.Q<VisualElement>("map");
        private void Apply(double west, double east)
        {
            window.rootVisualElement.Q<DoubleField>("west").SetValueWithoutNotify(west);
            window.rootVisualElement.Q<DoubleField>("east").SetValueWithoutNotify(east);
            window.rootVisualElement.Q<DoubleField>("south").SetValueWithoutNotify(-0.001);
            window.rootVisualElement.Q<DoubleField>("north").SetValueWithoutNotify(0.001);
            var button = window.rootVisualElement.Q<Button>("apply-bounds");
            button.Focus();
            using var submit = NavigationSubmitEvent.GetPooled();
            submit.target = button;
            button.SendEvent(submit);
        }

        [UnityTest]
        public IEnumerator CrossingInput_DrawsNarrowSelectionAndRequestsWrappedCopyright()
        {
            Apply(179.999, -179.999);
            yield return new WaitForSecondsRealtime(0.5f);
            var overlay = window.rootVisualElement.Q<VisualElement>("bounds-overlay");
            Assert.That(overlay.childCount, Is.EqualTo(1));
            Assert.That(overlay[0].resolvedStyle.width, Is.InRange(40f, 50f));
            var provider = window.rootVisualElement.Q<DropdownField>("map-provider");
            provider.value = "Google Maps";
            window.rootVisualElement.Q<TextField>("google-api-key").SetValueWithoutNotify("validation-key");
            provider.value = "地理院タイル";
            viewports.Clear();
            provider.value = "Google Maps";
            yield return new WaitForSecondsRealtime(1.2f);
            Assert.That(viewports.Count, Is.GreaterThan(0));
            Assert.That(viewports.Last().CrossesAntimeridian, Is.True);
            Assert.That(viewports.Last().LongitudeSpan, Is.LessThan(1));
            Assert.That(window.rootVisualElement.Q<Label>("map-attribution").text, Is.EqualTo("Validation copyright"));
            Assert.That(window.rootVisualElement.Q<VisualElement>("tiles").style.visibility.value, Is.EqualTo(Visibility.Visible));
        }

        [UnityTest]
        public IEnumerator ShiftDragAcrossSeam_KeepsSmallCrossingBounds()
        {
            Apply(179.999, -179.999);
            yield return new WaitForSecondsRealtime(0.5f);
            var center = Map.worldBound.center;
            using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0,
                mousePosition = center - new Vector2(40, 30), modifiers = EventModifiers.Shift })) { down.target = Map; Map.SendEvent(down); }
            using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0,
                mousePosition = center + new Vector2(40, 30), modifiers = EventModifiers.Shift })) { up.target = Map; Map.SendEvent(up); }
            yield return new WaitForSecondsRealtime(0.5f);
            var west = window.rootVisualElement.Q<DoubleField>("west").value;
            var east = window.rootVisualElement.Q<DoubleField>("east").value;
            Assert.That(west, Is.GreaterThan(179.99));
            Assert.That(east, Is.LessThan(-179.99));
            Assert.That(new GeoBounds(west, -1, east, 1).LongitudeSpan, Is.InRange(0.003, 0.004));
        }
        [UnityTest]
        public IEnumerator PanAndWheelAcrossSeam_RequestValidLongitudeAndSmallerViewport()
        {
            Apply(179.999, -179.999);
            yield return new WaitForSecondsRealtime(0.5f);
            var point = Map.worldBound.center;
            using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0,
                mousePosition = point })) { down.target = Map; Map.SendEvent(down); }
            using (var move = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseDrag, button = 0,
                mousePosition = point + new Vector2(300, 0) })) { move.target = Map; Map.SendEvent(move); }
            using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0,
                mousePosition = point + new Vector2(300, 0) })) { up.target = Map; Map.SendEvent(up); }
            var provider = window.rootVisualElement.Q<DropdownField>("map-provider");
            provider.value = "Google Maps";
            window.rootVisualElement.Q<TextField>("google-api-key").SetValueWithoutNotify("validation-key");
            provider.value = "地理院タイル";
            viewports.Clear();
            provider.value = "Google Maps";
            yield return new WaitForSecondsRealtime(1.2f);
            Assert.That(viewports.Count, Is.GreaterThan(0));
            var before = viewports.Last();
            Assert.That(before.CenterLongitude, Is.GreaterThan(179.9));
            using (var wheel = WheelEvent.GetPooled(new Event { type = EventType.ScrollWheel,
                mousePosition = point + new Vector2(80, 0), delta = new Vector2(0, -3) }))
            { wheel.target = Map; Map.SendEvent(wheel); }
            yield return new WaitForSecondsRealtime(1.5f);
            Assert.That(viewports.Last().LongitudeSpan, Is.LessThan(before.LongitudeSpan));
            Assert.That(viewports.Last().IsValid, Is.True);
        }
    }
}
