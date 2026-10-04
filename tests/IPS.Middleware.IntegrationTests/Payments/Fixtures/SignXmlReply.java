import java.io.StringWriter;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.KeyFactory;
import java.security.cert.CertificateFactory;
import java.security.cert.X509Certificate;
import java.security.spec.PKCS8EncodedKeySpec;
import java.util.List;
import javax.xml.XMLConstants;
import javax.xml.crypto.dsig.CanonicalizationMethod;
import javax.xml.crypto.dsig.DigestMethod;
import javax.xml.crypto.dsig.SignatureMethod;
import javax.xml.crypto.dsig.Transform;
import javax.xml.crypto.dsig.XMLSignatureFactory;
import javax.xml.crypto.dsig.dom.DOMSignContext;
import javax.xml.crypto.dsig.spec.C14NMethodParameterSpec;
import javax.xml.crypto.dsig.spec.TransformParameterSpec;
import javax.xml.parsers.DocumentBuilderFactory;
import javax.xml.transform.TransformerFactory;
import javax.xml.transform.dom.DOMSource;
import javax.xml.transform.stream.StreamResult;

// Independent JSR105 signer for simulated IPS replies: the IPS enveloped profile placed in AppHdr/Sgntr.
// Arguments: PKCS#8 key, DER certificate, then input/output file pairs.
class SignXmlReply {
    private static final String HEAD = "urn:iso:std:iso:20022:tech:xsd:head.001.001.03";

    public static void main(String[] args) throws Exception {
        var key = KeyFactory.getInstance("EC").generatePrivate(new PKCS8EncodedKeySpec(Files.readAllBytes(Path.of(args[0]))));
        X509Certificate certificate;
        try (var stream = Files.newInputStream(Path.of(args[1]))) {
            certificate = (X509Certificate) CertificateFactory.getInstance("X.509").generateCertificate(stream);
        }
        var signatures = XMLSignatureFactory.getInstance("DOM");
        var keys = signatures.getKeyInfoFactory();
        var keyInfo = keys.newKeyInfo(List.of(keys.newX509Data(List.of(certificate.getSubjectX500Principal().getName(), certificate))));

        var factory = DocumentBuilderFactory.newInstance();
        factory.setNamespaceAware(true);
        factory.setFeature("http://apache.org/xml/features/disallow-doctype-decl", true);
        factory.setAttribute(XMLConstants.ACCESS_EXTERNAL_DTD, "");
        factory.setAttribute(XMLConstants.ACCESS_EXTERNAL_SCHEMA, "");
        for (var index = 2; index < args.length; index += 2) {
            var document = factory.newDocumentBuilder().parse(Path.of(args[index]).toFile());
            var envelope = document.getElementsByTagNameNS(HEAD, "Sgntr");
            if (envelope.getLength() != 1) throw new IllegalArgumentException("Expected one empty AppHdr/Sgntr");
            // A Reference keeps its computed digest, so every document needs a fresh SignedInfo.
            var reference = signatures.newReference("", signatures.newDigestMethod(DigestMethod.SHA256, null),
                List.of(signatures.newTransform(Transform.ENVELOPED, (TransformParameterSpec) null),
                    signatures.newTransform(CanonicalizationMethod.INCLUSIVE, (TransformParameterSpec) null)), null, null);
            var signedInfo = signatures.newSignedInfo(
                signatures.newCanonicalizationMethod(CanonicalizationMethod.INCLUSIVE_11, (C14NMethodParameterSpec) null),
                signatures.newSignatureMethod(SignatureMethod.ECDSA_SHA256, null), List.of(reference));
            var context = new DOMSignContext(key, envelope.item(0));
            context.setDefaultNamespacePrefix("ds");
            signatures.newXMLSignature(signedInfo, keyInfo).sign(context);
            var output = new StringWriter();
            var transformer = TransformerFactory.newInstance().newTransformer();
            transformer.setOutputProperty("omit-xml-declaration", "yes");
            transformer.transform(new DOMSource(document), new StreamResult(output));
            Files.writeString(Path.of(args[index + 1]), output.toString());
        }
    }
}
