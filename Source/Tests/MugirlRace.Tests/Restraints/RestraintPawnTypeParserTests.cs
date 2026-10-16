using Mugirl.Features.Restraints;

namespace Mugirl.Tests.Restraints;

/// <summary>洗脑特质条目的身份解析：对 ASCII 输入与 1.6 的 Trim().ToLowerInvariant() 结果相同，且不分配字符串。</summary>
public sealed class RestraintPawnTypeParserTests
{
    [Theory]
    [InlineData("colonist", nameof(RestraintPawnType.Colonist))]
    [InlineData("Slave", nameof(RestraintPawnType.Slave))]
    [InlineData("  PRISONER ", nameof(RestraintPawnType.Prisoner))]
    [InlineData("free", nameof(RestraintPawnType.Free))]
    [InlineData("\thostile\n", nameof(RestraintPawnType.Hostile))]
    [InlineData("guest", nameof(RestraintPawnType.None))]
    [InlineData("", nameof(RestraintPawnType.None))]
    [InlineData("   ", nameof(RestraintPawnType.None))]
    [InlineData(null, nameof(RestraintPawnType.None))]
    public void Parse_RecognizesXmlValues(string? text, string expected)
        => Assert.Equal(expected, RestraintPawnTypeParser.Parse(text).ToString());

    [Theory]
    [InlineData("colonist")]
    [InlineData(" Colonist")]
    [InlineData("SLAVE ")]
    [InlineData("prisoner")]
    [InlineData("Free")]
    [InlineData("hostile")]
    [InlineData("colonists")]
    [InlineData("hostil")]
    public void Parse_AgreesWithLegacyLowerInvariantSwitch(string text)
    {
        RestraintPawnType legacy = text.Trim().ToLowerInvariant() switch
        {
            "colonist" => RestraintPawnType.Colonist,
            "slave" => RestraintPawnType.Slave,
            "prisoner" => RestraintPawnType.Prisoner,
            "free" => RestraintPawnType.Free,
            "hostile" => RestraintPawnType.Hostile,
            _ => RestraintPawnType.None,
        };
        Assert.Equal(legacy, RestraintPawnTypeParser.Parse(text));
    }
}
