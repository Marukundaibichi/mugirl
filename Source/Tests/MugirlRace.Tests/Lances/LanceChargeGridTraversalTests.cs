using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Mugirl.Features.Lances;
using Verse;

namespace Mugirl.Tests.Lances;

/// <summary><see cref="LanceChargeGridTraversal"/>：冲锋段破墙使用的网格 DDA。</summary>
public sealed class LanceChargeGridTraversalTests
{
    [Fact]
    public void ZeroLengthSegment_YieldsTheStartCellOnly()
    {
        List<IntVec3> cells = Collect(new Vector3(3.4f, 0f, 7.9f), new Vector3(3.4f, 0f, 7.9f));
        Assert.Equal(new[] { new IntVec3(3, 0, 7) }, cells);
    }

    [Fact]
    public void StraightSegment_VisitsEveryCellInOrder()
    {
        List<IntVec3> cells = Collect(new Vector3(0.5f, 0f, 0.5f), new Vector3(3.5f, 0f, 0.5f));
        Assert.Equal(new[] { new IntVec3(0, 0, 0), new IntVec3(1, 0, 0), new IntVec3(2, 0, 0), new IntVec3(3, 0, 0) }, cells);

        List<IntVec3> backwards = Collect(new Vector3(2.5f, 0f, 4.5f), new Vector3(2.5f, 0f, 1.5f));
        Assert.Equal(new[] { new IntVec3(2, 0, 4), new IntVec3(2, 0, 3), new IntVec3(2, 0, 2), new IntVec3(2, 0, 1) }, backwards);
    }

    [Fact]
    public void ExactCornerCrossing_StepsBothAxesTogether()
    {
        List<IntVec3> cells = Collect(new Vector3(0.5f, 0f, 0.5f), new Vector3(2.5f, 0f, 2.5f));
        Assert.Equal(new[] { new IntVec3(0, 0, 0), new IntVec3(1, 0, 1), new IntVec3(2, 0, 2) }, cells);
    }

    [Fact]
    public void ShallowDiagonal_IncludesNarrowlyGrazedCells()
    {
        // 从 (0.9, 0.1) 到 (2.1, 1.3)：先越过 x = 1，再越过 z = 1，最后越过 x = 2；斜线只擦过 (1, 0) 与 (1, 1) 的一角。
        List<IntVec3> cells = Collect(new Vector3(0.9f, 0f, 0.1f), new Vector3(2.1f, 0f, 1.3f));
        Assert.Equal(new[] { new IntVec3(0, 0, 0), new IntVec3(1, 0, 0), new IntVec3(1, 0, 1), new IntVec3(2, 0, 1) }, cells);
    }

    [Theory]
    [InlineData(float.NaN, 0f, 5f, 5f)]
    [InlineData(0f, float.PositiveInfinity, 5f, 5f)]
    [InlineData(0f, 0f, float.NegativeInfinity, 5f)]
    [InlineData(0f, 0f, 5f, float.NaN)]
    public void NonFiniteEndpoints_AreRejectedWithoutCells(float fromX, float fromZ, float toX, float toZ)
    {
        List<IntVec3> cells = new() { new IntVec3(9, 0, 9) };
        Assert.False(LanceChargeGridTraversal.TryCollectCrossedCells(new Vector3(fromX, 0f, fromZ), new Vector3(toX, 0f, toZ), cells));
        Assert.Empty(cells);
    }

    [Fact]
    public void ToCell_TruncatesLikeVerseIntVec3()
    {
        foreach (Vector3 position in new[] { new Vector3(0.99f, 5f, 12.5f), new Vector3(249.999f, 0f, 0f), new Vector3(-0.5f, 0f, -1.5f) })
        {
            Assert.Equal(new IntVec3(position.ToUnity()), LanceChargeGridTraversal.ToCell(position));
        }
    }

    [Fact]
    public void RandomMapSegments_MatchThe16AlgorithmAndStayConnected()
    {
        // 随机地图坐标上与 1.6 的 DDA 逐格一致（步数上限不截断正常线段），且相邻两格只相差一步。
        Random random = new(20261014);
        List<IntVec3> actual = [];
        for (int sample = 0; sample < 20000; sample++)
        {
            Vector3 from = new(NextCoordinate(random), 0f, NextCoordinate(random));
            Vector3 to = random.Next(4) == 0
                ? new Vector3(MathF.Floor(from.X) + random.Next(-3, 4) + 0.5f, 0f, MathF.Floor(from.Z) + random.Next(-3, 4) + 0.5f)
                : new Vector3(NextCoordinate(random), 0f, NextCoordinate(random));
            Assert.True(LanceChargeGridTraversal.TryCollectCrossedCells(from, to, actual));
            List<IntVec3> expected = Legacy16CrossedCells(from, to);
            if (!expected.SequenceEqual(actual))
            {
                Assert.Fail(string.Create(CultureInfo.InvariantCulture, $"segment {from} -> {to}: expected {expected.Count} cells, actual {actual.Count}"));
            }

            Assert.Equal(LanceChargeGridTraversal.ToCell(to), actual[^1]);
            for (int i = 1; i < actual.Count; i++)
            {
                IntVec3 step = actual[i] - actual[i - 1];
                Assert.True(Math.Abs(step.x) <= 1 && Math.Abs(step.z) <= 1 && step != IntVec3.Zero, "consecutive cells must be neighbours");
            }
        }
    }

    private static float NextCoordinate(Random random) => (float)(random.NextDouble() * 250.0);

    private static List<IntVec3> Collect(Vector3 from, Vector3 to)
    {
        List<IntVec3> cells = [];
        Assert.True(LanceChargeGridTraversal.TryCollectCrossedCells(from, to, cells));
        return cells;
    }

    // 1.6 PawnFlyer_LanceCharge 中 LanceChargeWallUtility.CrossedCellsNonAlloc 的原样副本（Mathf.Abs 即 Math.Abs(float)），作为差分参照。
    private static List<IntVec3> Legacy16CrossedCells(Vector3 from, Vector3 to)
    {
        List<IntVec3> outCells = [];
        IntVec3 cell = new((int)from.X, 0, (int)from.Z);
        IntVec3 end = new((int)to.X, 0, (int)to.Z);
        outCells.Add(cell);
        float dx = to.X - from.X;
        float dz = to.Z - from.Z;
        int stepX = Math.Sign(dx);
        int stepZ = Math.Sign(dz);
        float strideX = stepX == 0 ? float.PositiveInfinity : 1f / Math.Abs(dx);
        float strideZ = stepZ == 0 ? float.PositiveInfinity : 1f / Math.Abs(dz);
        float nextX = stepX == 0 ? float.PositiveInfinity : ((stepX > 0 ? cell.x + 1 : cell.x) - from.X) / dx;
        float nextZ = stepZ == 0 ? float.PositiveInfinity : ((stepZ > 0 ? cell.z + 1 : cell.z) - from.Z) / dz;
        while (cell != end)
        {
            if (cell.x == end.x)
            {
                nextX = float.PositiveInfinity;
            }

            if (cell.z == end.z)
            {
                nextZ = float.PositiveInfinity;
            }

            if (nextX < nextZ)
            {
                cell.x += stepX;
                nextX += strideX;
            }
            else if (nextZ < nextX)
            {
                cell.z += stepZ;
                nextZ += strideZ;
            }
            else
            {
                cell.x += stepX;
                cell.z += stepZ;
                nextX += strideX;
                nextZ += strideZ;
            }

            outCells.Add(cell);
        }

        return outCells;
    }
}
