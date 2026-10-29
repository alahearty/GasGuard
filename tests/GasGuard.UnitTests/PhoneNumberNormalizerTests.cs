using GasGuard.Application.Services;

namespace GasGuard.UnitTests;

public sealed class PhoneNumberNormalizerTests
{
    private readonly NigerianPhoneNumberNormalizer _normalizer = new();

    [Theory]
    [InlineData("08031234567")]
    [InlineData("8031234567")]
    [InlineData("+2348031234567")]
    [InlineData("2348031234567")]
    [InlineData("002348031234567")]
    [InlineData("+234 (803) 123-4567")]
    [InlineData("0803 123 4567")]
    public void NormalizesNigerianMobileNumbers(string input)
    {
        var success = _normalizer.TryNormalize(input, out var normalized, out var error);

        Assert.True(success, error);
        Assert.Equal("+2348031234567", normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("0803123456")]
    [InlineData("080312345678")]
    [InlineData("+2341031234567")]
    [InlineData("+2338031234567")]
    [InlineData("not-a-number")]
    public void RejectsInvalidNumbers(string input)
    {
        var success = _normalizer.TryNormalize(input, out var normalized, out var error);

        Assert.False(success);
        Assert.Equal(string.Empty, normalized);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void FormatsForDisplay()
    {
        Assert.Equal("+234 803 123 4567", _normalizer.FormatForDisplay("08031234567"));
    }

    [Fact]
    public void MatchesEquivalentFormats()
    {
        Assert.True(_normalizer.NumbersMatch("+2348031234567", "08031234567"));
        Assert.False(_normalizer.NumbersMatch("+2348031234567", "+2348031234568"));
    }

    [Fact]
    public void MatchesNonNormalizableSenderExactlyAsFallback()
    {
        Assert.True(_normalizer.NumbersMatch("GAS-DEMO", "gas-demo"));
    }
}
