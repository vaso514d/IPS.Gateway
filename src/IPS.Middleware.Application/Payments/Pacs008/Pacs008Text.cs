namespace IPS.Middleware.Application.Payments.Pacs008;

internal static class Pacs008Text
{
    private const string FreeTextClass =
        @"[0-9a-zA-Zა-ჰ/\-\?:\(\)\.,'\+ !#$%&\*=^_`\{\|\}~"";<>@\[\\\]]";

    private const string AsciiIdClass =
        @"[0-9a-zA-Z/\-\?:\(\)\.,'\+ !""#$%&\*=^_`\{\|\}~;<>@\[\\\]]";

    private const string SimpleTextClass = @"[0-9a-zA-Z/\-\?:\(\)\.,'\+ ]";

    private const string CompactClass = @"[0-9a-zA-Z/\-\?:\(\)\.,'\+]";

    private const string IdentificationClass = @"[0-9a-zA-Z/\-\?:\(\)\.,'\+| ]";
    public static string FreeText(int maxLength) => Wrap(FreeTextClass, maxLength);
    public static readonly string FreeTextAnyLength = "^" + FreeTextClass + "+$";
    public static string AsciiId(int maxLength) => Wrap(AsciiIdClass, maxLength);
    public static string SimpleText(int maxLength) => Wrap(SimpleTextClass, maxLength);
    public static string Compact(int maxLength) => Wrap(CompactClass, maxLength);
    public static string Identification(int maxLength) => Wrap(IdentificationClass, maxLength);

    public const string Bic = @"^[A-Z0-9]{4}[A-Z]{2}[A-Z0-9]{2}([A-Z0-9]{3})?$";

    public const string Currency = @"^[A-Z]{3}$";

    public const string Code4Upper = @"^[A-Z]{4}$";

    private static string Wrap(string characterClass, int maxLength) => "^" + characterClass + "{1," + maxLength + "}$";
}
