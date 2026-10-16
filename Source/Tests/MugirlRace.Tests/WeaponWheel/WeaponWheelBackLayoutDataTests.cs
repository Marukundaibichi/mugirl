using Mugirl.Features.WeaponWheel;

namespace Mugirl.Tests.WeaponWheel;

/// <summary><see cref="WeaponWheelBackLayoutData"/>：覆盖文件的读写与容错（与 1.6 WeaponWheelBackLayout 的解析规则一致）。</summary>
public sealed class WeaponWheelBackLayoutDataTests
{
    [Fact]
    public void Defaults_HaveEightEntriesAndAreCopied()
    {
        WeaponWheelBackLayoutEntry[] first = WeaponWheelBackLayoutData.CopyDefaults();
        Assert.Equal(WeaponWheelBackLayoutData.EntryCount, first.Length);
        first[0].Angle = 123f;
        Bits.Equal(60f, WeaponWheelBackLayoutData.CopyDefaults()[0].Angle, "callers get a copy");
        Bits.Equal(-60f, WeaponWheelBackLayoutData.DefaultAt(3).Angle);
    }

    [Fact]
    public void Xml_RoundTripsExactly()
    {
        WeaponWheelBackLayoutEntry[] entries = WeaponWheelBackLayoutData.CopyDefaults();
        entries[WeaponWheelBackLayoutData.IndexOf(1, 2)] = new WeaponWheelBackLayoutEntry
        {
            OffsetX = 0.125f,
            OffsetZ = -1.25f,
            Angle = -33.5f,
            Layer = 47.5f,
            FlipHorizontal = true,
            FlipVertical = false
        };
        string xml = WeaponWheelBackLayoutData.ToXml(entries, -12.5f);

        Assert.True(WeaponWheelBackLayoutData.TryParseXml(xml, out WeaponWheelBackLayoutEntry[] parsed, out float southLayer));
        Bits.Equal(-12.5f, southLayer);
        for (int i = 0; i < entries.Length; i++)
        {
            Bits.Equal(entries[i].OffsetX, parsed[i].OffsetX, "OffsetX " + i);
            Bits.Equal(entries[i].OffsetZ, parsed[i].OffsetZ, "OffsetZ " + i);
            Bits.Equal(entries[i].Angle, parsed[i].Angle, "Angle " + i);
            Bits.Equal(entries[i].Layer, parsed[i].Layer, "Layer " + i);
            Assert.Equal(entries[i].FlipHorizontal, parsed[i].FlipHorizontal);
            Assert.Equal(entries[i].FlipVertical, parsed[i].FlipVertical);
        }
    }

    [Fact]
    public void Parse_ClampsAndKeepsUnlistedDefaults()
    {
        const string Xml = """
            <WeaponWheelBackLayout>
              <southLayerWithAutoloading>900</southLayerWithAutoloading>
              <entries>
                <li><weaponIndex>0</weaponIndex><facing>East</facing><offsetX>5</offsetX><offsetZ>NaN</offsetZ><angle>-400</angle><layer>-99</layer></li>
                <li><weaponIndex>1</weaponIndex><facing>3</facing><angle>12</angle><flipVertical>true</flipVertical></li>
              </entries>
            </WeaponWheelBackLayout>
            """;
        Assert.True(WeaponWheelBackLayoutData.TryParseXml(Xml, out WeaponWheelBackLayoutEntry[] parsed, out float southLayer));
        Bits.Equal(WeaponWheelBackLayoutData.LayerMax, southLayer);

        WeaponWheelBackLayoutEntry east = parsed[WeaponWheelBackLayoutData.IndexOf(0, 1)];
        Bits.Equal(WeaponWheelBackLayoutData.OffsetRange, east.OffsetX);
        Bits.Equal(0f, east.OffsetZ, "non-finite numbers fall back to 0");
        Bits.Equal(-WeaponWheelBackLayoutData.AngleRange, east.Angle);
        Bits.Equal(WeaponWheelBackLayoutData.LayerMin, east.Layer);
        Assert.False(east.FlipHorizontal);

        WeaponWheelBackLayoutEntry west = parsed[WeaponWheelBackLayoutData.IndexOf(1, 3)];
        Bits.Equal(12f, west.Angle);
        Assert.True(west.FlipVertical);

        Bits.Equal(WeaponWheelBackLayoutData.DefaultAt(0).Angle, parsed[0].Angle, "unlisted entries keep defaults");
    }

    [Theory]
    [InlineData("<WeaponWheelBackLayout><entries><li><weaponIndex>2</weaponIndex><facing>North</facing></li></entries></WeaponWheelBackLayout>")]
    [InlineData("<WeaponWheelBackLayout><entries><li><weaponIndex>0</weaponIndex><facing>Up</facing></li></entries></WeaponWheelBackLayout>")]
    [InlineData("<OtherRoot />")]
    [InlineData("not xml")]
    public void Parse_RejectsInvalidFilesWithDefaults(string text)
    {
        Assert.False(WeaponWheelBackLayoutData.TryParseXml(text, out WeaponWheelBackLayoutEntry[] parsed, out float southLayer));
        Bits.Equal(WeaponWheelBackLayoutData.DefaultSouthLayerWithAutoloading, southLayer);
        Bits.Equal(WeaponWheelBackLayoutData.DefaultAt(5).OffsetX, parsed[5].OffsetX);
    }

    [Theory]
    [InlineData("1.5", true, 1.5f)]
    [InlineData("-0.25", true, -0.25f)]
    [InlineData("2", false, 0f)]
    [InlineData("1,5", false, 0f)]
    [InlineData("-", false, 0f)]
    [InlineData("Infinity", false, 0f)]
    [InlineData(null, false, 0f)]
    public void TryReadNumber_AcceptsFiniteInvariantNumbersInRange(string? text, bool valid, float expected)
    {
        Assert.Equal(valid, WeaponWheelBackLayoutData.TryReadNumber(text, -1.5f, 1.5f, out float value));
        if (valid)
        {
            Bits.Equal(expected, value);
        }
    }

    [Fact]
    public void CSharpExport_ListsEveryEntry()
    {
        string code = WeaponWheelBackLayoutData.ToCSharp(WeaponWheelBackLayoutData.CopyDefaults(), -8f);
        Assert.Equal(WeaponWheelBackLayoutData.EntryCount, code.Split("new WeaponWheelBackLayoutEntry").Length - 1);
        Assert.Contains("DefaultSouthLayerWithAutoloading = -8f;", code, StringComparison.Ordinal);
        Assert.Contains("Angle = 60f", code, StringComparison.Ordinal);
    }
}
