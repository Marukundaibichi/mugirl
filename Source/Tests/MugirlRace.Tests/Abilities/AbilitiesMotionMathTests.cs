using Mugirl.Features.Abilities;

namespace Mugirl.Tests.Abilities;

/// <summary>投掷与扣篮的轨迹公式：端点、弧顶、钳制与扣篮各阶段衔接。</summary>
public sealed class AbilitiesMotionMathTests
{
    private const float Overhead = 12.5f;

    [Theory]
    [InlineData(0f, 22)]
    [InlineData(10f, 36)]
    [InlineData(100f, 75)]
    public void FlightTicks_GrowWithDistanceWithinBounds(float distance, int expected)
        => Assert.Equal(expected, ThrowFlightMath.FlightTicks(distance));

    [Fact]
    public void ThrowFlight_StartsAtHeldPositionAndLandsOnDestination()
    {
        Vector3 start = new(10.5f, 3f, 20.5f);
        Vector3 destination = new(18.5f, 0f, 26.5f);
        const int flightTicks = 40;

        Vector3 begin = ThrowFlightMath.FlyingPosition(start, destination, 0f, flightTicks, 10f, Overhead);
        Bits.Equal(start.X, begin.X);
        Bits.Equal(start.Z, begin.Z);
        Bits.Equal(Overhead, begin.Y);

        Vector3 end = ThrowFlightMath.FlyingPosition(start, destination, flightTicks, flightTicks, 10f, Overhead);
        Bits.Equal(destination.X, end.X);
        Bits.Equal(destination.Z, end.Z);
        Bits.Equal(Overhead, end.Y);

        Vector3 apex = ThrowFlightMath.FlyingPosition(start, destination, flightTicks / 2f, flightTicks, 10f, Overhead);
        float arcHeight = 10f * 0.32f;
        Bits.Equal(Overhead + arcHeight, apex.Y, "arc height is distance * 0.32 within [2.2, 6.5]");

        Vector3 beyond = ThrowFlightMath.FlyingPosition(start, destination, flightTicks * 3f, flightTicks, 10f, Overhead);
        Bits.Equal(end.X, beyond.X, "progress is clamped");
        Bits.Equal(1f, ThrowFlightMath.Progress(5f, 0), "an invalid duration counts as arrived");
    }

    [Fact]
    public void HeldPosition_FloatsAboveCarrier()
    {
        Vector3 held = ThrowFlightMath.HeldPosition(new Vector3(4.5f, 7f, 9.5f), 0, Overhead);
        Bits.Equal(4.5f, held.X);
        Bits.Equal(9.5f + 1.05f, held.Z);
        Bits.Equal(Overhead, held.Y);
    }

    [Fact]
    public void DunkSpin_LiftsFromLaunchPosition()
    {
        Vector3 launch = new(3f, 4f, 5f);
        Vector3 atStart = DunkPresentationMath.SpinPosition(launch, 0f, 20f);
        Bits.Equal(launch.X, atStart.X);
        Bits.Equal(launch.Y, atStart.Y);
        Bits.Equal(launch.Z, atStart.Z);

        Vector3 lifted = DunkPresentationMath.SpinPosition(launch, 10f, 20f);
        Bits.Equal(20f, lifted.Y, "fully lifted after five ticks");
        Bits.Equal(launch.Z + (1.05f + UnityMathf.Sin(10f * DunkPresentationMath.OrbitDegreesPerTick * UnityMathf.Deg2Rad) * 0.30f), lifted.Z);
    }

    [Fact]
    public void DunkFlightAndSlam_ReachTheImpactCell()
    {
        Vector3 launch = new(3f, 4f, 5f);
        Vector3 end = new(9.5f, 6f, 1.5f);
        // sin(π) 的单精度结果约为 -8.7e-8，落点只在末位附近与目标相同。
        Vector3 landed = DunkPresentationMath.FlightPosition(launch, end, 7f, DunkPresentationMath.FlightTicksBeforeImpact);
        Assert.Equal(end.X, landed.X, 1e-5f);
        Assert.Equal(end.Y, landed.Y, 1e-5f);
        Assert.Equal(end.Z, landed.Z, 1e-5f);

        Vector3 midFlight = DunkPresentationMath.FlightPosition(launch, end, 7f, DunkPresentationMath.FlightTicksBeforeImpact / 2f);
        Assert.True(midFlight.Z > UnityMathf.Lerp(launch.Z, end.Z, 0.5f) + 3f, "the head arcs well above the straight line");

        Vector3 slamStart = DunkPresentationMath.SlamPosition(launch, end, 0f);
        Bits.Equal(launch.X, slamStart.X);
        Vector3 slamEnd = DunkPresentationMath.SlamPosition(launch, end, DunkPresentationMath.SlamTicks);
        Assert.Equal(end.Z, slamEnd.Z, 1e-5f);
    }

    [Fact]
    public void SlamCeiling_LeadsByOneTickButStopsShortOfImpact()
    {
        Bits.Equal(4f, DunkPresentationMath.PresentationCeiling(4, slamTracksCarrier: false));
        Bits.Equal(5f, DunkPresentationMath.PresentationCeiling(4, slamTracksCarrier: true));
        Bits.Equal(DunkPresentationMath.SlamTicks - 0.2f, DunkPresentationMath.PresentationCeiling(9, slamTracksCarrier: true));
    }
}
