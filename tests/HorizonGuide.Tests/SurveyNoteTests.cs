using HorizonGuide.Tools.TelemetryProbe;
using Xunit;

namespace HorizonGuide.Tests;

/// <summary>
/// 勘景时一行输入里的「名字(说明)」怎么拆。
///
/// 为什么要拆：Name 会**直接显示在玩家屏幕上**。说明是给取材用的线索
/// （原型是谁、哪里对不上），不该弹到玩家脸上。
/// </summary>
public class SurveyNoteTests
{
    [Fact]
    public void 括号里的说明拆进note不进name()
    {
        var (name, note) = SurveySession.SplitNote("雷鸟酒店(实际不存在，原型是立山黑部的室堂)");

        Assert.Equal("雷鸟酒店", name);
        Assert.Equal("实际不存在，原型是立山黑部的室堂", note);
    }

    [Fact]
    public void 全角括号也认()
    {
        // 中文输入法打出来的是全角括号，这是常态不是例外
        var (name, note) = SurveySession.SplitNote("色川航空中心（虚构，参考种子岛）");

        Assert.Equal("色川航空中心", name);
        Assert.Equal("虚构，参考种子岛", note);
    }

    [Fact]
    public void 没有括号就没有备注()
    {
        var (name, note) = SurveySession.SplitNote("涩谷十字路口");

        Assert.Equal("涩谷十字路口", name);
        Assert.Null(note);
    }

    [Fact]
    public void 说明里有嵌套括号时取最外层()
    {
        var (name, note) = SurveySession.SplitNote(
            "雷鸟酒店(参考了富山县(立山黑部)的室堂总站)");

        Assert.Equal("雷鸟酒店", name);
        Assert.Equal("参考了富山县(立山黑部)的室堂总站", note);
    }

    [Fact]
    public void 括号打在最前面时不拆()
    {
        // 名字整个在括号里，拆了会得到一个空名字——那还不如原样留着。
        var (name, note) = SurveySession.SplitNote("(某个还没想好名字的地方)");

        Assert.Equal("(某个还没想好名字的地方)", name);
        Assert.Null(note);
    }
}
