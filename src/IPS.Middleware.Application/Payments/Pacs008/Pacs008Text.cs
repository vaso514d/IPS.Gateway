namespace IPS.Middleware.Application.Payments.Pacs008;

// Field formats follow the ISO 20022 schemas: a MaxNText field is any XML character, 1 to N long. Patterns remain
// only where the schema has one (BIC, currency), plus the Annex D channel and instrument code (3.2.1.e).
internal static class Pacs008Text
{
    // Every character XML 1.0 can carry; control characters other than tab, line feed and carriage return cannot be.
    private const string XmlCharacter = @"[^\u0000-\u0008\u000B\u000C\u000E-\u001F￾￿]";

    // The client reference also travels in the Idempotency-Key HTTP header, which carries printable ASCII only.
    private const string HeaderCharacter = @"[ -~]";

    public static string Text(int maxLength) => Wrap(XmlCharacter, maxLength);
    public static readonly string TextAnyLength = "^" + XmlCharacter + "+$";
    public static string HeaderText(int maxLength) => Wrap(HeaderCharacter, maxLength);

    public const string Bic = @"^[A-Z0-9]{4}[A-Z]{2}[A-Z0-9]{2}([A-Z0-9]{3})?$";

    public const string Currency = @"^[A-Z]{3}$";

    public const string Code4Upper = @"^[A-Z]{4}$";

    private static string Wrap(string characterClass, int maxLength) => "^" + characterClass + "{1," + maxLength + "}$";
}
