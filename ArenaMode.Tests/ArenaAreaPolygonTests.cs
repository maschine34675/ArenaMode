using ArenaMode.Shared;
using Xunit;

namespace ArenaMode.Tests;

public sealed class ArenaAreaPolygonTests
{
    private static ArenaAreaVertex V(float x, float z) => new(x, z);
    private static ArenaAreaPoint P(float x, float z, float y = 1) => new(x, y, z);
    private static ArenaAreaVertex[] Elbow() => new[]
    { V(0, 0), V(6, 0), V(6, 2), V(2, 2), V(2, 6), V(0, 6) };
    private static ArenaAreaBounds Polygon() => new(Elbow(), 0, 8);

    [Fact]
    public void SharedSpawnPolicyUsesContourClearanceAndSeparateFootHeightBand()
    {
        var area = new ArenaAreaDefinition("test-polygon-v1", "test", "bigmap", Polygon(), .5f, 3, 2);
        Assert.True(area.AllowsSpawn(1, .5f, 5));
        Assert.True(area.AllowsSpawn(5, 3, 1));
        Assert.False(area.AllowsSpawn(4, 1, 4)); // concave void inside the extents
        Assert.False(area.AllowsSpawn(.7f, 1, 5)); // insufficient horizontal clearance
        Assert.False(area.AllowsSpawn(1, .49f, 5)); // volume is valid, spawn floor is not
        Assert.False(area.AllowsSpawn(1, 3.01f, 5));
        Assert.True(area.Bounds.Contains(1, 7, 5)); // ordinary vertical movement has separate headroom
    }

    [Fact]
    public void ConcaveMembershipUsesContourRatherThanBoundingBox()
    {
        var bounds = Polygon();
        Assert.True(bounds.IsValid);
        Assert.Equal((0f, 0f, 6f, 6f), (bounds.MinX, bounds.MinZ, bounds.MaxX, bounds.MaxZ));
        Assert.True(bounds.Contains(1, 0, 5));
        Assert.True(bounds.Contains(5, 8, 1));
        Assert.True(bounds.Contains(2, 4, 2));
        Assert.False(bounds.Contains(4, 4, 4));
        Assert.False(bounds.Contains(1, -0.001f, 1));
        Assert.False(bounds.Contains(1, 8.001f, 1));
        Assert.False(bounds.Contains(-0.001f, 1, 1));
        Assert.False(bounds.Contains(6.001f, 1, 1));
    }

    [Fact]
    public void ConcaveVoidCannotBeCrossedBeforeFirstOrBetweenLaterCorners()
    {
        var bounds = Polygon();
        var upper = P(1, 5); var right = P(5, 1);
        Assert.True(bounds.Contains(upper.X, upper.Y, upper.Z));
        Assert.True(bounds.Contains(right.X, right.Y, right.Z));
        Assert.False(bounds.ContainsPath(upper, new[] { right }, true));
        Assert.False(bounds.ContainsPath(P(1, 1), new[] { upper, right, P(1, 1) }, true));
        Assert.True(bounds.ContainsPath(upper, new[] { P(1, 1), right }, true, 0.5f));
        Assert.False(bounds.ContainsPath(upper, new[] { P(1, 1), right }, false));
    }

    [Fact]
    public void NarrowExcursionIsFoundWithoutFixedSpacingSampling()
    {
        var bounds = new ArenaAreaBounds(new[]
        {
            V(0, 0), V(1000, 0), V(1000, 10), V(5.00002f, 10),
            V(5.00002f, 2), V(5, 2), V(5, 10), V(0, 10)
        }, 0, 8);
        Assert.True(bounds.Contains(1, 1, 5));
        Assert.True(bounds.Contains(999, 1, 5));
        Assert.False(bounds.ContainsPath(P(1, 5), new[] { P(999, 5) }, true));
        Assert.False(bounds.ContainsPath(P(999, 5), new[] { P(1, 5) }, true));
        Assert.True(bounds.ContainsPath(P(1, 5), new[] { P(1, 1), P(999, 1), P(999, 5) }, true, 0.4f));
    }

    [Fact]
    public void DiagonalConcavityAlsoRejectsAnInteriorEndpointsShortcut()
    {
        var outline = Elbow().Select(v => V(v.X + v.Z, v.Z - v.X)).ToArray();
        var bounds = new ArenaAreaBounds(outline, -2, 8);
        Assert.True(bounds.Contains(6, 1, 4));
        Assert.True(bounds.Contains(6, 1, -4));
        Assert.False(bounds.Contains(6, 1, 0));
        Assert.False(bounds.ContainsPath(P(6, 4), new[] { P(6, -4) }, true));
        Assert.True(bounds.ContainsPath(P(6, 4), new[] { P(2, 0), P(6, -4) }, true, 0.5f));
    }

    [Fact]
    public void PositiveInsetAppliesToEntireSegmentNearReflexCorner()
    {
        var bounds = Polygon();
        var from = P(1, 2.5f); var to = P(2.5f, 1);
        Assert.True(bounds.Contains(from.X, from.Y, from.Z, 0.4f));
        Assert.True(bounds.Contains(to.X, to.Y, to.Z, 0.4f));
        Assert.True(bounds.ContainsPath(from, new[] { to }, true));
        Assert.True(bounds.ContainsPath(from, new[] { to }, true, 0.3f));
        // The middle passes only 0.5 / sqrt(2) from the concave corner (2,2).
        Assert.False(bounds.ContainsPath(from, new[] { to }, true, 0.4f));
        Assert.False(bounds.ContainsPath(to, new[] { from }, true, 0.4f));
    }

    [Fact]
    public void InsetIsEuclideanForSlopingEdgesAndDoesNotShrinkHeight()
    {
        var diamond = new ArenaAreaBounds(new[] { V(0, -4), V(4, 0), V(0, 4), V(-4, 0) }, -1, 7);
        Assert.True(diamond.Contains(2, -1, 0, 1.4f));
        Assert.True(diamond.Contains(2, 7, 0, 1.4f));
        Assert.False(diamond.Contains(2, 1, 0, 1.5f));
        Assert.True(diamond.Contains(0, 1, 0, 2.8f));
        Assert.False(diamond.Contains(0, 1, 0, 2.9f));
        Assert.True(diamond.ContainsPath(P(-2, 0), new[] { P(2, 0) }, true, 1.4f));
        Assert.False(diamond.ContainsPath(P(-2, 0), new[] { P(2, 0) }, true, 1.5f));
    }

    [Fact]
    public void BoundaryTravelAndReflexTangencyAreAllowedOnlyAtZeroInset()
    {
        var bounds = Polygon();
        Assert.True(bounds.ContainsPath(P(0, 0), new[] { P(6, 0), P(6, 2), P(2, 2), P(2, 6) }, true));
        Assert.True(bounds.ContainsPath(P(1, 3), new[] { P(3, 1) }, true));
        Assert.True(bounds.ContainsPath(P(3, 1), new[] { P(1, 3) }, true));
        Assert.False(bounds.ContainsPath(P(1, 3), new[] { P(3, 1) }, true, 0.001f));
        Assert.False(bounds.ContainsPath(P(2, 6), new[] { P(6, 2) }, true));
        Assert.False(bounds.ContainsPath(P(0, 0), new[] { P(6, 0) }, true, 0.001f));
        Assert.True(bounds.ContainsPath(P(1, 1, 0), new[] { P(1, 1, 8) }, true, 1));
    }

    [Fact]
    public void StraightBoundaryIntermediateVerticesAreValidAndDoNotCreateZeroAreaCaps()
    {
        var outline = new[] { V(0, 0), V(2, 0), V(4, 0), V(4, 2), V(4, 4), V(0, 4), V(0, 2) };
        var bounds = new ArenaAreaBounds(outline, 0, 8);
        Assert.Equal(outline.Length, bounds.Outline.Count);
        Assert.True(bounds.ContainsPath(P(0, 0), new[] { P(4, 0), P(4, 4) }, true));
        Assert.True(bounds.ContainsPath(P(1, 1), new[] { P(3, 3) }, true, 1));
        AssertCaps(bounds, 16);
    }

    [Fact]
    public void ClockwiseInputNormalizesToCounterclockwiseWithSameShapeAndCaps()
    {
        var ccw = Polygon();
        var clockwise = new ArenaAreaBounds(Elbow().Reverse().ToArray(), 0, 8);
        Assert.True(SignedArea(clockwise.Outline) > 0);
        Assert.Equal(ccw.Outline.ToArray(), clockwise.Outline.ToArray());
        AssertCaps(ccw, 20);
        AssertCaps(clockwise, 20);
        for (float x = -0.5f; x <= 6.5f; x += 0.5f)
            for (float z = -0.5f; z <= 6.5f; z += 0.5f)
                Assert.Equal(ccw.Contains(x, 1, z), clockwise.Contains(x, 1, z));
    }

    [Fact]
    public void ConcaveCapsCoverExactlyTheContourWithoutFillingOrOverlappingTheNotch()
    {
        var bounds = Polygon();
        AssertCaps(bounds, 20);
        // Independent shape oracle, off the integer/diagonal triangle edges.
        for (int xi = -1; xi <= 24; xi++)
            for (int zi = -1; zi <= 24; zi++)
            {
                double x = xi * 0.25 + 0.071, z = zi * 0.25 + 0.113;
                bool expected = x >= 0 && z >= 0 && x <= 6 && z <= 6 && (x <= 2 || z <= 2);
                Assert.Equal(expected ? 1 : 0, TriangleCoverage(bounds, x, z));
            }
    }

    [Fact]
    public void MultipleNotchesAndCollinearPointsStillProduceExactNonoverlappingCaps()
    {
        // Two deep exterior fingers; no hole is encoded in the single contour.
        var outline = new[]
        {
            V(0, 0), V(5, 0), V(10, 0), V(10, 10), V(8, 10), V(8, 2),
            V(6, 2), V(6, 10), V(4, 10), V(4, 2), V(2, 2), V(2, 10), V(0, 10)
        };
        foreach (var vertices in new[] { outline, outline.Reverse().ToArray() })
        {
            var bounds = new ArenaAreaBounds(vertices, -2, 8);
            AssertCaps(bounds, 68);
            for (int xi = 0; xi < 40; xi++)
                for (int zi = 0; zi < 40; zi++)
                {
                    double x = xi * 0.25 + 0.071, z = zi * 0.25 + 0.113;
                    bool expected = z < 2 || !(x > 2 && x < 4 || x > 6 && x < 8);
                    Assert.Equal(expected ? 1 : 0, TriangleCoverage(bounds, x, z));
                }
        }
    }

    [Fact]
    public void ConstructorCopiesInputAndExposesOnlyReadOnlyOutlineAndTriangles()
    {
        var source = Elbow();
        var bounds = new ArenaAreaBounds(source, 0, 8);
        var copy = bounds;
        var originalFirst = bounds.Outline[0];
        source[0] = V(100, 100);
        Assert.Equal(originalFirst, bounds.Outline[0]);
        Assert.Equal(originalFirst, copy.Outline[0]);
        Assert.True(bounds.Contains(1, 1, 1));
        Assert.False(bounds.Contains(4, 1, 4));
        var outline = Assert.IsAssignableFrom<IList<ArenaAreaVertex>>(bounds.Outline);
        var triangles = Assert.IsAssignableFrom<IList<int>>(bounds.FloorTriangles);
        Assert.True(outline.IsReadOnly);
        Assert.True(triangles.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => outline[0] = V(100, 100));
        Assert.Throws<NotSupportedException>(() => triangles[0] = 123);
        AssertCaps(bounds, 20);
    }

    [Fact]
    public void Exactly64VerticesAreAcceptedAndLargerOutlinesRejected()
    {
        var vertices = Enumerable.Range(0, 64)
            .Select(i => V((float)(10 * Math.Cos(i * Math.PI / 32)), (float)(10 * Math.Sin(i * Math.PI / 32))))
            .ToArray();
        var bounds = new ArenaAreaBounds(vertices, 0, 8);
        Assert.Equal(64, bounds.Outline.Count);
        Assert.True(bounds.Contains(0, 1, 0, 9));
        AssertCaps(bounds, SignedArea(vertices));
        var tooMany = Enumerable.Range(0, 65)
            .Select(i => V((float)Math.Cos(i * Math.PI * 2 / 65), (float)Math.Sin(i * Math.PI * 2 / 65)))
            .ToArray();
        Assert.Throws<ArgumentException>(() => new ArenaAreaBounds(tooMany, 0, 8));
    }

    public static IEnumerable<object[]> InvalidOutlines()
    {
        yield return new object[] { Array.Empty<ArenaAreaVertex>() };
        yield return new object[] { new[] { V(0, 0), V(1, 1) } };
        yield return new object[] { new[] { V(0, 0), V(1, 0), V(2, 0) } };
        yield return new object[] { new[] { V(0, 0), V(4, 0), V(4, 4), V(0, 0) } }; // closing duplicate
        yield return new object[] { new[] { V(0, 0), V(4, 0), V(4, 0), V(0, 4) } }; // zero edge
        yield return new object[] { new[] { V(0, 0), V(4, 0), V(2, 0), V(2, 4), V(0, 4) } }; // adjacent overlap
        yield return new object[] { new[] { V(0, 0), V(4, 4), V(0, 4), V(4, 0) } }; // crossing
        yield return new object[] { new[] { V(0, 0), V(6, 0), V(6, 6), V(3, 0), V(0, 6) } }; // vertex touches edge
        yield return new object[] { new[] { V(0, 0), V(6, 0), V(6, 6), V(2, 6), V(2, 0), V(4, 0), V(4, 4), V(0, 4) } }; // nonadjacent overlap
        yield return new object[] { new[] { V(0, 0), V(float.NaN, 0), V(0, 4) } };
        yield return new object[] { new[] { V(0, 0), V(4, 0), V(0, float.PositiveInfinity) } };
    }

    [Theory]
    [MemberData(nameof(InvalidOutlines))]
    public void InvalidContoursFailClosed(ArenaAreaVertex[] outline)
        => Assert.Throws<ArgumentException>(() => new ArenaAreaBounds(outline, 0, 8));

    [Theory]
    [InlineData(float.NaN, 8)]
    [InlineData(0, float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity, 8)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    public void InvalidPrismHeightsAreRejected(float minY, float maxY)
        => Assert.Throws<ArgumentException>(() => new ArenaAreaBounds(Elbow(), minY, maxY));

    [Fact]
    public void DefaultNullNonfiniteAndInvalidPathInputsCannotAdmitAnything()
    {
        var empty = default(ArenaAreaBounds);
        Assert.False(empty.IsValid);
        Assert.Empty(empty.Outline);
        Assert.Empty(empty.FloorTriangles);
        Assert.False(empty.Contains(0, 0, 0));
        Assert.False(empty.ContainsPath(P(0, 0, 0), new[] { P(0, 0, 0) }, true));
        Assert.Throws<ArgumentException>(() => new ArenaAreaBounds(null!, 0, 8));
        Assert.Throws<ArgumentException>(() => new ArenaAreaDefinition("p", "p", "map", empty, 0, 0, 2));
        var bounds = Polygon();
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Assert.False(bounds.Contains(invalid, 1, 1));
            Assert.False(bounds.Contains(1, invalid, 1));
            Assert.False(bounds.Contains(1, 1, invalid));
            Assert.False(bounds.Contains(1, 1, 1, invalid));
            Assert.False(bounds.ContainsPath(P(1, 1), new[] { P(1, 1) }, true, invalid));
        }
        Assert.False(bounds.Contains(1, 1, 1, -0.001f));
        Assert.False(bounds.Contains(1, 1, 1, 10));
        Assert.False(bounds.ContainsPath(P(1, 1), Array.Empty<ArenaAreaPoint>(), true));
        Assert.False(bounds.ContainsPath(P(1, 1), null!, true));
        Assert.False(bounds.ContainsPath(P(1, 1), new[] { P(1, 1) }, true, null!));
        Assert.False(bounds.ContainsPath(P(4, 4), new[] { P(1, 1) }, true));
        Assert.False(bounds.ContainsPath(P(1, 1), new[] { P(1, 1, 9) }, true));
        Assert.True(bounds.ContainsPath(P(1, 5), new[] { (1f, 1f), (5f, 1f) }, true,
            p => P(p.Item1, p.Item2), 0.5f));
    }

    [Fact]
    public void TranslatedAndSmallPolygonsRemainFiniteAndRetainTheirActualArea()
    {
        var translated = new ArenaAreaBounds(Elbow().Select(v => V(v.X + 1048576, v.Z - 1048576)).ToArray(), 0, 8);
        AssertCaps(translated, 20);
        Assert.True(translated.Contains(1048577, 1, -1048571));
        Assert.False(translated.Contains(1048580, 1, -1048572));
        var tiny = new ArenaAreaBounds(new[] { V(0, 0), V(0.00001f, 0), V(0, 0.00001f) }, 0, 8);
        Assert.True(tiny.Contains(0.000001f, 1, 0.000001f));
        Assert.False(tiny.Contains(0.000009f, 1, 0.000009f));
        AssertCaps(tiny, SignedArea(tiny.Outline));
    }

    [Fact]
    public void ExistingPresetsKeepTheirExactCoordinatesAndBoxBehavior()
    {
        Assert.Equal("factory-south-v1", ArenaAreaCatalog.FactorySouth.Id);
        Assert.Equal("customs-factory-center-v1", ArenaAreaCatalog.CustomsFactoryCenter.Id);
        Assert.Equal((-25f, -1f, -46f, 40f, 10f, 26f), Extents(ArenaAreaCatalog.FactorySouth.Bounds));
        Assert.Equal((305f, 0f, -135f, 420f, 12f, -65f), Extents(ArenaAreaCatalog.CustomsFactoryCenter.Bounds));
        foreach (var bounds in new[] { ArenaAreaCatalog.FactorySouth.Bounds, ArenaAreaCatalog.CustomsFactoryCenter.Bounds })
        {
            Assert.Equal(4, bounds.Outline.Count);
            AssertCaps(bounds, (double)(bounds.MaxX - bounds.MinX) * (bounds.MaxZ - bounds.MinZ));
            foreach (float inset in new[] { 0, 0.4f, 0.75f, 1, 1000 })
                foreach (float x in new[] { bounds.MinX - 1, bounds.MinX, bounds.MinX + inset, (bounds.MinX + bounds.MaxX) / 2, bounds.MaxX - inset, bounds.MaxX, bounds.MaxX + 1 })
                    foreach (float z in new[] { bounds.MinZ - 1, bounds.MinZ, bounds.MinZ + inset, (bounds.MinZ + bounds.MaxZ) / 2, bounds.MaxZ - inset, bounds.MaxZ, bounds.MaxZ + 1 })
                    {
                        bool expected = x >= bounds.MinX + inset && x <= bounds.MaxX - inset
                            && z >= bounds.MinZ + inset && z <= bounds.MaxZ - inset;
                        Assert.Equal(expected, bounds.Contains(x, bounds.MinY, z, inset));
                        Assert.Equal(expected, bounds.Contains(x, bounds.MaxY, z, inset));
                    }
            Assert.True(bounds.ContainsPath(P(bounds.MinX, bounds.MinZ, bounds.MinY),
                new[] { P(bounds.MaxX, bounds.MaxZ, bounds.MaxY) }, true));
        }
    }

    private static (float, float, float, float, float, float) Extents(ArenaAreaBounds b)
        => (b.MinX, b.MinY, b.MinZ, b.MaxX, b.MaxY, b.MaxZ);

    private static double SignedArea(IReadOnlyList<ArenaAreaVertex> vertices)
    {
        double area = 0;
        for (int i = 0; i < vertices.Count; i++)
        {
            var a = vertices[i]; var b = vertices[(i + 1) % vertices.Count];
            area += (double)a.X * b.Z - (double)a.Z * b.X;
        }
        return area / 2;
    }

    private static void AssertCaps(ArenaAreaBounds bounds, double expectedArea)
    {
        Assert.NotEmpty(bounds.FloorTriangles);
        Assert.Equal(0, bounds.FloorTriangles.Count % 3);
        double coveredArea = 0;
        for (int i = 0; i < bounds.FloorTriangles.Count; i += 3)
        {
            for (int j = 0; j < 3; j++) Assert.InRange(bounds.FloorTriangles[i + j], 0, bounds.Outline.Count - 1);
            var a = bounds.Outline[bounds.FloorTriangles[i]];
            var b = bounds.Outline[bounds.FloorTriangles[i + 1]];
            var c = bounds.Outline[bounds.FloorTriangles[i + 2]];
            double twiceArea = Side(a, b, c.X, c.Z);
            Assert.True(twiceArea > 0, "Cap triangles must be nondegenerate and CCW in X/Z (3D normal -Y).");
            coveredArea += twiceArea / 2;
        }
        Assert.True(Math.Abs(coveredArea - expectedArea) <= Math.Max(1e-20, Math.Abs(expectedArea) * 1e-10),
            $"Cap area {coveredArea:R} must equal polygon area {expectedArea:R}.");
    }

    private static int TriangleCoverage(ArenaAreaBounds bounds, double x, double z)
    {
        int count = 0;
        for (int i = 0; i < bounds.FloorTriangles.Count; i += 3)
        {
            var a = bounds.Outline[bounds.FloorTriangles[i]];
            var b = bounds.Outline[bounds.FloorTriangles[i + 1]];
            var c = bounds.Outline[bounds.FloorTriangles[i + 2]];
            if (Side(a, b, x, z) > 0 && Side(b, c, x, z) > 0 && Side(c, a, x, z) > 0) count++;
        }
        return count;
    }

    private static double Side(ArenaAreaVertex a, ArenaAreaVertex b, double x, double z)
        => ((double)b.X - a.X) * (z - a.Z) - ((double)b.Z - a.Z) * (x - a.X);
}
