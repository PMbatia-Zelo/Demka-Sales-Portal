using Demka.Portal.Core.Security;

namespace Demka.Portal.Tests;

public class SecurityTests
{
    [Fact]
    public void Pin_hash_verifies_only_the_right_pin()
    {
        var hash = PinHasher.Hash("2580", iterations: 1_000);
        Assert.True(PinHasher.Verify("2580", hash));
        Assert.False(PinHasher.Verify("2581", hash));
        Assert.DoesNotContain("2580", hash);
    }

    [Fact]
    public void Same_pin_gives_different_hashes()
    {
        Assert.NotEqual(PinHasher.Hash("2580", 1_000), PinHasher.Hash("2580", 1_000));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v2.1.abc.def")]
    [InlineData("not-a-hash")]
    public void Bad_stored_hash_never_verifies(string? stored)
    {
        Assert.False(PinHasher.Verify("2580", stored));
    }

    [Theory]
    [InlineData("2580", true)]
    [InlineData("907316", true)]
    [InlineData("123", false)]       // too short
    [InlineData("1234567", false)]   // too long
    [InlineData("12a4", false)]      // not digits
    [InlineData("1111", false)]      // same digit
    [InlineData("1234", false)]      // run up
    [InlineData("6543", false)]      // run down
    public void Pin_policy(string pin, bool allowed)
    {
        Assert.Equal(allowed, PinPolicy.Check(pin) is null);
    }

    [Fact]
    public void Temporary_pins_are_six_digits_and_pass_policy()
    {
        for (var i = 0; i < 200; i++)
        {
            var pin = PinHasher.GenerateTemporaryPin();
            Assert.Equal(6, pin.Length);
            Assert.Null(PinPolicy.Check(pin));
        }
    }

    [Theory]
    [InlineData("0712 345 678", "+254712345678")]
    [InlineData("712345678", "+254712345678")]
    [InlineData("254712345678", "+254712345678")]
    [InlineData("+254 712-345-678", "+254712345678")]
    [InlineData("0110 123 456", "+254110123456")]
    [InlineData("0812345678", null)]   // not a mobile range
    [InlineData("07123", null)]
    [InlineData("", null)]
    public void Phone_numbers_normalise_to_plus_254(string input, string? expected)
    {
        Assert.Equal(expected, PhoneNumber.Normalize(input));
    }

    [Fact]
    public void Phone_mask_shows_last_three_digits()
    {
        Assert.Equal("07•• ••• 678", PhoneNumber.Mask("+254712345678"));
    }
}
