# IPS Middleware — პროცესების დიაგრამები

ქართული, რედაქტირებადი draw.io sequence დიაგრამები, ძველი რეპოზიტორიის ფორმატის მიხედვით. აღწერს მიმდინარე კოდს **0636b9c** (`codex/payment-initiation`, 2026-10-07), და არა ყველა შესაძლო სამიზნე ბიზნესპროცესს.

## გახსნა

- [ყველა flow ერთ draw.io ფაილში](IPS_All_Flows_KA.drawio) — 25 გვერდი; გვერდი აირჩიეთ draw.io-ს ქვედა ჩანართებით.
- [ლოკალური preview](preview.html) — იხსნება ბრაუზერში; გარე ქსელსა და სერვისებს არ იყენებს.
- ქვემოთ მოცემული ცალკეული ფაილები იგივე გვერდებს შეიცავს, შეტყობინების ტიპის მიხედვით. საერთო და ცალკეული ვერსიები ერთი snapshot-ია; შემდგომი რედაქტირებისას სინქრონულად განაახლეთ.

API, Application და worker lifeline-ები ლოგიკური როლებია ერთ executable-ში; ისინი დამოუკიდებელ production host-ებს არ ნიშნავს. SQL commit მწვანეა, პასუხი პუნქტირითაა, მნიშვნელოვანი პირობა/ლიმიტი ყვითლადაა მონიშნული. პირობითი ნაბიჯები მხოლოდ შესაბამისი პირობის შესრულებისას ხდება.

## გვერდები

| გვერდი | flow | draw.io ფაილი |
|---|---|---|
| 01 | მიღება, შენახვა და MessageAck | [00_Runtime.drawio](00_Runtime.drawio) |
| 02 | რამდენიმე ინსტანცია და shutdown | [00_Runtime.drawio](00_Runtime.drawio) |
| 03 | გამავალი pacs.008 — ძირითადი გზა | [IPS_Seq_pacs008_KA.drawio](IPS_Seq_pacs008_KA.drawio) |
| 04 | გამავალი pacs.008 — უცნობი შედეგი | [IPS_Seq_pacs008_KA.drawio](IPS_Seq_pacs008_KA.drawio) |
| 05 | შემომავალი pacs.008 — CBS-ის გადაწყვეტილება | [IPS_Seq_pacs008_KA.drawio](IPS_Seq_pacs008_KA.drawio) |
| 06 | შემომავალი pacs.008 — გაურკვევლობა და reversal | [IPS_Seq_pacs008_KA.drawio](IPS_Seq_pacs008_KA.drawio) |
| 07 | pacs.028 — უცნობი pacs.008-ის გამოკვლევა | [IPS_Seq_pacs028_KA.drawio](IPS_Seq_pacs028_KA.drawio) |
| 08 | 1016-ის შემდეგ ავტორიზებული pacs.008 resend | [IPS_Seq_pacs028_KA.drawio](IPS_Seq_pacs028_KA.drawio) |
| 09 | IPS-ის ასინქრონული pacs.002 | [IPS_Seq_pacs002_KA.drawio](IPS_Seq_pacs002_KA.drawio) |
| 10 | ჩვენი pacs.002 — შენახული პასუხი და retry | [IPS_Seq_pacs002_KA.drawio](IPS_Seq_pacs002_KA.drawio) |
| 11 | გამავალი pacs.009 | [IPS_Seq_pacs009_KA.drawio](IPS_Seq_pacs009_KA.drawio) |
| 12 | შემომავალი pacs.009 | [IPS_Seq_pacs009_KA.drawio](IPS_Seq_pacs009_KA.drawio) |
| 13 | გამავალი pacs.004 — დაბრუნება | [IPS_Seq_pacs004_KA.drawio](IPS_Seq_pacs004_KA.drawio) |
| 14 | შემომავალი pacs.004 | [IPS_Seq_pacs004_KA.drawio](IPS_Seq_pacs004_KA.drawio) |
| 15 | გამავალი camt.056 — თანხის გამოთხოვა | [IPS_Seq_camt056_KA.drawio](IPS_Seq_camt056_KA.drawio) |
| 16 | შემომავალი camt.056 | [IPS_Seq_camt056_KA.drawio](IPS_Seq_camt056_KA.drawio) |
| 17 | გამავალი camt.029 — უარი | [IPS_Seq_camt029_KA.drawio](IPS_Seq_camt029_KA.drawio) |
| 18 | შემომავალი camt.029 | [IPS_Seq_camt029_KA.drawio](IPS_Seq_camt029_KA.drawio) |
| 19 | შემომავალი pain.001 — CBS-ში მიწოდება | [IPS_Seq_pain001_KA.drawio](IPS_Seq_pain001_KA.drawio) |
| 20 | pain.001 — შემდგომი ბიზნესგადაწყვეტილება | [IPS_Seq_pain001_KA.drawio](IPS_Seq_pain001_KA.drawio) |
| 21 | გამავალი pain.002 — ინიციაციის უარი | [IPS_Seq_pain002_KA.drawio](IPS_Seq_pain002_KA.drawio) |
| 22 | შემომავალი camt.055 — გაუქმების მოთხოვნა | [IPS_Seq_camt055_KA.drawio](IPS_Seq_camt055_KA.drawio) |
| 23 | Proxy — რეგისტრაცია, განახლება, წაშლა | [IPS_Seq_Proxy_KA.drawio](IPS_Seq_Proxy_KA.drawio) |
| 24 | საბოლოო სტატუსი — callback და GET | [IPS_Seq_StatusDelivery_KA.drawio](IPS_Seq_StatusDelivery_KA.drawio) |
| 25 | სხვა outgoing ტიპები — duplicate recovery | [IPS_Seq_Recovery_KA.drawio](IPS_Seq_Recovery_KA.drawio) |

## წაკითხვის მნიშვნელოვანი წესები

- pacs.008-ის საწყისი დამუშავება პირდაპირ იწყება; SQL discovery / bounded channel აღდგენას ემსახურება. რიგი მფლობელობას არ ანიჭებს: პირველმა წარმატებულმა SQL claim-მა უნდა გაიმარჯვოს.
- შემომავალ pacs.008-ზე MessageAck არ იგზავნება. პასუხი შენახული pacs.002-ია. სხვა მხარდაჭერილი ტიპების ACK commit-ის შემდეგ დამოუკიდებლად იგზავნება.
- პირველი incoming reply დაუყოვნებლივ სრულდება processing composition-იდან, ახალი scope-ით; dedicated reply worker დარჩენილ და განმეორებით სამუშაოს აგრძელებს.
- დიაგრამის რამდენიმე თანმიმდევრული COMMIT არ არის ერთი ხანგრძლივი ტრანზაქცია. SQL ტრანზაქცია HTTP call-ს არ ფარავს.
- არხები ლოკალური bounded channels-ია; message broker არ გამოიყენება. ACK რიგში მცირე receipt metadata ინახება, processing/reply რიგებში — journal ID-ები, outgoing recovery-ში — payment ID-ები.
- CBS callback არის at-least-once. SQL ownership გარე სისტემის exactly-once შესრულების გარანტია არ არის.
- თანხის დაბრუნება, recall-ის ტექნიკური მიღება და დასრულებული reversal სხვადასხვა შედეგებია. Reversal-ზე 2xx მხოლოდ მოთხოვნის მიღებას ადასტურებს.
- Incoming camt.056/029/055 არქივდება: CBS შეტყობინება და ბიზნესქმედება არ სრულდება. pain.001-ის შემდგომი თანხმობა/უარი და შესაბამისი ვადა CBS-ის პასუხისმგებლობაა.
- IPS signing development-only unsigned გამონაკლისით მუშაობს. Proxy-ის optional signing ცალკე, უფრო სუსტი შეთანხმებული პოლიტიკაა. Certificate validity-ის 012b მხოლოდ დაუმთავრებელი სპეციფიკაციაა; ის დიაგრამებში რეალიზებულ ფუნქციად არ არის ნაჩვენები.
- მოცემული timeout-ები და ლიმიტები მიმდინარე კონფიგურაციის დეფოლტებია. live processing ნაგულისხმევად გამორთულია.

## საფუძველი და შემოწმება

წყაროები: `src/IPS.Middleware.Application`-ის workflows, `src/IPS.Middleware.Infrastructure`-ის runtime/transport/repositories, API routes და `appsettings.json`; თითო გვერდის ბოლოში მითითებულია შესაბამისი კლასები/სპეციფიკაციები. ძველი `IPS.MiidleWear/docs/IPS_Seq_*_KA.drawio` გამოყენებულია ფორმატის საცნობაროდ; ძველი 202/dispatcher flow არ გადმოტანილა მიმდინარე სინქრონულ API-ზე.

25-ვე გვერდის flow დამოუკიდებლად გადამოწმდა კოდთან. შემოწმებულია XML სტრუქტურა, უნიკალური cell ID-ები, საერთო/ცალკეული ფაილების თანხვედრა და preview-ში ტექსტის ჩატევა. რამდენიმე წარმომადგენლობითი გვერდი ვიზუალურად დათვალიერებულია ლოკალური Chrome რენდერით. Native draw.io renderer ამ გარემოში არ იყო ხელმისაწვდომი; preview იგივე ტექსტებსა და კოორდინატებს იყენებს.

[აუდიტის ხელახალი შემოწმება](../reviews/business-flow-audit-recheck-2026-10-07.md): ოთხი საწყისი defect გასწორებულია კოდში; ACK readiness-ის ერთი დამატებითი შემთხვევა ღიაა. ამ სამუშაოში production კოდი არ შეცვლილა და ტესტები თავიდან არ გაშვებულა.
