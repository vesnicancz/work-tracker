# Synchronizace mezi zařízeními

**Stav:** návrh, zatím nic není implementované ani vyzkoušené. Odkazy do kódu odpovídají stavu k 2026-11-05 (`58f2466`), před implementací je ověř.

## Proč

Databáze se dnes mezi zařízeními přenáší na flash disku (`Database:Path`, `Database:Pooling = false`). Zařízení přibývají a ztráta flashky znamená ztrátu dat. Zařízení nejsou v jedné lokalitě, takže SMB ani VPN nepřipadají v úvahu.

## Požadavky

- Podporované klienty jsou Windows a macOS. Linux jako klient pro tento případ není potřeba, server ale v Linuxovém kontejneru běží.
- **Sync je volitelný a ve výchozím stavu vypnutý.** Aplikaci používají i kolegové, kterým se bez zapnutého sync nesmí změnit chování.
- Souběžná práce na více zařízeních se neřeší, zařízení se střídají.
- Práce offline je vzácná, ale musí fungovat, včetně zařízení, které se vrátí online s běžícím záznamem.
- Hosting je k dispozici: Proxmox, Portainer VM a Nginx Proxy Manager (NPM). Veřejné domény existují, doporučená je soukromá, aby server nezávisel na té firemní.

## Rozhodnutí

| Téma | Rozhodnutí | Důvod |
|---|---|---|
| Model | Lokální SQLite na každém zařízení a synchronizace na pozadí | Aplikace běží offline a stávající kód zůstává skoro beze změny |
| Backend | Vlastní malé ASP.NET Core API v Dockeru | Hosting je k dispozici, server přiděluje pořadí změn, data zůstávají na vlastním železe |
| Přenos | Nový typ pluginu `IWorkDataSyncPlugin` | Volitelnost, konfiguraci i zapínání řeší stávající systém pluginů |
| Zachycení změn, zápis a konflikty | Jádro (Application, Infrastructure) | Zachycení musí proběhnout v transakci zápisu a pravidla patří k doméně. Server i plugin zůstávají hloupý přenos. |
| Identita záznamu | Nový unikátní `Guid` sloupec ve `WorkEntry`, `int Id` zůstává PK | CLI `worklog edit <id>` a `delete <id>` berou číslo (`src/WorkTracker.CLI/Program.cs:250`, `:276`) a `id` je i ve výstupu `list` a `status`. Změna PK by změnila chování kolegům. |
| Mazání | V `WorkEntries` zůstává natvrdo, smazání se zapisuje jen do `SyncOutbox` | `WorkEntry` se kvůli sync nemusí učit tombstones |
| Migrace u kolegů | Přijatelná, schváleno | Přidá sloupec a prázdné tabulky a spustí jednorázovou zálohu přes `DatabaseMigrationBackup` |
| Šifrování na zařízení | Ne | Data leží na vlastním serveru, stačí HTTPS |

### Zamítnuté varianty

- **Soubor `.db` v OneDrive nebo Dropboxu:** klient může zkopírovat soubor uprostřed zápisu a konfliktní kopie se ručně nesloučí.
- **Centrální server bez lokální databáze:** nefunguje offline a `IUnitOfWork` stojí na zamykání SQLite, takže by se musel přepsat.
- **Logy zařízení ve sdílené cloudové složce:** funkční, ale s vlastním serverem zbytečně složité. Řeší se pořadí podle hodin, šifrování a další klient na každém zařízení.

## Architektura

```
Windows / macOS                                Portainer VM
+---------------------------------+            +------------------------+
| GUI / CLI                       |            | WorkTracker.SyncServer |
|   |                             |            | ASP.NET Core API       |
| WorkEntryService + UnitOfWork   |   HTTPS    | SQLite na volume       |
|   |                             |   (NPM)    | historie změn          |
| WorkEntries + SyncOutbox        |            |                        |
|   |                             |            |                        |
| WorkDataSyncService             |            |                        |
|   |                             |            |                        |
| IWorkDataSyncPlugin (Plugin.Sync) <--------> |                        |
+---------------------------------+            +------------------------+
```

### Co kam patří

**Plugin (`WorkTracker.Plugin.Sync`):**

- komunikace se serverem,
- konfigurace přes `PluginConfigurationField`: adresa (`Url`) a token (`Password`),
- test připojení přes `ITestablePlugin`,
- zapnutí a vypnutí přes stávající stav pluginů.

Rozhraní zhruba umí odeslat změny a stáhnout změny od určitého bodu. Plugin nemá přístup k databázi, podobně jako Stream Deck pracuje jen přes `IRemoteControlHost`. Pluginy se v `release.yml` vydávají jako samostatné artefakty, takže kolegové Plugin.Sync nemusí vůbec instalovat.

**Jádro:**

- `Guid` ve `WorkEntry`, generovaný ve `WorkEntry.Create`,
- zachycení změn v `UnitOfWork.SaveChangesAsync`, aktivní jen s řádkem v `SyncState`,
- `WorkDataSyncService`: pořadí odeslání a stažení, zápis stažených změn, pravidla konfliktů,
- zobrazení stavu a případná volba při připojení v GUI, obojí viditelné jen při zapnutém sync.

**Server (`WorkTracker.SyncServer`):**

- přijímá změny, přiděluje jim revize a vrací je od zadané revize,
- ukládá historii všech verzí,
- pravidla konfliktů nezná.

## Datový model klienta

Přidává jedna migrace. Protože upravuje data, patří k ní případ v `ExistingDatabaseMigrationTests`.

- **`WorkEntries.Guid`:** unikátní, migrace ho doplní i stávajícím řádkům. Kandidát na hodnotu nových záznamů je `Guid.CreateVersion7()`.
- **`SyncState`:** nejvýš jeden řádek s identitou zařízení a poslední staženou revizí. **Existence řádku znamená, že je sync zapnutý.** Stav je v databázi, protože na ni dosáhnou GUI i CLI. `ApplicationSettings` je v projektu Avalonia.
- **`SyncOutbox`:** neodeslané změny včetně smazání, zapisované ve stejné transakci jako data.
- **Revize záznamu:** pro detekci konfliktu úprav je potřeba znát revizi, ze které změna vychází. Místo uložení (sloupec, nebo malá tabulka mimo `WorkEntries`) je otevřené.

Lokální `int Id` se mezi zařízeními může lišit a slouží jen pro zobrazení a odkazy na daném zařízení. Identitu při synchronizaci nese `Guid`.

## Průběh synchronizace

- **Kdy:** v GUI při startu, po každé změně a pravidelně na pozadí. V CLI u každého příkazu s krátkým timeoutem.
- **Pořadí:** klient nejdřív odešle svoje změny, potom stáhne cizí.
- **Výpadek:** nikdy neblokuje práci. Změny čekají v `SyncOutbox`, CLI vypíše jen varování.
- **Obnovení UI:** změny zapsané na pozadí by mělo promítnout `ExternalWorkChangeMonitor`, který sleduje změny databázového souboru přes `IWorkDataChangeDetector`.
- **Kontroly:** zápis stažených změn obchází kontroly ve `WorkEntryService`, takže invarianty musí hlídat sám.
- **Protokol:** nese číslo verze a server nekompatibilního klienta odmítne.

### CLI

`CliCommandRequirements` dnes pro `start`, `stop`, `status`, `list`, `edit` a `delete` pluginy nenačítá, protože to stojí čas a může to selhat. Proto se o zapnutí sync rozhoduje podle `SyncState`:

- **Bez řádku:** CLI neudělá nic navíc kromě kontroly jednoho řádku a pluginy dál nenačítá.
- **S řádkem:** změna jde do fronty a CLI spustí plugin přes nový způsob použití v `CliPluginUse`.

### Připojení zařízení

Zapnutí vytvoří řádek v `SyncState` a podle stavu dat zvolí postup:

| Lokální databáze | Server | Postup |
|---|---|---|
| Má data | Prázdný | Nahrát lokální data |
| Prázdná | Má data | Stáhnout data ze serveru |
| Má data | Má data | Zeptat se. Na výběr je jen nahrazení lokálních dat ze serveru, nebo zrušení. Před nahrazením se lokální databáze zazálohuje. |

**Pozor:** `Guid` se migrací generuje v každé kopii databáze zvlášť. Dvě kopie téže databáze tak mají různé `Guid` a nesmí se obě nahrát, jinak se záznamy zdvojí. Proto se data nahrávají jen z jednoho zařízení a sloučení dvou neprázdných databází se nenabízí.

Vypnutí pluginu odstraní `SyncState` a `SyncOutbox`. Opětovné zapnutí je nové připojení. Případ, kdy byl plugin vypnutý s neodeslanými změnami, je potřeba v UI ošetřit upozorněním.

## Konflikty

Pravidla vyhodnocuje klient při zápisu stažených změn. Obě zařízení musí dojít ke stejnému výsledku, aby na pořadí zpracování nezáleželo.

| Situace po připojení | Výsledek |
|---|---|
| Běží dva záznamy | Starší skončí začátkem novějšího, běží novější |
| Offline ukončený záznam se překrývá se záznamem z jiného zařízení | Zůstanou oba, překryv se označí k vyřešení |
| Stejný záznam upravený na dvou místech | Vyhraje poslední změna přijatá serverem, přepsaná verze zůstane v historii, zobrazí se upozornění |
| Záznam smazaný na jednom zařízení a upravený offline na druhém | Záznam se vrátí s upozorněním, aby se data neztratila |

V jedné dávce se nejdřív aplikují ukončení a pak starty, jinak by zápis narazil na unikátní index běžícího záznamu (`WorkTrackerDbContext`, filtr `IsActive = 1`).

## Server a nasazení

- Docker image a `compose` soubor pro Portainer. V repu zatím žádný Dockerfile není.
- Pro jednoho uživatele stačí SQLite na volume, zálohuje se s Proxmoxem.
- Kontejner poslouchá jen HTTP na interním portu. TLS (Let's Encrypt) a routing řeší proxy host v NPM, aplikace musí respektovat forwarded hlavičky.
- Každé zařízení má vlastní token, který lze samostatně zrušit při ztrátě zařízení. Na straně klienta token drží konfigurace pluginu jako pole `Password`.
- Limit pokusů o přihlášení je přímo v API, aby nezávisel na konfiguraci proxy.
- Základní endpointy: `POST /changes` a `GET /changes?since=<revize>`.
- Server se kolegům do instalace nedostane, má vlastní image.

## Plán

### 1. Základ v jádru

- `Guid` ve `WorkEntry` s migrací a tabulky `SyncState` a `SyncOutbox`.
- Zachycení změn v `UnitOfWork`, aktivní jen s řádkem v `SyncState`.
- Testy, že se bez řádku nic nezapisuje a nevolá se síť a že se nemění výstup CLI.

Tento krok ještě nepotřebuje server. Až do připojení zařízení je pro všechny neviditelný.

### 2. Server

- `WorkTracker.SyncServer`, tokeny zařízení, limit pokusů, verze protokolu a historie.
- Dockerfile, `compose` a testy.
- Vydávání image v CI.

### 3. Plugin a synchronizace

- `IWorkDataSyncPlugin` a `WorkTracker.Plugin.Sync`.
- `WorkDataSyncService` včetně pravidel konfliktů, napojení na GUI a CLI.
- Průběh připojení, zobrazení stavu a upozornění.
- Dokumentace: `docs/plugins/sync.md`, `docs/user-guide.md` a `docs/architecture.md`.

### 4. Převod vlastních dat

- Nasazení serveru a proxy hostu v NPM.
- Nahrání databáze z flashky a připojení ostatních zařízení stažením.
- Flashka zůstává zálohou, dokud se sync neosvědčí.

### Později

Oblíbené položky a nastavení (`ApplicationSettings`), které nejsou v databázi. Tokeny pluginů se nepřenášejí záměrně.

## Otevřené body

- **Dvojité odeslání worklogů:** `PluginBasedWorklogSubmissionService` překryvy neřeší. Je potřeba ověřit, jestli je nehlídá něco jiného. Pokud ne, označený překryv by mohl poslat hodiny do Tempa dvakrát a odesílání potřebuje upozornění.
- **Překryvy ve `WorkEntryService`:** kontroly na řádcích 59, 131 a 224 můžou po synchronizaci vracet chyby u již existujícího překryvu. Je potřeba ověřit, že ho půjde úpravou vyřešit.
- **Zobrazení překryvů:** jak je uživateli ukázat, zatím navržené není.
- **Výběr pluginu:** kolik sync pluginů může být zapnutých. Předpoklad je nejvýš jeden.
- **Vícenásobný běh:** GUI a CLI můžou synchronizovat zároveň, zápis do lokální databáze i odesílání z fronty proto musí být bezpečné a idempotentní.
- **Tokeny v CLI:** `PluginSettingsReader` čte konfiguraci ze souboru nastavení, CLI by tedy tokeny mělo dostat stejně jako ostatní pluginy. Ověřit v praxi.
- **Dočasná záloha:** do dokončení lze snížit riziko ztráty flashky pravidelnou kopií databáze do cloudu přes `VACUUM INTO`. Ostrá databáze zůstává mimo synchronizovanou složku.
