using MyApplication.Api.Clients;
using MyApplication.Api.Dtos;

namespace MyApplication.Api.Tests;

/// <summary>
/// Reading our free-text incident addresses (SH-I06 shape), and street matching for Public Transport's
/// road closures, which come without coordinates (ES-FR-010).
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "External services")]
public class SharedAddressTests
{
    // TC-ADDR-01
    [Theory]
    [Trait("Requirement", "SH-I06")]
    [InlineData("100 King St W", "100", "King St W", null, null)]
    [InlineData("9 Elm Rd, Cambridge", "9", "Elm Rd", "Cambridge", null)]
    [InlineData("700 Coronation Blvd, Cambridge, ON N1R 1A1", "700", "Coronation Blvd", "Cambridge", "N1R 1A1")]
    [InlineData("108 University Ave E, Waterloo, ON n2j2w2", "108", "University Ave E", "Waterloo", "N2J 2W2")]
    [InlineData("12B Mill Lane, Cambridge ON", "12B", "Mill Lane", "Cambridge", null)]
    public void Parse_SplitsAFreeTextAddress(string text, string? number, string street, string? city, string? postal)
    {
        var a = SharedAddress.Parse(text);

        Assert.Equal(number, a.StreetNumber);
        Assert.Equal(street, a.StreetName);
        Assert.Equal(city, a.City);
        Assert.Equal(postal, a.PostalCode);
    }

    // TC-ADDR-02
    [Theory]
    [Trait("Requirement", "SH-I06")]
    [InlineData(null)]
    [InlineData("   ")]
    public void Parse_NoAddress_StillGivesAStreetName(string? text)
    {
        Assert.Equal("Unknown location", SharedAddress.Parse(text).StreetName);
    }

    // TC-ADDR-03
    [Fact]
    [Trait("Requirement", "SH-I06")]
    public void Parse_KeepsUnrecognisedTextInStreetName_SoNothingIsLost()
    {
        var a = SharedAddress.Parse("Main St between Water and Ainslie");

        Assert.Null(a.StreetNumber);
        Assert.Equal("Main St between Water and Ainslie", a.StreetName);
    }

    // TC-ADDR-04
    [Theory]
    [Trait("Requirement", "ES-FR-010")]
    [InlineData("King Street West", "king st. w")]
    [InlineData("University Avenue East", "University Ave E")]
    [InlineData("Hespeler Road", "HESPELER RD")]
    public void NormalizeStreet_TreatsSpellingsOfTheSameStreetAsEqual(string a, string b)
    {
        Assert.Equal(SharedAddress.NormalizeStreet(a), SharedAddress.NormalizeStreet(b));
    }

    // TC-ADDR-05
    [Fact]
    [Trait("Requirement", "ES-FR-010")]
    public void NormalizeStreet_KeepsDifferentStreetsDifferent()
    {
        Assert.NotEqual(SharedAddress.NormalizeStreet("King St W"), SharedAddress.NormalizeStreet("King St E"));
        Assert.NotEqual(SharedAddress.NormalizeStreet("Main St"), SharedAddress.NormalizeStreet("Main Rd"));
    }

    // TC-ADDR-06
    [Theory]
    [InlineData("King St W", "King Street West between Ainslie and Water", true)]
    [InlineData("King St W", "King St", true)]                 // the plain street, no direction
    [InlineData("King St W", "Kingsway Dr", false)]            // whole words only
    [InlineData("King St W", "Main St", false)]
    [InlineData("Unknown location", "King St", false)]
    [Trait("Requirement", "ES-FR-010")]
    public void OnStreet_MatchesTheIncidentsStreet_InTransitsFreeText(string incidentStreet, string closureText, bool expected)
    {
        var street = SharedAddress.NormalizeStreet(incidentStreet);
        if (street == "unknown location") street = "";

        Assert.Equal(expected, RoadClosureClient.OnStreet(street, [closureText]));
    }
}
