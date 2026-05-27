using Fabulis.Server.Data;
using Xunit;

namespace Fabulis.Server.Tests;

public class NarrationValidationTests
{
    [Fact]
    public void NormalizesSpeedFromRequestThenSettingThenOne()
    {
        Assert.Equal(1.5, NarrationValidation.NormalizeSpeed(requested: 1.5, settingValue: "1.0"));
        Assert.Equal(1.0, NarrationValidation.NormalizeSpeed(requested: null, settingValue: "1.0"));
        Assert.Equal(1.0, NarrationValidation.NormalizeSpeed(requested: null, settingValue: null));
        Assert.Equal(1.0, NarrationValidation.NormalizeSpeed(requested: null, settingValue: "not a number"));
    }

    [Theory]
    [InlineData(0.5, true)]
    [InlineData(2.0, true)]
    [InlineData(1.25, true)]
    [InlineData(0.49, false)]
    [InlineData(2.01, false)]
    [InlineData(double.NaN, false)]
    public void ValidatesSpeedRange(double speed, bool ok)
    {
        Assert.Equal(ok, NarrationValidation.IsSpeedValid(speed));
    }

    [Fact]
    public void NormalizesVoiceFromRequestOverSetting()
    {
        Assert.Equal("af_bella", NarrationValidation.NormalizeVoice("af_bella", "am_michael"));
        Assert.Equal("am_michael", NarrationValidation.NormalizeVoice(null, "am_michael"));
        Assert.Null(NarrationValidation.NormalizeVoice(null, null));
        Assert.Null(NarrationValidation.NormalizeVoice("   ", null));
    }

    [Fact]
    public void NormalizesKokoroBaseUrl()
    {
        Assert.Equal("http://localhost:8880",
            NarrationValidation.NormalizeBaseUrl("http://localhost:8880/"));
        Assert.Equal("https://kokoro.local",
            NarrationValidation.NormalizeBaseUrl("https://kokoro.local"));
    }

    [Theory]
    [InlineData("http://localhost:8880", true)]
    [InlineData("https://kokoro.local:8443", true)]
    [InlineData("ftp://nope", false)]
    [InlineData("not a url", false)]
    [InlineData("", false)]
    public void ValidatesKokoroBaseUrl(string url, bool ok)
    {
        Assert.Equal(ok, NarrationValidation.IsBaseUrlValid(url));
    }

    [Fact]
    public void ValidatesTextLength()
    {
        Assert.True(NarrationValidation.IsTextLengthOk("short"));
        Assert.True(NarrationValidation.IsTextLengthOk(new string('x', NarrationValidation.MaxTextLength)));
        Assert.False(NarrationValidation.IsTextLengthOk(new string('x', NarrationValidation.MaxTextLength + 1)));
    }
}
