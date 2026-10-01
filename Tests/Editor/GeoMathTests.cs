using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Zabaglione.PlateauAreaDownloader.Editor.Tests
{
    public class GeoMathTests
    {
        [Test]
        public void FromCoordinate_ReturnsExpectedJapaneseThirdLevelMeshCodes()
        {
            Assert.That(JapanMeshCode.FromCoordinate(35.6586, 139.7454), Is.EqualTo("53393599"));
            Assert.That(JapanMeshCode.FromCoordinate(34.9949, 135.7850), Is.EqualTo("52353692"));
            Assert.That(JapanMeshCode.FromCoordinate(43.0687, 141.3508), Is.EqualTo("64414288"));
        }

        [Test]
        public void GetBounds_ContainsCoordinateAndHasThirdLevelDimensions()
        {
            GeoBounds bounds = JapanMeshCode.GetBounds("53393599");

            Assert.That(bounds.Contains(35.6586, 139.7454), Is.True);
            Assert.That(bounds.North - bounds.South, Is.EqualTo(1.0 / 120.0).Within(1e-12));
            Assert.That(bounds.East - bounds.West, Is.EqualTo(1.0 / 80.0).Within(1e-12));
        }

        [Test]
        public void CatalogBounds_DistinguishesSecondLevelDemQuarters()
        {
            var southwest = JapanMeshCode.GetCatalogBounds("644142",
                "https://example.test/udx/dem/644142_dem_6697_00_op.gml");
            var northeast = JapanMeshCode.GetCatalogBounds("644142",
                "https://example.test/udx/dem/644142_dem_6697_55_op.gml");
            Assert.That(southwest.East, Is.EqualTo(northeast.West).Within(1e-12));
            Assert.That(southwest.North, Is.EqualTo(northeast.South).Within(1e-12));
            Assert.That(northeast.Contains(43.0687, 141.3508), Is.True);
        }

        [Test]
        public void EnumerateIntersecting_ExcludesAdjacentCellAtDecimalBoundary()
        {
            var bounds = new GeoBounds(141.35, 43.068, 141.351, 43.069);
            CollectionAssert.AreEqual(new[] { "64414288" },
                JapanMeshCode.EnumerateIntersecting(bounds, includeBoundary: false));
        }

        [Test]
        public void EnumerateIntersecting_IncludesCellsTouchingAnExactMeshBoundary()
        {
            GeoBounds cell = JapanMeshCode.GetBounds("53393599");
            GeoBounds sharedEdge = new GeoBounds(
                cell.East,
                cell.South + 0.001,
                cell.East,
                cell.North - 0.001);
            List<string> codes = new List<string>(JapanMeshCode.EnumerateIntersecting(sharedEdge));

            CollectionAssert.AreEquivalent(new[] { "53393599", "53393690" }, codes);
            Assert.That(JapanMeshCode.EnumerateIntersecting(sharedEdge, includeBoundary: false), Is.Empty);
        }

        [Test]
        public void FromCenter_CreatesAOneKilometerSquareAtCenterLatitude()
        {
            const double latitude = 35.6586;
            const double longitude = 139.7454;
            GeoBounds bounds = GeoBounds.FromCenter(latitude, longitude);

            Assert.That((bounds.West + bounds.East) * 0.5, Is.EqualTo(longitude).Within(1e-12));
            Assert.That((bounds.South + bounds.North) * 0.5, Is.EqualTo(latitude).Within(1e-12));

            double radians = latitude * (Math.PI / 180.0);
            const double semiMajorAxisMeters = 6378137.0;
            const double flattening = 1.0 / 298.257223563;
            double eccentricitySquared = flattening * (2.0 - flattening);
            double denominator = Math.Sqrt(1.0 - eccentricitySquared * Math.Sin(radians) * Math.Sin(radians));
            double primeVerticalRadius = semiMajorAxisMeters / denominator;
            double meridionalRadius = semiMajorAxisMeters * (1.0 - eccentricitySquared)
                / (denominator * denominator * denominator);
            double widthMeters = (bounds.East - bounds.West) * (Math.PI / 180.0)
                * primeVerticalRadius * Math.Cos(radians);
            double heightMeters = (bounds.North - bounds.South) * (Math.PI / 180.0) * meridionalRadius;

            Assert.That(widthMeters, Is.EqualTo(1000.0).Within(1e-8));
            Assert.That(heightMeters, Is.EqualTo(1000.0).Within(1e-8));
        }

        [Test]
        public void GeoBounds_RejectsInvalidRanges()
        {
            Assert.Throws<ArgumentException>(() => new GeoBounds(139.0, 36.0, 140.0, 35.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GeoBounds(-181.0, 35.0, 139.0, 36.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => GeoBounds.FromCenter(35.0, 139.0, 0.0));
        }

        [Test]
        public void WheelZoom_UsesFractionalStepsForSmallEvents()
        {
            Assert.That(MapZoomMath.ApplyWheel(-0.5f, 15, 5, 18), Is.EqualTo(15.0625));
            Assert.That(MapZoomMath.ApplyWheel(-1f, 15.0625, 5, 18), Is.EqualTo(15.1875));
        }

        [Test]
        public void WheelZoom_LimitsOneLargeEvent()
        {
            Assert.That(MapZoomMath.ApplyWheel(100, 15, 5, 18), Is.EqualTo(14.75));
            Assert.That(MapZoomMath.ApplyWheel(-100, 15, 5, 18), Is.EqualTo(15.25));
        }

        [Test]
        public void WheelZoom_RespondsToIndividualTicks()
        {
            var zoom = 15d;
            for (var i = 0; i < 3; i++) zoom = MapZoomMath.ApplyWheel(-1, zoom, 5, 18);
            Assert.That(zoom, Is.EqualTo(15.375));
        }

        [Test]
        public void WheelZoom_SensitivityScalesTheSameInput()
        {
            Assert.That(MapZoomMath.ApplyWheel(-1, 15, 5, 18, 0.5), Is.EqualTo(15.0625));
            Assert.That(MapZoomMath.ApplyWheel(-1, 15, 5, 18, 2), Is.EqualTo(15.25));
            Assert.That(MapZoomMath.ApplyWheel(100, 15, 5, 18, 3), Is.EqualTo(14.25));
        }

        [Test]
        public void WheelZoom_ReversesWithoutStoredRemainder()
        {
            var zoom = MapZoomMath.ApplyWheel(-0.5f, 15, 5, 18);
            Assert.That(MapZoomMath.ApplyWheel(0.5f, zoom, 5, 18), Is.EqualTo(15));
        }

        [Test]
        public void WheelZoom_ClampsAtZoomLimitsAndIgnoresInvalidInput()
        {
            Assert.That(MapZoomMath.ApplyWheel(-100, 18, 5, 18), Is.EqualTo(18));
            Assert.That(MapZoomMath.ApplyWheel(100, 5, 5, 18), Is.EqualTo(5));
            Assert.That(MapZoomMath.ApplyWheel(float.NaN, 15, 5, 18), Is.EqualTo(15));
        }

        [Test]
        public void FractionalZoom_PreservesTileScaleAndCoordinateRoundTrip()
        {
            Assert.That(MapZoomMath.TileZoom(15.5), Is.EqualTo(15));
            Assert.That(MapZoomMath.TileSize(15.5), Is.EqualTo(256 * Math.Sqrt(2)).Within(0.00001));
            Assert.That(MapZoomMath.WorldScale(16), Is.EqualTo(MapZoomMath.WorldScale(15) * 2));
            var world = MapZoomMath.ToWorld(35.7100, 139.8100, 15.37);
            var geo = MapZoomMath.FromWorld(world.x, world.y, 15.37);
            Assert.That(geo.latitude, Is.EqualTo(35.7100).Within(0.00000001));
            Assert.That(geo.longitude, Is.EqualTo(139.8100).Within(0.00000001));
        }

        [Test]
        public void FractionalZoom_KeepsPointerAnchorFixed()
        {
            var anchor = MapZoomMath.ToWorld(35.7100, 139.8100, 15.2);
            const double pointerX = 200;
            const double pointerY = 120;
            const double width = 900;
            const double height = 430;
            var center = MapZoomMath.FromWorld(anchor.x - pointerX + width / 2,
                anchor.y - pointerY + height / 2, 15.2);
            var newAnchor = MapZoomMath.ToWorld(35.7100, 139.8100, 15.8);
            var newCenter = MapZoomMath.FromWorld(newAnchor.x - pointerX + width / 2,
                newAnchor.y - pointerY + height / 2, 15.8);
            var centerWorld = MapZoomMath.ToWorld(newCenter.latitude, newCenter.longitude, 15.8);
            Assert.That(newAnchor.x - centerWorld.x + width / 2, Is.EqualTo(pointerX).Within(0.000001));
            Assert.That(newAnchor.y - centerWorld.y + height / 2, Is.EqualTo(pointerY).Within(0.000001));
            Assert.That(center.latitude, Is.Not.EqualTo(newCenter.latitude));
        }
    }
}
