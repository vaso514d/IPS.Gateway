namespace IPS.Middleware.Application.Payments.Pacs008.Validation;

internal static class PaymentChecksums
{
    internal static bool ValidTaxCode(string? code)
    {
        if (code is not { Length: 9 } || !code.All(char.IsAsciiDigit)) return false;
        if (code[0] is < '1' or > '4') return true;
        var sum = 0;
        for (var i = 0; i < 8; i++)
        {
            var product = (code[i] - '0') * (i % 2 == 0 ? 2 : 1);
            sum += product / 10 + product % 10;
        }
        return code[8] - '0' == (10 - sum % 10) % 10;
    }

    internal static bool ValidIban(string value)
    {
        if (value.Length < 5 || !value.All(char.IsAsciiLetterOrDigit)) return false;
        var remainder = 0;
        foreach (var character in value[4..].Concat(value[..4]))
        {
            var digit = char.IsAsciiDigit(character) ? character - '0' : char.ToUpperInvariant(character) - 'A' + 10;
            remainder = (remainder * (digit < 10 ? 10 : 100) + digit) % 97;
        }
        return remainder == 1;
    }
}
