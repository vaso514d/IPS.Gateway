namespace IPS.Middleware.Infrastructure.Proxy;

// Names of the Proxy Solution's acmt.022 exchange (Annex E 2.3).
internal static class ProxyXml
{
    internal const string HeaderNamespace = "urn:iso:std:iso:20022:tech:xsd:head.001.001.03";
    internal const string DocumentNamespace = "urn:iso:std:iso:20022:tech:xsd:acmt.022.001.04";
    // The wrapper of Annex E p. 32: <hdr:Message xmlns:hdr="urn:montran:message.01">.
    internal const string WrapperNamespace = "urn:montran:message.01";
    internal const string MessageDefinition = "acmt.022.001.04";
}
