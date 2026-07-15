using HorizonGuide.Core.Locations;
using Xunit;

namespace HorizonGuide.Tests;

public class LocationNameTests
{
    [Fact]
    public void DisplayNameUsesRequestedLanguage()
    {
        var location = new Location
        {
            Id = "SHIBUYA_CROSSING",
            Name = "涩谷十字路口",
            Names = new Dictionary<string, string>
            {
                ["zh"] = "涩谷十字路口",
                ["ja"] = "渋谷スクランブル交差点",
                ["en"] = "Shibuya Scramble Crossing",
            },
        };

        Assert.Equal("渋谷スクランブル交差点", location.DisplayName("ja"));
        Assert.Equal("Shibuya Scramble Crossing", location.DisplayName("en"));
    }

    [Fact]
    public void DisplayNameFallsBackToChineseThenName()
    {
        var withChinese = new Location
        {
            Id = "PEACE_TORII",
            Name = "平和鸟居",
            Names = new Dictionary<string, string> { ["zh"] = "平和鸟居" },
        };
        var withoutLocalizedNames = new Location
        {
            Id = "AKIHABARA",
            Name = "秋叶原",
        };

        Assert.Equal("平和鸟居", withChinese.DisplayName("ja"));
        Assert.Equal("秋叶原", withoutLocalizedNames.DisplayName("en"));
    }
}
