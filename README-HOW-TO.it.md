# AlyCE Log Analyzer — Info rapide

Leggi, filtra e segui i log applicativi strutturati su Windows. Questa pagina è inclusa anche nello ZIP portable come `README.txt`.

## Installazione

Ogni release è pubblicata nella [pagina Releases](https://github.com/gianpaoloi/AlyCE_LogAnalyzer/releases) del repository. Scegli uno dei tre modi qui sotto.

### 1. Setup (consigliato)

1. Scarica `AlyCE-LogAnalyzer-Setup-{version}.exe` dall'ultima release.
2. Eseguilo. Non servono i diritti di amministratore.
3. Avvialo dalla voce **AlyCE Log Analyzer** del menu Start. Seleziona **Create a desktop icon** durante l'installazione se vuoi un'icona sul desktop.

Il setup:

- installa per utente, in `%LOCALAPPDATA%\TeamSystem\AlyCE Log Analyzer`;
- installa il Microsoft Edge WebView2 Runtime quando manca;
- aggiunge la voce **Open with AlyCE Log Analyzer** nel menu di scelta rapida (clic destro) per i file `.log`;
- permette all'app di aggiornarsi da sola in seguito (vedi la sezione Aggiornamenti).

Per disinstallare, usa la voce **Uninstall AlyCE Log Analyzer** del menu Start.

### 2. winget (non ancora disponibile)

```
winget install TeamSystem.AlyCELogAnalyzer
```

Questo comando funziona solo dopo che il pacchetto sarà accettato nel repository della comunità winget. Fino ad allora usa il download Setup qui sopra.

### 3. Versione portable (ZIP)

Usa la versione portable quando non puoi o non vuoi installare: un computer con restrizioni, una cartella di rete o una chiavetta USB. Non richiede diritti di amministratore e non scrive nulla nel registro.

1. Scarica `AlyCE-LogAnalyzer-{version}-win-x64.zip` dalla [pagina Releases](https://github.com/gianpaoloi/AlyCE_LogAnalyzer/releases) ed estrailo dove vuoi: una cartella locale, una cartella di rete o una chiavetta USB.
2. Fai doppio clic su `Launch.bat`, oppure esegui direttamente `Application\LogAnalyzer.Maui.exe`.

Lo ZIP si estrae in questa struttura:

```
AlyCE-LogAnalyzer-{version}-win-x64/
├── Application/       l'app e il suo runtime
├── Launch.bat         la avvia da qualunque posizione la estrai
└── README.txt         questa pagina
```

Non viene installato nulla e nulla viene scritto nel registro. Il WebView2 Runtime deve essere già presente (vedi la sezione Requisiti di sistema). Lo ZIP non si aggiorna da solo: scarica il nuovo ZIP quando viene pubblicata una release.

### Installazione silenziosa (per l'IT)

Il pacchetto Setup accetta i parametri standard di Inno Setup:

```
AlyCE-LogAnalyzer-Setup-{version}.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-
```

Usa `/SILENT` al posto di `/VERYSILENT` per mostrare una finestra di avanzamento.

## Aggiornamenti

L'app desktop controlla se esiste una release più recente quando si avvia. Se c'è, sopra la versione nella barra laterale compare una riga **Update to v…**.

- **Installata con Setup:** clicca sulla riga, leggi le note di rilascio e premi **Scarica e installa**. L'app scarica il pacchetto Setup, verifica il suo SHA256 rispetto alla release, lo installa in modo silenzioso e si riavvia sulla nuova versione.
- **ZIP portable:** la riga porta solo alla pagina della release. Scarica il nuovo ZIP ed estrailo di nuovo.

## Requisiti di sistema

- Windows 10 versione 17763 (1809) o successiva, oppure Windows 11. x64.
- Non serve un runtime .NET; il pacchetto è autonomo.
- Si consigliano almeno 4 GB di RAM.
- **Microsoft Edge WebView2 Runtime**: l'interfaccia dell'app gira al suo interno. È incluso in Windows 11 e nella maggior parte dei Windows 10 aggiornati. Il setup lo installa quando manca. Lo ZIP richiede che sia già presente; scaricalo dalla [pagina WebView2 di Microsoft](https://developer.microsoft.com/microsoft-edge/webview2/) (non servono i diritti di amministratore).

## Avvio rapido

1. Avvia l'app dal menu Start, dall'icona sul desktop o da `Launch.bat`.
2. Carica i log in uno dei modi sotto. I modi basati su file usano il pannello **Carica file**.
3. Leggi il risultato in **Panoramica**, usa **Esplora** per ogni riga e **Triage** per errori e avvisi raggruppati.

### Modi per caricare i log

- **Percorso di cartella o di file:** scrivi un percorso di cartella (locale o `\\server\share`) e premi **Carica**. Clicca nella casella del percorso vuota per scegliere una cartella già usata.
- **Trascina o scegli:** trascina sulla zona di caricamento file `.log` o uno ZIP, oppure cliccala per sceglierli.
- **Export `.json` di Grafana:** in Grafana, usa **Download logs** nella pagina Logs per salvare un file `.json`. Trascinalo sulla zona di caricamento oppure sceglilo allo stesso modo. Viene letto come un file `.log`, e file `.log`, `.zip` e `.json` si possono caricare insieme.
- **Incolla dagli appunti:** copia delle righe da un altro programma, poi premi **Incolla** nel pannello, oppure premi Ctrl+V in qualunque punto della pagina. Le righe incollate vengono caricate come un unico log.
- **Apri con:** con l'installazione Setup, fai clic destro su un file `.log` in Esplora file e scegli **Open with AlyCE Log Analyzer**.
- **Grafana Cloud:** importa i log da una pagina Loki, con i suoi filtri e l'intervallo di tempo. La sezione Import da Grafana Cloud qui sotto spiega i passaggi.
- **File che sta ancora crescendo:** **File Live Watch** lo segue mentre vengono scritte nuove righe. La sezione delle pagine qui sotto lo descrive.
- **Eventi inviati dalla rete:** **Network Live Watch** riceve eventi NLog senza alcun file. La sezione delle pagine qui sotto mostra come configurarlo.

## Le pagine

La barra laterale raggruppa le pagine in due. **Analisi dei file** lavora sui file di log già presenti sul disco. **Monitoraggio live** lavora sugli eventi che arrivano adesso.

### Panoramica

Statistiche riassuntive, volume dei log nel tempo e grafici di ripartizione per i log caricati.

### Esplora

Ogni riga di log con filtri avanzati. Cerca nel testo del messaggio, filtra per livello, ambiente, azienda o tempo dalle intestazioni delle colonne, e aggiungi colonne facoltative da **Aggiungi colonne…**. Clicca su una riga qualunque per la voce completa, con il messaggio intero e lo stack trace.

### Triage

Gli stessi errori e avvisi, raggruppati per firma del messaggio. Le parti variabili di un messaggio (id, GUID, percorsi, durate) vengono normalizzate prima del raggruppamento. Clicca su una riga per vedere le occorrenze che la compongono.

### File Live Watch

Segue un file di log mentre cresce, su un percorso locale o su una condivisione UNC. Seleziona **from start** per leggere prima il contenuto esistente. Deseleziona **auto-scroll** per leggere una riga mentre il tail continua; **Show N new lines** ti riporta alle ultime.

### Network Live Watch

Riceve gli eventi di log inviati in rete dal target `NLogViewer` di NLog, senza alcun file di log.

1. Aggiungi questo al `NLog.config` dell'applicazione che invia gli eventi, poi riavviala:

   ```xml
   <target name="viewer" xsi:type="NLogViewer"
           address="udp://127.0.0.1:9999"
           includeScopeProperties="true" />
   <logger name="*" minlevel="Trace" writeTo="viewer" />
   ```

   `includeScopeProperties` riempie le colonne Machine, Company, Username e Cid. È disattivato per impostazione predefinita. Con NLog 6 il target arriva dal pacchetto `NLog.Targets.Network`; NLog 5 lo include già.
2. Nella pagina, imposta la stessa porta e premi **Avvia l'ascolto**. La pagina mostra lo snippet per quella porta, con un pulsante **Copia**.
3. Per impostazione predefinita la pagina ascolta solo su questo computer (127.0.0.1). Seleziona **all interfaces** per accettare eventi anche dagli altri computer.

Se non compare nulla, controlla che la porta coincida, che l'applicazione sia stata riavviata e il badge **N datagrams ignored**: significa che arriva qualcosa che non è un evento NLogViewer.

## Lavorare con i log

### Caricamento

- Scrivi un percorso di cartella e premi **Carica**, oppure clicca nella casella del percorso vuota per scegliere una cartella già usata. Gli ultimi 12 percorsi vengono ricordati tra una sessione e l'altra.
- Seleziona **include DEBUG** per caricare anche le righe di debug. Usa molta più memoria.
- L'avanzamento mostra la fase corrente, per esempio *parsing 7 / 31 files*, poi *sorting and computing statistics*.
- Clicca l'intestazione **Carica file** per chiudere il pannello quando hai finito.

### Filtri

- Usa le intestazioni delle colonne per filtrare per livello, logger o contenuto del messaggio.
- I filtri multipli si combinano.
- I filtri restano attivi quando cambi pagina.

### Grafico dei volumi dei log

- Il grafico sopra la griglia di Esplora mostra le voci nel tempo, impilate per livello, e riflette sempre i filtri in uso.
- La dimensione delle barre si adatta all'intervallo di tempo, indicata per esempio come *1h per bar*.
- Trascina sulle barre per filtrare la griglia su quella finestra temporale. Un singolo clic seleziona una barra. La **x** sul chip cancella la finestra.
- Clicca un livello nella legenda per filtrarlo. Passa sopra una barra per i conteggi esatti.

### Esportazione

Usa **Scarica** per esportare i risultati filtrati come CSV o come righe `.log` originali.

## Import da Grafana Cloud

Panoramica, Esplora e Triage possono caricare i log direttamente da Grafana Cloud Loki.

1. Apri nel browser la pagina dei log di Loki e copia il suo indirizzo.
2. Nella finestra di import, incolla l'indirizzo nella casella del link, oppure premi **Incolla link** per leggerlo dagli appunti. **Apri link** apre il link incollato in una nuova scheda, oppure la pagina Explore di Grafana se la casella è vuota. Premi **Carica** per riempire team, servizio, ambiente, filtri e intervallo di tempo dal link.
3. Scegli un accesso: il cookie `grafana_session` della tua sessione del browser, oppure un token di service account. Clicca **?** accanto alla casella per sapere dove trovare l'uno o l'altro.
4. Premi **Importa log**. Le righe compaiono mentre vengono scaricate, e **Annulla** conserva le righe caricate fino a quel momento.

Per ricordare l'accesso alla prossima volta, seleziona **Ricorda l'accesso su questo computer**. Il token o il cookie viene salvato cifrato: per questo account Windows nell'app desktop, oppure con le chiavi del server nell'app web. **Dimentica l'accesso** lo elimina. Tratta cookie o token come una password, e usa **Dimentica l'accesso** su un computer condiviso.

## Risoluzione dei problemi

### L'applicazione non si avvia

- Verifica di usare Windows 10 versione 17763 o successiva.
- Prova a estrarre lo ZIP in una cartella con un percorso più corto.
- Se continua a non funzionare, prova ad eseguirla come amministratore.

### "WebView2 Runtime required", oppure "Couldn't find a compatible WebView2 Runtime"

- Il Microsoft Edge WebView2 Runtime manca.
- Rispondi **Sì** al messaggio per aprire la pagina di download, oppure scaricalo dalla [pagina WebView2 di Microsoft](https://developer.microsoft.com/microsoft-edge/webview2/), nella sezione *Evergreen Standalone Installer* (x64).
- Si installa senza diritti di amministratore e senza riavvio. Rieseguire il pacchetto Setup lo installa a sua volta.
- Alcuni computer aziendali bloccano il download. Chiedi all'IT *Microsoft Edge WebView2 Runtime*, un componente standard Microsoft.

### I log non si caricano

- Verifica che i file `.log` siano di testo semplice.
- Controlla che l'applicazione possa leggere i file.
- Prova prima con un numero minore di file.

### Le prestazioni sono lente

- Chiudi le altre applicazioni per liberare memoria.
- Nascondi le colonne che non ti servono.
- Filtra su un intervallo di tempo più ristretto.

### Porta di Network Live Watch occupata

Su una porta UDP può essere in ascolto una sola copia dell'app alla volta. Se la porta è occupata, scegline un'altra oppure chiudi l'altra copia.

## Informazioni

La versione in uso è mostrata in fondo alla barra di navigazione laterale. Cliccala per copiare la versione esatta e il commit. Includila in ogni segnalazione di bug, oppure usa direttamente il link "Feedback" dell'app.

**Piattaforma di destinazione:** Windows x64

Per problemi o richieste di funzionalità, contatta l'autore:

**Autore:** Gianpaolo Iannetta
**Email:** g.iannetta[at]teamsystem.com

**LinkedIn:** [https://www.linkedin.com/in/gianpaoloiannetta/](https://www.linkedin.com/in/gianpaoloiannetta/)

Grazie per averla usata ;)
