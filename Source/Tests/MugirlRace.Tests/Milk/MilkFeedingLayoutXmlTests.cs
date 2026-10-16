using System.IO;
using System.Xml.Linq;
using Mugirl.Features.Milk;

namespace Mugirl.Tests.Milk;

/// <summary>喂奶姿态覆盖文件（Config/Mugirl/MilkFeedingLayout.xml）的格式：往返、钳制与拒绝无效内容。</summary>
public sealed class MilkFeedingLayoutXmlTests
{
    [Fact]
    public void WriteThenRead_RoundTripsFourDecimals()
    {
        MilkFeedingLayoutEntry value = new()
        {
            SourceX = 0.125f,
            SourceZ = -0.5f,
            RecipientX = 0.0625f,
            RecipientZ = 1f,
            SourceAngle = -12.5f,
            RecipientAngle = 45f,
        };

        MilkFeedingLayoutEntry read = MilkFeedingLayoutXml.Read(XDocument.Parse(MilkFeedingLayoutXml.Write(value)).Root);
        Assert.Equal(value, read);
    }

    [Fact]
    public void Write_UsesInvariantNumbersAndLegacyElementNames()
    {
        string xml = MilkFeedingLayoutXml.Write(new MilkFeedingLayoutEntry { SourceX = 0.12345f, SourceAngle = -3.5f });
        Assert.Contains("<sourceX>0.1235</sourceX>", xml, StringComparison.Ordinal);
        Assert.Contains("<sourceAngle>-3.5</sourceAngle>", xml, StringComparison.Ordinal);
        Assert.Contains("<recipientAngle>0</recipientAngle>", xml, StringComparison.Ordinal);
        Assert.StartsWith("<?xml", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_ClampsOutOfRangeValues()
    {
        MilkFeedingLayoutEntry read = MilkFeedingLayoutXml.Read(XElement.Parse(
            "<MilkFeedingLayout><sourceX>3</sourceX><sourceZ>-2</sourceZ><recipientX>0</recipientX><recipientZ>0</recipientZ>"
            + "<sourceAngle>90</sourceAngle><recipientAngle>-100</recipientAngle></MilkFeedingLayout>"));
        Bits.Equal(1f, read.SourceX);
        Bits.Equal(-1f, read.SourceZ);
        Bits.Equal(45f, read.SourceAngle);
        Bits.Equal(-45f, read.RecipientAngle);
    }

    [Theory]
    [InlineData("<Other />")]
    [InlineData("<MilkFeedingLayout><sourceX>0</sourceX></MilkFeedingLayout>")]
    [InlineData("<MilkFeedingLayout><sourceX>NaN</sourceX><sourceZ>0</sourceZ><recipientX>0</recipientX><recipientZ>0</recipientZ><sourceAngle>0</sourceAngle><recipientAngle>0</recipientAngle></MilkFeedingLayout>")]
    [InlineData("<MilkFeedingLayout><sourceX>0,5</sourceX><sourceZ>0</sourceZ><recipientX>0</recipientX><recipientZ>0</recipientZ><sourceAngle>0</sourceAngle><recipientAngle>0</recipientAngle></MilkFeedingLayout>")]
    public void Read_RejectsInvalidDocuments(string xml)
    {
        Assert.Throws<InvalidDataException>(() => MilkFeedingLayoutXml.Read(XElement.Parse(xml)));
    }

    [Fact]
    public void Read_RejectsMissingRoot()
    {
        Assert.Throws<InvalidDataException>(() => MilkFeedingLayoutXml.Read(null));
    }
}
