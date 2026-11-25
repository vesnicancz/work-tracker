# MCP server jako plugin

**Stav:** implementováno, zbývá ruční ověření (viz konec). Odkazy do kódu odpovídají stavu k 2026-11-18 (`809ba9b`), tedy před implementací. Odchylky od návrhu: `PreviewWorklogsAsync` vrací `PluginResult`, aby mohl odmítnout neplatný režim, a nové typy jsou v `RemoteWorklogs.cs`.

## Proč

AI asistenti jako Claude Code mají umět číst a ovládat WorkTracker: zjistit, co běží, spustit a zastavit práci, opravit popisy a tickety záznamů a odeslat worklogy. Standardem pro takové napojení je [Model Context Protocol](https://modelcontextprotocol.io/) (MCP).

## Požadavky

- Číst stav a záznamy za zvolené období.
- Spouštět, zastavovat a obnovovat práci (to host už umí).
- **Upravovat popisy a nastavovat ID ticketů** existujících záznamů.
- **Odesílat worklogy** přes stávající pluginy (`IWorklogUploadPlugin`).
- Plugin je volitelný a ve výchozím stavu vypnutý, kolegům bez něj se nic nemění.

## Rozhodnutí

| Téma | Rozhodnutí | Důvod |
|---|---|---|
| Kde server běží | Plugin `WorkTracker.Plugin.Mcp` typu `IRemoteControlPlugin` v GUI | `IRemoteControlHost` už má většinu potřebného API. Změny jdou přes běžící aplikaci, takže okno, Pomodoro, Luxafor i Stream Deck zareagují hned. Návrhy z Jiry a O365 jdou přes sdílenou cache a tokeny GUI. |
| Transport | Streamable HTTP na `http://localhost:<port>/mcp` | Klient plugin nemůže spustit, takže stdio nepřipadá v úvahu |
| HTTP server | `HttpListener` + `StreamableHttpServerTransport` z `ModelContextProtocol.Core` | `ModelContextProtocol.AspNetCore` potřebuje sdílený framework `Microsoft.AspNetCore.App`, který GUI nenačítá, a plugin si ho v izolovaném `AssemblyLoadContext` přinést nemůže |
| Relace | Bezstavový režim (`Stateless = true`), každý POST má vlastní `McpServer` | Není co držet ani uklízet. Server neposílá nic mimo odpověď, což nástroje nepotřebují. |
| Autentizace | Statický bearer token z konfigurace pluginu (`Password`), porovnání v konstantním čase | Jednoduché a stačí na localhost. OAuth by byl zbytečně složitý. |
| Ochrana před prohlížečem | Cizí `Origin` → 403, prefix `localhost` | Webová stránka nedosáhne na server přes prohlížeč uživatele. Požadavky s jiným `Host` (DNS rebinding) nesedí na prefix. |
| Chyby nástrojů | `CallToolResult` s `IsError = true` a textem z `PluginResult.Error` | Model vidí důvod a může se opravit. `McpException` vyhozená z nástroje se v SDK 2.4.0 modelu nepředala, přišlo jen obecné „An error occurred invoking …“. |
| Odesílání worklogů | Náhled a odeslání jako dva nástroje, odeslání s `destructiveHint` a vypnuté přepínačem v konfiguraci | Odeslaný worklog nejde vzít zpět |

### Zamítnuté varianty

- **Samostatný CLI nástroj se stdio transportem:** funguje i bez GUI, ale zapisuje do SQLite mimo běžící aplikaci, takže okno, Pomodoro ani Stream Deck se o změně nedozvědí. Návrhy by musel načítat a přihlašovat sám, a interaktivní MSAL přihlášení v procesu bez okna, který spustil MCP klient, je nepraktické. Pokud bude MCP potřeba i bez GUI (například proti sync serveru z [multi-device-sync.md](multi-device-sync.md)), dá se doplnit později.
- **ASP.NET Core v pluginu:** viz řádek *HTTP server*. Šlo by to jen přes `FrameworkReference` přímo v Avalonia projektu, a to kvůli jednomu volitelnému pluginu nestojí za to.
- **Stavový režim se SSE na GET:** potřeboval by evidenci relací a jejich úklid. Hodí se jen pro notifikace ze serveru, které zatím nejsou potřeba.

## Co je hotové (prototyp)

Větev obsahuje plugin `plugins/WorkTracker.Plugin.Mcp/`:

- `McpPlugin` – konfigurace (port, výchozí 47821, a povinný token aspoň o 16 znacích), spuštění serveru v `AttachAsync`, zastavení v `OnShutdownAsync` a `OnDisposeAsync`. V CLI zůstává neaktivní, protože host dostane jen GUI.
- `McpHttpServer` – smyčka `HttpListener`, kontroly (cesta, `Origin`, token, metoda, `Accept`), deserializace `JsonRpcMessage`, `McpServer` pro každý požadavek a `DeferredStartStream`. Ten zadrží flush až do prvního zápisu, takže na notifikaci jde ještě vrátit 202.
- `WorkTrackerTools` – ukázkové nástroje `get_status`, `start_work` a `stop_work`.
- `ModelContextProtocol.Core` 2.4.0 v `Directory.Packages.props`, projekt ve `WorkTracker.slnx` a cesta v `src/WorkTracker.Avalonia/appsettings.Development.json`.

### Co bylo ověřeno

Dočasný hostitel mimo repozitář načetl plugin stejně jako `PluginLoadContext` v čistém .NET procesu bez ASP.NET Core:

- `ModelContextProtocol.Core` i `Microsoft.Extensions.AI.Abstractions` se načetly do kontextu pluginu.
- Oficiální C# klient (`ModelContextProtocol.Core` 2.4.0, `HttpClientTransport`) z jiného procesu se připojil, vyjednal protokol 2026-07-28, vypsal nástroje a zavolal je. Chyba z hosta došla jako `isError` s textem.
- Přes surové HTTP s protokolem 2025-06-18: `initialize` vrátí 200, `notifications/initialized` 202.
- Bez tokenu nebo se špatným tokenem 401, cizí `Origin` 403, `Origin` z localhostu projde, bez `Accept` 406, neplatný JSON 400, GET 405. Po `ShutdownAsync` server neposlouchá.

**Neověřeno:** běh uvnitř GUI a připojení z Claude Code, macOS a Linux (managed `HttpListener` místo http.sys), dva pluginy s vlastní kopií MCP knihovny najednou (GoranG3 ji používá jako klient).

## Rozšíření `IRemoteControlHost`

Implementuje ho jen `src/WorkTracker.Avalonia/Services/RemoteControlHost.cs`, takže se mění bez ohledu na zpětnou kompatibilitu. Stream Decku nové členy nevadí.

```csharp
/// <summary>
/// Gets the entries that start between the start of <paramref name="from"/> and the end of
/// <paramref name="to"/>, in order of their start.
/// </summary>
Task<IReadOnlyList<RemoteWorkEntry>> GetWorkEntriesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

/// <summary>
/// Replaces the ticket and the description of an entry, null clears it. The times stay as they are.
/// Fails when neither would be left.
/// </summary>
Task<PluginResult<RemoteWorkEntry>> UpdateWorkEntryTextAsync(int id, string? ticketId, string? description, CancellationToken cancellationToken);

/// <summary>
/// Gets the enabled worklog upload plugins.
/// </summary>
IReadOnlyList<RemoteWorklogProvider> GetWorklogProviders();

/// <summary>
/// Gets what <see cref="SubmitWorklogsAsync"/> would send for the same arguments.
/// </summary>
Task<IReadOnlyList<RemoteWorklogDay>> PreviewWorklogsAsync(DateOnly date, RemoteWorkedTimePeriod period, WorklogSubmissionMode mode, CancellationToken cancellationToken);

/// <summary>
/// Submits the completed work of the day or the week of <paramref name="date"/>. Can't be taken back.
/// </summary>
Task<PluginResult<RemoteSubmissionResult>> SubmitWorklogsAsync(DateOnly date, RemoteWorkedTimePeriod period, WorklogSubmissionMode mode, string providerId, CancellationToken cancellationToken);
```

Nové typy patří do `src/WorkTracker.Plugin.Abstractions/RemoteControlState.cs` (nebo do nového souboru vedle něj). Plugin na Application nevidí, takže nesmí používat `WorkEntry`, `WorklogDto` ani `SubmissionResult`. `WorklogSubmissionMode` už v `Plugin.Abstractions` je.

```csharp
/// <param name="Id">Stable for the lifetime of the entry, used to edit it.</param>
/// <param name="EndTime">Null while the work is being tracked.</param>
public sealed record RemoteWorkEntry(int Id, string? TicketId, string? Description, DateTimeOffset StartTime, DateTimeOffset? EndTime);

public sealed record RemoteWorklogProvider(string Id, string Name, WorklogSubmissionMode SupportedModes);

public sealed record RemoteWorklogDay(DateOnly Date, IReadOnlyList<RemoteWorklog> Worklogs);

/// <param name="StartTime">In aggregated mode the earliest start of the group, not a real interval.</param>
public sealed record RemoteWorklog(string? TicketId, string? Description, DateTimeOffset StartTime, int DurationMinutes);

/// <param name="Errors">What went wrong, with the position of the worklog in the submitted list when known.</param>
public sealed record RemoteSubmissionResult(int Total, int Successful, int Failed, IReadOnlyList<RemoteSubmissionError> Errors);

public sealed record RemoteSubmissionError(int? WorklogIndex, string Message);
```

### Proč takhle

- **Úprava nahrazuje celé hodnoty.** `WorkEntryChanges` (`src/WorkTracker.Application/DTOs/WorkEntryChanges.cs`) neumí hodnotu smazat, protože null u něj znamená „beze změny“. Agent ale musí umět odebrat špatně přiřazený ticket. Host proto přečte záznam a zavolá `UpdateWorkEntryAsync` s původními časy.
- **Časy zatím ne.** Změna začátku nebo konce naráží na překryvy a host se nemá jak zeptat uživatele (viz `AddWorkEntryAsync`, který v takovém případě selže). Případně později jako samostatná metoda se stejným pravidlem.
- **`providerId` je povinný.** `IWorklogSubmissionService.SubmitAsync` sice při jediném providerovi přijme null, ale agent seznam providerů stejně načte, ať tedy cíl zvolí výslovně.
- **Režim (`Timed`/`Aggregated`) volí volající.** Musí to být jediná hodnota (`IsSingleMode()`) a provider ho musí podporovat (`SupportedModes`), jinak host vrátí `Validation`.
- **Období ve stejném tvaru jako `GetWorkedTimeAsync`**, tedy den nebo týden přes `RemoteWorkedTimePeriod`. Odpovídá to `SubmissionPeriod.Day` a `SubmissionPeriod.WeekOf`.

### Implementace v `RemoteControlHost`

- **Čtení záznamů:** `_workEntryService.GetWorkEntriesByDateRangeAsync(from, to)` a filtr na začátek v rozsahu. Převod časů přes stávající `ToLocalOffset`.
- **Úprava:** přes `RunAsync`, aby šla na UI vlákno a ohlásila výsledek (`IActionFeedbackService`, nový lokalizační klíč typu `RemoteEntryUpdated`). Záznam načíst přes `_workEntryService.GetWorkEntryByIdAsync` (neexistuje → `NotFound`). Zapsat přes `_worklogStateService.UpdateWorkEntryAsync(id, ticketId, start, end, description)`, ne přímo přes `IWorkEntryService`: jen tak se vyvolá `WorkEntriesModified` a okno se obnoví. Pokud jde o aktivní záznam, ověř, že `UpdateWorkEntryAsync` s `EndTime = null` aktualizuje i `ActiveWork` (jinak by Stream Deck a hlavní okno ukazovaly starý popis).
- **Pravidla záznamu:** že zůstane aspoň jeden z ticketu a popisu, hlídá doména (`WorkEntry`, `src/WorkTracker.Domain/Entities/WorkEntry.cs:33`). Formát ticketu nehlídá nikdo. Chybu převést přes stávající `ToPluginResult` (`_localization.LocalizeError`), host nic nevaliduje sám.
- **Odesílání:** stejnou cestou jako dialog, tedy `IWorklogSubmissionService.PreviewAsync(period, mode)` a pak `SubmitCustomWorklogsAsync(worklogs, providerId, mode)`, viz `WorklogSubmissionOrchestrator`. Odeslání sestaví náhled znovu na straně hosta, nebere data od klienta. Výsledek ohlásit stejně jako dialog (`FormatSubmissionStatus` v orchestrátoru, případně ho přesunout, aby ho mohl použít i host).
- **Nové závislosti hosta:** `IWorklogSubmissionService` (nebo `IWorklogSubmissionOrchestrator`). Registrace v `PresentationServices.cs` se nemění, `RemoteControlHost` je už v DI.

## MCP nástroje

| Nástroj | Volání hosta | Anotace |
|---|---|---|
| `get_status` | `GetStateAsync` | `readOnly` |
| `list_work_entries(from, to)` | `GetWorkEntriesAsync` | `readOnly` |
| `get_worked_time(date, period)` | `GetWorkedTimeAsync` | `readOnly` |
| `get_suggestions` | `GetSuggestionsAsync` | `readOnly`, `openWorld` (volá Jiru a O365) |
| `get_favorites` | `GetFavorites` | `readOnly` |
| `start_work(ticketId?, description?)` | `StartWorkAsync` | |
| `stop_work` | `StopWorkAsync` | `idempotent` |
| `resume_last_work` | `ResumeLastWorkAsync` | |
| `add_work_entry(ticketId?, description?, start, end?)` | `AddWorkEntryAsync` | |
| `update_work_entry(id, ticketId?, description?)` | `UpdateWorkEntryTextAsync` | `idempotent` |
| `list_worklog_providers` | `GetWorklogProviders` | `readOnly` |
| `preview_worklogs(date, period, mode)` | `PreviewWorklogsAsync` | `readOnly` |
| `submit_worklogs(date, period, mode, providerId)` | `SubmitWorklogsAsync` | `destructive`, `openWorld`. Registrovat jen při zapnutém přepínači. |

Zásady:

- Popis každého nástroje říká, co dělá a co vrací. U `update_work_entry` výslovně uvést, že vynechaný nebo null parametr hodnotu smaže, a ID záznamu získat z `list_work_entries`. U `submit_worklogs` uvést, že před ním má proběhnout `preview_worklogs` a odsouhlasení uživatelem.
- Volitelné parametry mají výchozí hodnotu v signatuře lambdy, jinak je SDK ve schématu označí jako povinné.
- Datum jako `DateOnly` (ISO `yyyy-MM-dd`), čas jako `DateTimeOffset` s posunem. `TimeSpan` se serializuje jako `hh:mm:ss`, což modely čtou dobře.
- Pomodoro a dialog nového záznamu do MCP nedávat: první k AI nepatří, druhý otevře okno, které agent nevidí.
- `ServerInstructions` v `McpServerOptions`: krátce, že časy jsou v místním čase uživatele, týden začíná v pondělí a změny se okamžitě projeví v aplikaci.

## Konfigurace pluginu

| Klíč | Typ | Výchozí | Poznámka |
|---|---|---|---|
| `port` | `Number` | 47821 | 1024–65535 |
| `access_token` | `Password` | – | povinný, aspoň 16 znaků bez mezer |
| `allow_worklog_submission` | `Checkbox` | vypnuto | bez něj se `submit_worklogs` nezaregistruje |

Obsazený port: `HttpListener.Start` vyhodí `HttpListenerException`. Teď ji `AttachAsync` propustí ven. Před dokončením ověř, jak s tím naloží `IRemoteControlCoordinator`, a pokud výjimku jen zaloguje, ohlas ji uživateli jako chybu pluginu, aby nebyl potichu nefunkční.

Případně doplnit `ITestablePlugin`, který jen zkusí port otevřít a zavřít.

## Připojení klientů

Claude Code:

```
claude mcp add --transport http worktracker http://localhost:47821/mcp --header "Authorization: Bearer <token>"
```

VS Code (`.vscode/mcp.json` nebo uživatelská konfigurace):

```json
{
  "servers": {
    "worktracker": {
      "type": "http",
      "url": "http://localhost:47821/mcp",
      "headers": { "Authorization": "Bearer ${input:worktracker-token}" }
    }
  },
  "inputs": [{ "id": "worktracker-token", "type": "promptString", "password": true, "description": "WorkTracker MCP token" }]
}
```

Klienti, kteří umí jen stdio (třeba Claude Desktop přes `claude_desktop_config.json`), potřebují most, například `npx mcp-remote http://localhost:47821/mcp --header "Authorization: Bearer <token>"`. Před zdokumentováním vyzkoušej, protože parametry `mcp-remote` se mění.

## Testy

Nový projekt `tests/WorkTracker.Plugin.Mcp.Tests` po vzoru `WorkTracker.Plugin.StreamDeck.Tests` (Moq pro `IRemoteControlHost`, každý test s vlastním volným portem):

- plugin: jediný veřejný konstruktor, `InitializeAsync` server nespustí, `AttachAsync` ano, dvojí `AttachAsync` server neztratí, `ShutdownAsync` ho zastaví, bez tokenu se plugin neinicializuje,
- HTTP: 401, 403, 404, 405, 406, 400 a 202 na notifikaci (jako v ověření výše),
- nástroje přes `McpClient` + `HttpClientTransport`: výpis nástrojů, schéma volitelných parametrů, převod argumentů na volání hosta, `PluginResult.Failure` → `isError` s textem, `submit_worklogs` chybí bez přepínače,
- `RemoteControlHost` v `WorkTracker.Avalonia.Tests`: výpis záznamů v rozsahu, úprava zachová časy a smaže hodnotu předanou jako null, neexistující ID → `NotFound`, úprava vyvolá `WorkEntriesModified`, odeslání použije zvolený režim a providera, nepodporovaný režim → `Validation`.

Načtení přes izolovaný `AssemblyLoadContext` testy v jednom procesu neověří: testovací projekt referencuje plugin, takže MCP knihovna je ve výchozím kontextu a `PluginLoadContext` by ji vzal odtamtud. Jednou ručně ověř v GUI (Debug načítá plugin z `bin/Debug` podle `appsettings.Development.json`), na Windows i na macOS.

## Zbývá před sloučením

- [x] Rozšíření `IRemoteControlHost` a `RemoteControlHost` včetně lokalizace (cs, en).
- [x] Nástroje podle tabulky, `ServerInstructions`, přepínač odesílání.
- [x] Ošetření obsazeného portu (hlásí ho `RemoteControlCoordinator`).
- [x] Testy podle kapitoly výše.
- [ ] Ruční ověření v GUI s Claude Code na Windows a macOS.
- [x] `docs/plugins/mcp.md` (uživatelská dokumentace ve stylu `stream-deck.md`), odkaz z `README.md`, `README.cs.md`, `docs/plugin-development.md` a `docs/architecture.md`, soubor ve složce `/docs/plugins/` ve `WorkTracker.slnx`.
- [x] Plugin v matici `publish-plugins` v `.github/workflows/release.yml`.
- [x] Sekce Existing plugins v `CLAUDE.md`.

## Otevřené otázky

- Má plugin mít i výchozí providera a režim podle posledního odeslání z dialogu (`PersistSelection` v `SubmitWorklogViewModel`)? Zjednodušilo by to agentovi práci, ale je to proti pravidlu výslovné volby cíle.
- Mazání záznamů přes MCP: zatím ne. Pokud ano, tak s `destructiveHint` a vlastním přepínačem.
