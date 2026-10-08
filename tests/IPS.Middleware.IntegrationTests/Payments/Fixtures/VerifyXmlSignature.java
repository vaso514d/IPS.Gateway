import java.nio.file.Files;
import java.nio.file.Path;
import java.security.cert.CertificateFactory;
import javax.xml.XMLConstants;
import javax.xml.crypto.dsig.XMLSignature;
import javax.xml.crypto.dsig.XMLSignatureFactory;
import javax.xml.crypto.dsig.dom.DOMValidateContext;
import javax.xml.parsers.DocumentBuilderFactory;

// Independent JSR105 verification. The trusted test key is supplied separately from KeyInfo.
class VerifyXmlSignature {
    public static void main(String[] args) throws Exception {
        var factory = DocumentBuilderFactory.newInstance();
        factory.setNamespaceAware(true);
        factory.setFeature("http://apache.org/xml/features/disallow-doctype-decl", true);
        factory.setAttribute(XMLConstants.ACCESS_EXTERNAL_DTD, "");
        factory.setAttribute(XMLConstants.ACCESS_EXTERNAL_SCHEMA, "");
        var document = factory.newDocumentBuilder().parse(Path.of(args[0]).toFile());
        var signatures = document.getElementsByTagNameNS(XMLSignature.XMLNS, "Signature");
        if (signatures.getLength() != 1) throw new IllegalArgumentException("Expected exactly one signature");
        try (var certificateStream = Files.newInputStream(Path.of(args[1]))) {
            var certificate = CertificateFactory.getInstance("X.509").generateCertificate(certificateStream);
            var context = new DOMValidateContext(certificate.getPublicKey(), signatures.item(0));
            context.setProperty("org.jcp.xml.dsig.secureValidation", Boolean.TRUE);
            var signature = XMLSignatureFactory.getInstance("DOM").unmarshalXMLSignature(context);
            System.out.println(signature.validate(context) ? "VALID" : "INVALID");
        }
    }
}
