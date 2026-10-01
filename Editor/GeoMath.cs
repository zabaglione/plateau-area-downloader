using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Zabaglione.PlateauAreaDownloader.Editor
{
    /// <summary>A longitude/latitude aligned rectangle in degrees.</summary>
    public readonly struct GeoBounds : IEquatable<GeoBounds>
    {
        private const double MinimumLongitude = -180.0;
        private const double MaximumLongitude = 180.0;
        private const double MinimumLatitude = -90.0;
        private const double MaximumLatitude = 90.0;

        public double West { get; }
        public double South { get; }
        public double East { get; }
        public double North { get; }

        public GeoBounds(double west, double south, double east, double north)
        {
            if (!IsFinite(west) || west < MinimumLongitude || west > MaximumLongitude)
            {
                throw new ArgumentOutOfRangeException(nameof(west), "Longitude must be finite and between -180 and 180 degrees.");
            }

            if (!IsFinite(east) || east < MinimumLongitude || east > MaximumLongitude)
            {
                throw new ArgumentOutOfRangeException(nameof(east), "Longitude must be finite and between -180 and 180 degrees.");
            }

            if (!IsFinite(south) || south < MinimumLatitude || south > MaximumLatitude)
            {
                throw new ArgumentOutOfRangeException(nameof(south), "Latitude must be finite and between -90 and 90 degrees.");
            }

            if (!IsFinite(north) || north < MinimumLatitude || north > MaximumLatitude)
            {
                throw new ArgumentOutOfRangeException(nameof(north), "Latitude must be finite and between -90 and 90 degrees.");
            }

            if (south > north)
            {
                throw new ArgumentException("South must not be north of north.", nameof(south));
            }

            West = west;
            South = south;
            East = east;
            North = north;
        }

        /// <summary>West greater than east represents a rectangle crossing the antimeridian.</summary>
        public bool CrossesAntimeridian => West > East;
        public double LongitudeSpan => CrossesAntimeridian ? East - West + 360.0 : East - West;
        public double CenterLongitude => NormalizeLongitude(West + LongitudeSpan * 0.5);

        /// <summary>Wraps a finite longitude into [-180, 180).</summary>
        public static double NormalizeLongitude(double longitude)
        {
            if (!IsFinite(longitude)) throw new ArgumentOutOfRangeException(nameof(longitude));
            var wrapped = longitude % 360.0;
            if (wrapped < -180.0) wrapped += 360.0;
            if (wrapped >= 180.0) wrapped -= 360.0;
            return wrapped;
        }

        /// <summary>Creates bounds from ordered, potentially unwrapped longitude endpoints.</summary>
        public static GeoBounds FromUnwrapped(double west, double south, double east, double north)
        {
            if (!IsFinite(west) || !IsFinite(east) || east < west)
                throw new ArgumentException("Unwrapped longitudes must be finite and ordered.");
            var span = east - west;
            if (span >= 360.0) return new GeoBounds(-180.0, south, 180.0, north);
            var normalizedWest = NormalizeLongitude(west);
            var normalizedEast = NormalizeLongitude(east);
            // Preserve the eastern edge at +180 without turning a zero-width seam into the whole world.
            if (normalizedEast == -180.0 && span > 0) normalizedEast = 180.0;
            return new GeoBounds(normalizedWest, south, normalizedEast, north);
        }

        /// <summary>Returns one or two ordered rectangles suitable for APIs requiring west less than east.</summary>
        public GeoBounds[] SplitAtAntimeridian()
        {
            Validate();
            if (!CrossesAntimeridian) return new[] { this };
            if (LongitudeSpan == 0) return new[] { new GeoBounds(West, South, West, North) };
            return new[] { new GeoBounds(West, South, 180.0, North), new GeoBounds(-180.0, South, East, North) }
                .Where(part => part.LongitudeSpan > 0).ToArray();
        }

        /// <summary>True when this value contains finite geographic coordinates.</summary>
        public bool IsValid
        {
            get
            {
                return IsFinite(West) && IsFinite(South) && IsFinite(East) && IsFinite(North)
                    && West >= MinimumLongitude && West <= MaximumLongitude
                    && East >= MinimumLongitude && East <= MaximumLongitude
                    && South >= MinimumLatitude && South <= MaximumLatitude
                    && North >= MinimumLatitude && North <= MaximumLatitude
                    && South <= North;
            }
        }

        /// <summary>Throws when this value is invalid.</summary>
        public void Validate()
        {
            if (!IsValid)
            {
                throw new ArgumentException("Geo bounds must contain finite geographic coordinates and ordered latitudes.");
            }
        }

        /// <summary>Creates a square of the given side length, in meters, around a latitude/longitude center.</summary>
        public static GeoBounds FromCenter(double latitude, double longitude, double sideLengthMeters = 1000.0)
        {
            if (!IsFinite(latitude) || latitude < MinimumLatitude || latitude > MaximumLatitude)
            {
                throw new ArgumentOutOfRangeException(nameof(latitude), "Latitude must be finite and between -90 and 90 degrees.");
            }

            if (!IsFinite(longitude) || longitude < MinimumLongitude || longitude > MaximumLongitude)
            {
                throw new ArgumentOutOfRangeException(nameof(longitude), "Longitude must be finite and between -180 and 180 degrees.");
            }

            if (!IsFinite(sideLengthMeters) || sideLengthMeters <= 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(sideLengthMeters), "Side length must be a positive finite number of meters.");
            }

            const double semiMajorAxisMeters = 6378137.0;
            const double flattening = 1.0 / 298.257223563;
            double eccentricitySquared = flattening * (2.0 - flattening);
            double latitudeRadians = ToRadians(latitude);
            double sinLatitude = Math.Sin(latitudeRadians);
            double denominator = Math.Sqrt(1.0 - eccentricitySquared * sinLatitude * sinLatitude);
            double primeVerticalRadius = semiMajorAxisMeters / denominator;
            double meridionalRadius = semiMajorAxisMeters * (1.0 - eccentricitySquared)
                / (denominator * denominator * denominator);
            double eastWestScale = primeVerticalRadius * Math.Cos(latitudeRadians);

            if (eastWestScale <= 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(latitude), "A square cannot have an east-west extent at a pole.");
            }

            double halfLatitudeSpan = (sideLengthMeters / meridionalRadius) * 0.5;
            double halfLongitudeSpan = (sideLengthMeters / eastWestScale) * 0.5;
            return FromUnwrapped(
                longitude - ToDegrees(halfLongitudeSpan),
                Math.Max(MinimumLatitude, latitude - ToDegrees(halfLatitudeSpan)),
                longitude + ToDegrees(halfLongitudeSpan),
                Math.Min(MaximumLatitude, latitude + ToDegrees(halfLatitudeSpan)));
        }

        /// <summary>Tests whether a latitude/longitude point is inside this rectangle.</summary>
        public bool Contains(double latitude, double longitude, bool includeBoundary = true)
        {
            Validate();
            if (!IsFinite(latitude) || !IsFinite(longitude)) return false;
            return (includeBoundary ? latitude >= South && latitude <= North : latitude > South && latitude < North)
                && ContainsLongitude(longitude, includeBoundary);
        }

        private bool ContainsLongitude(double longitude, bool includeBoundary)
        {
            if (LongitudeSpan >= 360.0) return true;
            var offset = NormalizeLongitude(longitude - West);
            if (offset < 0) offset += 360.0;
            return includeBoundary ? offset <= LongitudeSpan : offset > 0 && offset < LongitudeSpan;
        }

        /// <summary>Tests whether two rectangles overlap, optionally counting touching edges.</summary>
        public bool Intersects(GeoBounds other, bool includeBoundary = true)
        {
            Validate();
            other.Validate();
            if (includeBoundary ? South > other.North || North < other.South : South >= other.North || North <= other.South)
                return false;
            foreach (var a in SplitAtAntimeridian())
            foreach (var b in other.SplitAtAntimeridian())
                if (includeBoundary ? a.West <= b.East && a.East >= b.West : a.West < b.East && a.East > b.West)
                    return true;
            return includeBoundary && ContainsLongitude(180.0, true) && other.ContainsLongitude(180.0, true);
        }

        public bool Equals(GeoBounds other)
        {
            return West.Equals(other.West) && South.Equals(other.South)
                && East.Equals(other.East) && North.Equals(other.North);
        }

        public override bool Equals(object obj)
        {
            return obj is GeoBounds && Equals((GeoBounds)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = West.GetHashCode();
                hash = (hash * 397) ^ South.GetHashCode();
                hash = (hash * 397) ^ East.GetHashCode();
                hash = (hash * 397) ^ North.GetHashCode();
                return hash;
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static double ToRadians(double degrees)
        {
            return degrees * (Math.PI / 180.0);
        }

        private static double ToDegrees(double radians)
        {
            return radians * (180.0 / Math.PI);
        }
    }

    /// <summary>Converts coordinates and rectangles to Japan's eight-digit third-level mesh codes.</summary>
    public static class JapanMeshCode
    {
        private const double RowsPerDegree = 120.0;
        private const double ColumnsPerDegree = 80.0;
        private const double MeshSouthLimit = 0.0;
        private const double MeshNorthLimit = 200.0 / 3.0;
        private const double MeshWestLimit = 100.0;
        private const double MeshEastLimit = 200.0;
        private const int MaximumCellIndex = 7999;

        /// <summary>Returns the third-level mesh containing a latitude/longitude point.</summary>
        public static string FromCoordinate(double latitude, double longitude)
        {
            ValidateCoordinate(latitude, longitude);
            int row = (int)Math.Floor(latitude * RowsPerDegree);
            int column = (int)Math.Floor((longitude - MeshWestLimit) * ColumnsPerDegree);
            return Encode(row, column);
        }

        /// <summary>Returns false instead of throwing when the point is outside the supported mesh-code domain.</summary>
        public static bool TryFromCoordinate(double latitude, double longitude, out string meshCode)
        {
            meshCode = null;
            if (!IsFinite(latitude) || !IsFinite(longitude)
                || latitude < MeshSouthLimit || latitude >= MeshNorthLimit
                || longitude < MeshWestLimit || longitude >= MeshEastLimit)
            {
                return false;
            }

            meshCode = FromCoordinate(latitude, longitude);
            return true;
        }

        /// <summary>Returns the geographic rectangle represented by an eight-digit third-level code.</summary>
        public static GeoBounds GetBounds(string meshCode)
        {
            int row;
            int column;
            Parse(meshCode, out row, out column);

            double south = row / RowsPerDegree;
            double west = MeshWestLimit + column / ColumnsPerDegree;
            return new GeoBounds(west, south, west + (1.0 / ColumnsPerDegree), south + (1.0 / RowsPerDegree));
        }

        /// <summary>Returns the extent of a catalog mesh, which may be second- or third-level.</summary>
        public static GeoBounds GetCatalogBounds(string meshCode, string url = null)
        {
            if (meshCode != null && meshCode.Length == 6)
            {
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    var name = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
                    var parts = name.Split('_');
                    for (var i = 0; i + 2 < parts.Length; i++)
                    {
                        if (parts[i] != "dem" || parts[i + 2].Length != 2) continue;
                        var quadrant = parts[i + 2];
                        if ((quadrant[0] == '0' || quadrant[0] == '5') &&
                            (quadrant[1] == '0' || quadrant[1] == '5'))
                        {
                            var corner = GetBounds(meshCode + quadrant);
                            return new GeoBounds(corner.West, corner.South,
                                corner.West + 5.0 / ColumnsPerDegree,
                                corner.South + 5.0 / RowsPerDegree);
                        }
                    }
                }
                var southwest = GetBounds(meshCode + "00");
                return new GeoBounds(southwest.West, southwest.South,
                    southwest.West + 10.0 / ColumnsPerDegree,
                    southwest.South + 10.0 / RowsPerDegree);
            }
            return GetBounds(meshCode);
        }

        /// <summary>
        /// Lazily enumerates every third-level cell that intersects the bounds. Touching edges count by default.
        /// </summary>
        public static IEnumerable<string> EnumerateIntersecting(GeoBounds bounds, bool includeBoundary = true)
        {
            bounds.Validate();
            if (bounds.CrossesAntimeridian)
                return bounds.SplitAtAntimeridian().SelectMany(part => EnumerateIntersecting(part, includeBoundary)).Distinct();

            double minimumRowValue = SnapMeshBoundary(bounds.South * RowsPerDegree);
            double maximumRowValue = SnapMeshBoundary(bounds.North * RowsPerDegree);
            double minimumColumnValue = SnapMeshBoundary((bounds.West - MeshWestLimit) * ColumnsPerDegree);
            double maximumColumnValue = SnapMeshBoundary((bounds.East - MeshWestLimit) * ColumnsPerDegree);

            int firstRow;
            int lastRow;
            int firstColumn;
            int lastColumn;
            if (includeBoundary)
            {
                firstRow = (int)Math.Ceiling(minimumRowValue) - 1;
                lastRow = (int)Math.Floor(maximumRowValue);
                firstColumn = (int)Math.Ceiling(minimumColumnValue) - 1;
                lastColumn = (int)Math.Floor(maximumColumnValue);
            }
            else
            {
                firstRow = (int)Math.Floor(minimumRowValue);
                lastRow = (int)Math.Ceiling(maximumRowValue) - 1;
                firstColumn = (int)Math.Floor(minimumColumnValue);
                lastColumn = (int)Math.Ceiling(maximumColumnValue) - 1;
            }

            if (lastRow < 0 || firstRow > MaximumCellIndex
                || lastColumn < 0 || firstColumn > MaximumCellIndex)
            {
                return new string[0];
            }

            firstRow = Math.Max(firstRow, 0);
            lastRow = Math.Min(lastRow, MaximumCellIndex);
            firstColumn = Math.Max(firstColumn, 0);
            lastColumn = Math.Min(lastColumn, MaximumCellIndex);

            if (firstRow > lastRow || firstColumn > lastColumn)
            {
                return new string[0];
            }

            return EnumerateCells(firstRow, lastRow, firstColumn, lastColumn);
        }

        private static IEnumerable<string> EnumerateCells(int firstRow, int lastRow, int firstColumn, int lastColumn)
        {
            for (int row = firstRow; row <= lastRow; row++)
            {
                for (int column = firstColumn; column <= lastColumn; column++)
                {
                    yield return Encode(row, column);
                }
            }
        }

        private static double SnapMeshBoundary(double value)
        {
            var rounded = Math.Round(value);
            return Math.Abs(value - rounded) < 1e-9 ? rounded : value;
        }

        private static void ValidateCoordinate(double latitude, double longitude)
        {
            if (!IsFinite(latitude) || latitude < MeshSouthLimit || latitude >= MeshNorthLimit)
            {
                throw new ArgumentOutOfRangeException(nameof(latitude), "Latitude must be in the mesh-code range [0, 200/3) degrees.");
            }

            if (!IsFinite(longitude) || longitude < MeshWestLimit || longitude >= MeshEastLimit)
            {
                throw new ArgumentOutOfRangeException(nameof(longitude), "Longitude must be in the mesh-code range [100, 200) degrees.");
            }
        }

        private static string Encode(int row, int column)
        {
            int primaryLatitude = row / 80;
            int latitudeRemainder = row % 80;
            int secondLatitude = latitudeRemainder / 10;
            int thirdLatitude = latitudeRemainder % 10;

            int primaryLongitude = column / 80;
            int longitudeRemainder = column % 80;
            int secondLongitude = longitudeRemainder / 10;
            int thirdLongitude = longitudeRemainder % 10;

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:00}{1:00}{2}{3}{4}{5}",
                primaryLatitude,
                primaryLongitude,
                secondLatitude,
                secondLongitude,
                thirdLatitude,
                thirdLongitude);
        }

        private static void Parse(string meshCode, out int row, out int column)
        {
            if (meshCode == null)
            {
                throw new ArgumentNullException(nameof(meshCode));
            }

            if (meshCode.Length != 8)
            {
                throw new ArgumentException("A third-level mesh code must contain exactly eight digits.", nameof(meshCode));
            }

            for (int index = 0; index < meshCode.Length; index++)
            {
                if (meshCode[index] < '0' || meshCode[index] > '9')
                {
                    throw new ArgumentException("A mesh code may contain only ASCII digits.", nameof(meshCode));
                }
            }

            int primaryLatitude = ParseDigits(meshCode, 0, 2);
            int primaryLongitude = ParseDigits(meshCode, 2, 2);
            int secondLatitude = meshCode[4] - '0';
            int secondLongitude = meshCode[5] - '0';
            int thirdLatitude = meshCode[6] - '0';
            int thirdLongitude = meshCode[7] - '0';

            if (secondLatitude > 7 || secondLongitude > 7)
            {
                throw new ArgumentException("Second-level mesh digits must be between 0 and 7.", nameof(meshCode));
            }

            row = (primaryLatitude * 80) + (secondLatitude * 10) + thirdLatitude;
            column = (primaryLongitude * 80) + (secondLongitude * 10) + thirdLongitude;
        }

        private static int ParseDigits(string value, int start, int length)
        {
            int parsed = 0;
            for (int index = start; index < start + length; index++)
            {
                parsed = (parsed * 10) + (value[index] - '0');
            }

            return parsed;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
