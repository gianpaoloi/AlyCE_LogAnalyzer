# AlyCE Log Analyzer — Info e shpejtë

Lexo, filtro dhe ndjek logjet e strukturuara të aplikacioneve në Windows. Kjo faqe përfshihet edhe në ZIP-in portabël si `README.txt`.

## Instalimi

Çdo version publikohet në [faqen Releases](https://github.com/gianpaoloi/AlyCE_LogAnalyzer/releases) të depove. Zgjidh një nga tre mënyrat më poshtë.

### 1. Setup (e rekomanduar)

1. Shkarko `AlyCE-LogAnalyzer-Setup-{version}.exe` nga versioni i fundit.
2. Ekzekutoje. Nuk nevojiten të drejta administratori.
3. Nisme nga hyrja **AlyCE Log Analyzer** në menynë Start. Zgjidh **Create a desktop icon** gjatë instalimit nëse dëshiron një ikonë në desktop.

Setup-i:

- instalon për përdoruesin, në `%LOCALAPPDATA%\TeamSystem\AlyCE Log Analyzer`;
- instalon Microsoft Edge WebView2 Runtime kur mungon;
- shton hyrjen **Open with AlyCE Log Analyzer** në menynë e klikimit të djathtë për skedarët `.log`;
- lejon aplikacionin të përditësohet vetë më vonë (shih seksionin Përditësimet).

Për ta çinstaluar, përdor hyrjen **Uninstall AlyCE Log Analyzer** në menynë Start.

### 2. winget (ende jo e disponueshme)

```
winget install TeamSystem.AlyCELogAnalyzer
```

Kjo komandë funksionon vetëm pasi paketa të pranohet në depon e komunitetit winget. Deri atëherë përdor shkarkimin Setup më sipër.

### 3. Versioni portabël (ZIP)

Përdore versionin portabël kur nuk mund ose nuk dëshiron të instalosh: një kompjuter me kufizime, një ndarje rrjeti ose një memorie USB. Nuk kërkon të drejta administratori dhe nuk shkruan asgjë në regjistër.

1. Shkarko `AlyCE-LogAnalyzer-{version}-win-x64.zip` nga [faqja Releases](https://github.com/gianpaoloi/AlyCE_LogAnalyzer/releases) dhe nxirre kudo: në një dosje lokale, në një ndarje rrjeti ose në një memorie USB.
2. Kliko dy herë `Launch.bat`, ose ekzekuto drejtpërdrejt `Application\LogAnalyzer.Maui.exe`.

ZIP-i nxirret në këtë dosje:

```
AlyCE-LogAnalyzer-{version}-win-x64/
├── Application/       aplikacioni dhe runtime-i i tij
├── Launch.bat         e nis nga çdo vend ku e nxjerr
└── README.txt         kjo faqe
```

Nuk instalohet asgjë dhe nuk shkruhet asgjë në regjistër. WebView2 Runtime duhet të jetë tashmë i pranishëm (shih seksionin Kërkesat e sistemit). ZIP-i nuk përditësohet vetë: shkarko ZIP-in e ri kur publikohet një version.

### Instalimi në heshtje (për IT)

Paketa Setup pranon ndërprerësit standardë të Inno Setup:

```
AlyCE-LogAnalyzer-Setup-{version}.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-
```

Përdor `/SILENT` në vend të `/VERYSILENT` për të shfaqur një dritare progresi.

## Përditësimet

Aplikacioni desktop kontrollon nëse ekziston një version më i ri kur nis. Kur ka, mbi versionin në shiritin anësor shfaqet një rresht **Update to v…**.

- **I instaluar me Setup:** kliko rreshtin, lexo shënimet e versionit dhe shtyp **Shkarko dhe instalo**. Aplikacioni shkarkon paketën Setup, kontrollon SHA256-në e saj me versionin, e instalon në heshtje dhe rinisët me versionin e ri.
- **ZIP portabël:** rreshti thjesht të çon te faqja e versionit. Shkarko ZIP-in e ri dhe nxirre përsëri.

## Kërkesat e sistemit

- Windows 10 versioni 17763 (1809) ose më i ri, ose Windows 11. x64.
- Nuk nevojitet runtime-i .NET; paketa është e pavarur.
- Rekomandohen të paktën 4 GB RAM.
- **Microsoft Edge WebView2 Runtime**: ndërfaqja e aplikacionit funksionon brenda tij. Përfshihet në Windows 11 dhe në shumicën e Windows 10 të përditësuara. Setup-i e instalon kur mungon. ZIP-i kërkon që të jetë tashmë i pranishëm; shkarkoje nga [faqja WebView2 e Microsoft](https://developer.microsoft.com/microsoft-edge/webview2/) (nuk nevojiten të drejta administratori).

## Nisja e shpejtë

1. Nis aplikacionin nga menyja Start, nga ikona e desktopit ose nga `Launch.bat`.
2. Ngarko logjet në një nga mënyrat më poshtë. Mënyrat me skedarë përdorin panelin **Ngarko skedarë**.
3. Lexo rezultatin te **Përmbledhje**, përdor **Eksploruesi** për çdo rresht dhe **Triazhimi** për gabimet dhe paralajmërimet e grupuara.

### Mënyrat për të ngarkuar logjet

- **Shtegu i dosjes ose i skedarit:** shkruaj një shteg dosjeje (lokal ose `\\server\share`) dhe shtyp **Ngarko**. Kliko kutinë e shtegut bosh për të zgjedhur një dosje të përdorur më parë.
- **Tërhiq dhe zgjidh:** tërhiq skedarë `.log` ose një ZIP mbi zonën e lëshimit, ose kliko zonën për t'i zgjedhur.
- **Eksport `.json` nga Grafana:** në Grafana, përdor **Download logs** në faqen Logs për të ruajtur një skedar `.json`. Tërhiqe mbi zonën e lëshimit ose zgjidhe në të njëjtën mënyrë. Lexohet si skedar `.log`, dhe skedarët `.log`, `.zip` dhe `.json` mund të ngarkohen bashkë.
- **Ngjit nga kujtesa e fragmenteve:** kopjo rreshta nga një program tjetër, pastaj shtyp **Ngjit** në panel, ose shtyp Ctrl+V në çdo pjesë të faqes. Rreshtat e ngjitur ngarkohen si një log i vetëm.
- **Hap me:** me instalimin Setup, kliko me butonin e djathtë mbi një skedar `.log` në Explorer dhe zgjidh **Open with AlyCE Log Analyzer**.
- **Grafana Cloud:** importo logjet nga një faqe Loki, me filtrat dhe intervalin kohor të saj. Seksioni Importi nga Grafana Cloud më poshtë shpjegon hapat.
- **Një skedar që ende po rritet:** **File Live Watch** e ndjek ndërsa shkruhen rreshta të rinj. Seksioni i faqeve më poshtë e përshkruan.
- **Ngjarje të dërguara nëpër rrjet:** **Network Live Watch** merr ngjarje NLog pa asnjë skedar. Seksioni i faqeve më poshtë tregon si ta konfigurosh.

## Faqet

Shiriti anësor i grupon faqet në dy. **Analiza e skedarëve** punon me skedarë logjesh që janë tashmë në disk. **Ndjekja e drejtpërdrejtë** punon me ngjarjet që po vijnë tani.

### Përmbledhje

Statistika përmbledhëse, vëllimi i logjeve në kohë dhe grafikë ndarjeje për logjet e ngarkuara.

### Eksploruesi

Çdo rresht logu me filtra të avancuar. Kërko në tekstin e mesazhit, filtro sipas nivelit, mjedisit, kompanisë ose kohës nga titujt e kolonave, dhe shto kolona opsionale nga **Shto kolona…**. Kliko çdo rresht për hyrjen e plotë, me mesazhin e plotë dhe stack trace-in.

### Triazhimi

Të njëjtat gabime dhe paralajmërime, të grupuara sipas nënshkrimit të mesazhit. Pjesët e ndryshueshme të një mesazhi (id, GUID, shtigje, kohëzgjatje) normalizohen përpara grupimit. Kliko një rresht për të parë ngjarjet që e përbëjnë.

### File Live Watch

Ndjek një skedar logu ndërsa rritet, në një shteg lokal ose në një ndarje UNC. Zgjidh **from start** për të lexuar fillimisht përmbajtjen ekzistuese. Hiqe zgjedhjen e **auto-scroll** për të lexuar një rresht ndërsa vazhdon ndjekja; **Show N new lines** të sjell te rreshtat më të fundit.

### Network Live Watch

Merr ngjarje logjesh të dërguara nëpër rrjet nga objektivi `NLogViewer` i NLog, pa asnjë skedar loge.

1. Shto këtë në `NLog.config` të aplikacionit dërgues, pastaj rinise atë aplikacion:

   ```xml
   <target name="viewer" xsi:type="NLogViewer"
           address="udp://127.0.0.1:9999"
           includeScopeProperties="true" />
   <logger name="*" minlevel="Trace" writeTo="viewer" />
   ```

   `includeScopeProperties` plotëson kolonat Machine, Company, Username dhe Cid. Është i çaktivizuar si parazgjedhje. Me NLog 6 objektivi vjen nga paketa `NLog.Targets.Network`; NLog 5 e ka të integruar.
2. Te faqja, cakto të njëjtën portë dhe shtyp **Nis dëgjimin**. Faqja shfaq fragmentin për atë portë me një buton **Kopjo**.
3. Si parazgjedhje faqja dëgjon vetëm në këtë kompjuter (127.0.0.1). Zgjidh **all interfaces** për të pranuar ngjarje edhe nga kompjuterë të tjerë.

Nëse nuk shfaqet asgjë, kontrollo që porta të përputhet, që aplikacioni të jetë rinisur, dhe shenjën **N datagrams ignored**: do të thotë se po vjen diçka që nuk është ngjarje NLogViewer.

## Punimi me logjet

### Ngarkimi

- Shkruaj një shteg dosjeje dhe shtyp **Ngarko**, ose kliko kutinë bosh të shtegut për të zgjedhur një dosje të përdorur më parë. 12 shtegjet e fundit kujtohen mes sesioneve.
- Zgjidh **include DEBUG** për të ngarkuar edhe rreshtat e debug-ut. Përdor shumë më tepër memorie.
- Progresi tregon fazën aktuale, për shembull *parsing 7 / 31 files*, pastaj *sorting and computing statistics*.
- Kliko titullin **Ngarko skedarë** për ta mbyllur panelin kur të kesh mbaruar.

### Filtrimi

- Përdor titujt e kolonave për të filtruar sipas nivelit, logger-it ose përmbajtjes së mesazhit.
- Filtrat e shumëfishtë kombinohen.
- Filtrat mbeten aktivë kur ndërron faqe.

### Grafiku i vëllimit të logjeve

- Grafiku mbi rrjetën e Eksploruesit tregon hyrjet në kohë, të grumbulluara sipas nivelit, dhe gjithmonë pasqyron filtrat në përdorim.
- Madhësia e shtyllave përshtatet me intervalin kohor, p.sh. *1h per bar*.
- Tërhiq përgjatë shtyllave për të filtruar rrjetën sipas asaj dritareje kohore. Një klikim i vetëm zgjedh një shtyllë. **x**-i në çip pastron dritaren.
- Kliko një nivel në legjendë për ta filtruar. Kalo miun mbi një shtyllë për numërimet e sakta.

### Eksportimi

Përdor **Shkarko** për të eksportuar rezultatet e filtruara si CSV ose si rreshta origjinalë `.log`.

## Importi nga Grafana Cloud

Përmbledhja, Eksploruesi dhe Triazhimi mund të ngarkojnë logje drejtpërdrejt nga Grafana Cloud Loki.

1. Hap në shfletues faqen e logjeve Loki dhe kopjo adresën e saj.
2. Te dritarja e importit, ngjit adresën në kutinë e lidhjes, ose shtyp **Ngjit lidhjen** për ta lexuar nga kujtesa e fragmenteve. **Hap lidhjen** hap lidhjen e ngjitur në një skedë të re, ose faqen Explore të Grafana kur kutia është bosh. Shtyp **Ngarko** për të plotësuar ekipin, shërbimin, mjedisin, filtrat dhe intervalin kohor nga lidhja.
3. Zgjidh një hyrje: cookie-n `grafana_session` të sesionit tënd të shfletuesit, ose një token të llogarisë së shërbimit. Kliko **?** pranë kutisë për të parë ku gjenden të dyja.
4. Shtyp **Importo log-et**. Rreshtat shfaqen ndërsa shkarkohen, dhe **Anulo** ruan rreshtat e ngarkuar deri atëherë.

Për ta mbajtur mend hyrjen për herën tjetër, zgjidh **Mbaji mend hyrjen në këtë kompjuter**. Tokeni ose cookie-i ruhet i enkriptuar: për këtë llogari Windows në aplikacionin desktop, ose me çelësat e serverit në aplikacionin web. **Harro hyrjen** e heq. Trajto cookie-n ose tokenin si një fjalëkalim, dhe përdor **Harro hyrjen** në një kompjuter të përbashkët.

## Asistenti AI (opsional)

Asistenti AI është i çaktivizuar derisa të shtosh një ofrues. Teksti i logjeve dërgohet përmes internetit te ofruesi vetëm kur e kërkon ti, kurrë në sfond.

### Konfigurimi i një ofruesi

1. Kliko **AI** në shiritin e sipërm.
2. Shtyp **Shto ofrues**, zgjidh llojin dhe plotëso fushat e tij:
   - **Claude (Anthropic)** ose **ChatGPT (OpenAI)**: një çelës API dhe një model.
   - **Gateway tjetër ose model lokal**: një adresë dhe një model, plus një çelës API nëse serveri e kërkon.
3. Shtyp **Testo lidhjen**. Shfaqet një shenjë e gjelbër kur ofruesi përgjigjet. Shtyp **Ruaj**.
4. Shtyp **Përdore për shpjegimet** te ofruesi që dëshiron aktiv. Mund të ruash disa dhe të ndërrosh midis tyre.

Çelësi API ruhet i enkriptuar në këtë kompjuter. Një çelës nuk dërgohet kurrë te një adresë me `http` të thjeshtë, përveç nëse adresa është ky kompjuter.

Çdo ofrues i ruajtur tregon token-ët e përdorur përmes tij. **Rivendos përdorimin** pastron totalet e tij.

### Ofruesit e pajtueshëm

- **Claude (Anthropic):** API e Claude. Adresa është fikse. Përdor një çelës API nga Claude Console.
- **ChatGPT (OpenAI):** API e OpenAI. Adresa është fikse. Përdor një çelës API nga platforma OpenAI.
- **Google Gemini (API native):** API `generateContent` e vetë Gemini. Përdore për endpoint-in e Google ose për një gateway që shërben Gemini në mënyrë native, si Databricks AI Gateway. Shkruaj adresën deri te rrënja e gateway-it, p.sh. `https://<workspace>.azuredatabricks.net/ai-gateway/gemini`; aplikacioni shton `/v1beta/models/<modeli>:generateContent`. Shkruaj emrin e modelit siç e liston endpoint-i, dhe token-in ose çelësin që kërkon gateway-i (për Databricks, një personal access token). Adresa e Google plotësohet si parazgjedhje.
- **OpenAI Responses API:** API `/v1/responses` e OpenAI, të cilën e përdorin modelet më të reja të OpenAI, dhe gateway-et që e ofrojnë, si Databricks AI Gateway. Shkruaj adresën deri te rrënja e gateway-it, p.sh. `https://<workspace>.azuredatabricks.net/ai-gateway/mlflow`; aplikacioni shton `/v1/responses`. Shkruaj emrin e modelit siç e liston endpoint-i, dhe token-in ose çelësin që kërkon gateway-i. Adresa e OpenAI plotësohet si parazgjedhje.
- **Gateway tjetër ose model lokal:** çdo server që ofron API-n chat-completions të OpenAI (`/v1/chat/completions`). Shembuj: një gateway LiteLLM, që mund të drejtojë te Gemini, Claude dhe modele të tjera; Ollama; LM Studio; vLLM; serveri llama.cpp. Shkruaj adresën e serverit, me ose pa `/v1`, emrin e modelit saktësisht siç e liston serveri, dhe një çelës API vetëm nëse serveri e kërkon.

Jo të mbështetura drejtpërdrejt: Azure OpenAI dhe formate që nuk janë as OpenAI as Gemini. Arrije përmes një gateway-i që ofron një endpoint të pajtueshëm me OpenAI ose me API-n native të Gemini. Vetëm llojet Claude dhe ChatGPT kanë adresa fikse; një çelës nuk dërgohet kurrë përmes `http` të thjeshtë përveç nëse adresa është ky kompjuter, prandaj një gateway në distancë kërkon `https`.

### Ku ndihmon AI

- **Detajet e logut** (Eksploruesi, Live, Network): **Shpjego** përshkruan një mesazh dhe shkakun e mundshëm. Në Eksploruesi, **Gjej log-et e lidhura** liston hyrjet me të njëjtin CID, ose të njëjtin thread brenda dy minutash në të njëjtin mjedis. Këto rreshta të lidhur përfshihen në shpjegim.
- **Triazhimi**: **Analizo grupet me AI** renditi grupet në ekran në *Rregullo shpejt*, *Shiko më vonë* dhe *Ndoshta zhurmë*. Çdo grup ka edhe **Shpjego** dhe **Skicë çështjeje me AI**. Skica hapet në GitHub në shfletues, ku ende shtyp Dërgo. Aplikacioni kurrë nuk e publikon.
- **Përmbledhja**: **Përmblidh me AI** përshkruan skedarin e ngarkuar. Për një skedar me 50.000 hyrje ose më shumë, aplikacioni pyet së pari.
- **Eksploruesi**: shkruaj një pyetje me fjalë të thjeshta, p.sh. *gabime nga pagesat dje*, dhe **Pyet** vendos filtrat. Kur ke zgjedhur një interval kohe te grafiku, **Pse ndryshoi?** e krahason atë me një periudhë me të njëjtën gjatësi para tij, dhe **Përmblidh këtë interval** e përshkruan. Një interval i madh kërkon konfirmim së pari.
- **Live**: **Shpjego gabimet e reja me AI** vendos çdo gabim të ri në radhë për miratimin tënd. Asgjë nuk dërgohet derisa të shtypësh **Dërgo** mbi të.

### Shqyrto para dërgimit

Ndërrimi **Shqyrto tekstin para dërgimit** ndodhet pranë butonave AI. Është i çaktivizuar si parazgjedhje.

- **Jo i zgjedhur:** kërkesa dërgohet menjëherë pasi shtyp butonin. Teksti nuk shfaqet, por vlerat si adresat e e-mailit, adresat IP, fjalëkalimet dhe token-ët maskohen para se të largohet nga kompjuteri.
- **I zgjedhur:** teksti i saktë shfaqet dhe mund ta ndryshosh. Dërgo dërgon atë që është në kuti.

Çdo përgjigje tregon token-ët e përdorur nga kërkesa e saj, kur ofruesi i raporton.

## Zgjidhja e problemeve

### Aplikacioni nuk nis

- Sigurohu që përdor Windows 10 versioni 17763 ose më të ri.
- Provo të nxjerrësh ZIP-in në një dosje me shteg më të shkurtër.
- Nëse prapë dështon, provo ta ekzekutosh si administrator.

### "WebView2 Runtime required", ose "Couldn't find a compatible WebView2 Runtime"

- Microsoft Edge WebView2 Runtime mungon.
- Përgjigju **Po** te mesazhi për të hapur faqen e shkarkimit, ose shkarkoje nga [faqja WebView2 e Microsoft](https://developer.microsoft.com/microsoft-edge/webview2/), te seksioni *Evergreen Standalone Installer* (x64).
- Instalohet pa të drejta administratori dhe pa rinisje. Ri-ekzekutimi i paketës Setup e instalon gjithashtu.
- Kompjuterët e korporatës mund ta bllokojnë shkarkimin. Kërko nga IT *Microsoft Edge WebView2 Runtime*, një komponent standard i Microsoft.

### Logjet nuk ngarkohen

- Sigurohu që skedarët `.log` janë tekst i thjeshtë.
- Kontrollo që aplikacioni mund t'i lexojë skedarët.
- Provo fillimisht me një grup më të vogël skedarësh.

### Performanca është e ngadaltë

- Mbyll aplikacionet e tjera për të liruar memorie.
- Fshih kolonat që nuk të duhen.
- Filtro në një interval kohor më të ngushtë.

### Porta e Network Live Watch është e zënë

Vetëm një kopje e aplikacionit mund të dëgjojë në një portë UDP njëkohësisht. Nëse porta është e zënë, zgjidh një tjetër ose mbyll kopjen tjetër.

## Rreth

Versioni që po përdor shfaqet në fund të shiritit anësor të navigimit. Kliko për të kopjuar versionin e saktë dhe commit-in. Përfshije në çdo raport gabimi, ose përdor drejtpërdrejt lidhjen "Feedback" të aplikacionit.

**Platforma e synuar:** Windows x64

Për probleme ose kërkesa veçorish, kontakto autorin:

**Autori:** Gianpaolo Iannetta
**Email:** g.iannetta[at]teamsystem.com

**LinkedIn:** [https://www.linkedin.com/in/gianpaoloiannetta/](https://www.linkedin.com/in/gianpaoloiannetta/)

Faleminderit që e përdorni ;)
