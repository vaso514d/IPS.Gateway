using System.Globalization;
using System.Text;

namespace IPS.MiidleWear.CoreAPI.Sending;

// Random identifiers that pass the gateway's checksums: a Georgian IBAN (mod 97) and a nine-digit organisation tax code.
// The algorithms are the original gateway's generator's (IPS.MiidleWear.JsonGenerator.API).
internal static class TestIdentifiers
{
    private static readonly int[] TaxCodeWeights = [2, 1, 2, 1, 2, 1, 2, 1];

    // GE + two check digits + the two-letter bank code (the BIC's first two letters) + sixteen digits.
    internal static string Iban(string bankCode)
    {
        var code = bankCode.Length == 2 && bankCode.All(char.IsLetter) ? bankCode.ToUpperInvariant() : "NB";
        var bban = code + "000000" + Digits(10);
        return $"GE{CheckDigits(bban)}{bban}";
    }

    internal static string OrganisationTaxCode()
    {
        var digits = new int[9];
        digits[0] = Random.Shared.Next(1, 5);
        for (var i = 1; i < 8; i++)
        {
            digits[i] = Random.Shared.Next(0, 10);
        }

        var sum = 0;
        for (var i = 0; i < 8; i++)
        {
            var product = digits[i] * TaxCodeWeights[i];
            sum += product / 10 + product % 10;
        }

        var remainder = sum % 10;
        digits[8] = remainder == 0 ? 0 : 10 - remainder;
        return string.Concat(digits);
    }

    internal static string PersonalNumber() => Digits(11);

    internal static string Digits(int count) =>
        string.Concat(Enumerable.Range(0, count).Select(_ => Random.Shared.Next(0, 10).ToString(CultureInfo.InvariantCulture)));

    private static string CheckDigits(string bban)
    {
        var numeric = new StringBuilder();
        foreach (var character in bban + "GE00")
        {
            numeric.Append(char.IsDigit(character)
                ? character.ToString()
                : (char.ToUpperInvariant(character) - 'A' + 10).ToString(CultureInfo.InvariantCulture));
        }

        var remainder = 0;
        foreach (var digit in numeric.ToString())
        {
            remainder = (remainder * 10 + (digit - '0')) % 97;
        }

        return (98 - remainder).ToString("00", CultureInfo.InvariantCulture);
    }
}
