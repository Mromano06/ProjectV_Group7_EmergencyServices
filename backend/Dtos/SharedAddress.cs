using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MyApplication.Api.Dtos;

/// <summary>
/// The shared address shape every team agreed to use (SH-I06):
/// { streetNumber, streetName, city, postalCode }. We use it to pull the street out of an incident's
/// free-text address: it goes to Transit as affected_segments, and is matched against their road closures.
/// Transit's POST /road-closures (their Swagger, Nov 26) takes it as its required "address" object,
/// so the JSON names are fixed to the agreed camelCase ones.
/// </summary>
public sealed record SharedAddress(
    [property: JsonPropertyName("streetNumber")] string? StreetNumber,
    [property: JsonPropertyName("streetName")] string StreetName,
    [property: JsonPropertyName("city")] string? City,
    [property: JsonPropertyName("postalCode")] string? PostalCode)
{
    // "100 King St W, Cambridge, ON N1R 1A1"  ->  100 | King St W | Cambridge | N1R 1A1
    private static readonly Regex NumberAndStreet = new(@"^\s*(?<num>\d+[A-Za-z]?)\s+(?<street>.+)$");
    private static readonly Regex PostalCodePattern = new(@"\b([A-Za-z]\d[A-Za-z])\s?(\d[A-Za-z]\d)\b");

    /// <summary>
    /// Splits one free-text address (how incidents store it today) into the shared shape.
    /// Anything it can't recognise stays in StreetName, so nothing is lost.
    /// </summary>
    public static SharedAddress Parse(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return new SharedAddress(null, "Unknown location", null, null);

        var parts = address.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        string? postal = null;
        var postalMatch = PostalCodePattern.Match(address);
        if (postalMatch.Success) postal = $"{postalMatch.Groups[1].Value} {postalMatch.Groups[2].Value}".ToUpperInvariant();

        var first = parts[0];
        string? number = null;
        var street = first;
        var m = NumberAndStreet.Match(first);
        if (m.Success)
        {
            number = m.Groups["num"].Value;
            street = m.Groups["street"].Value.Trim();
        }

        string? city = null;
        if (parts.Length > 1)
        {
            // The city is the next part, minus any province or postal code after it ("Cambridge ON N1R 1A1").
            city = PostalCodePattern.Replace(parts[1], "").Trim();
            city = Regex.Replace(city, @"\s+(ON|Ont\.?|Ontario)$", "", RegexOptions.IgnoreCase).Trim();
            if (city.Length == 0 || Regex.IsMatch(city, @"^(ON|Ontario)$", RegexOptions.IgnoreCase)) city = null;
        }

        return new SharedAddress(number, street, city, postal);
    }

    private static readonly (string Long, string Short)[] Abbreviations =
    [
        ("street", "st"), ("road", "rd"), ("avenue", "ave"), ("boulevard", "blvd"), ("drive", "dr"),
        ("lane", "ln"), ("court", "ct"), ("crescent", "cres"), ("place", "pl"), ("highway", "hwy"),
        ("west", "w"), ("east", "e"), ("north", "n"), ("south", "s"),
    ];

    /// <summary>
    /// A street name reduced to a comparable form, so "King Street West" matches "king st. w".
    /// Used to find road closures on the same street as an incident (ES-FR-010) when Transit
    /// sends addresses but no coordinates.
    /// </summary>
    public static string NormalizeStreet(string? street)
    {
        if (string.IsNullOrWhiteSpace(street)) return string.Empty;
        var words = Regex.Replace(street.ToLowerInvariant(), @"[^\w\s]", " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => Abbreviations.FirstOrDefault(a => a.Long == w).Short ?? w);
        return string.Join(' ', words);
    }
}