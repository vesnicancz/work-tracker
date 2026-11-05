# Plugin: Goran G3

| Plugin ID | `gorang3.worklog` |
|-----------|-------------------|
| Třída | `GoranG3WorklogPlugin` |
| Rozhraní | `IWorklogUploadPlugin` |
| Autentizace | Entra ID (MSAL device code flow) |
| Protokol | MCP (Model Context Protocol) |

Plugin odesílá worklogy do interního systému **Goran G3** přes jeho MCP server. Cílová skupina: zaměstnanci organizací používajících Goran G3 jako výkaznictví.

---

## Konfigurační pole

| Pole | Typ | Povinné | Default | Popis |
|------|-----|---------|---------|-------|
| `GoranBaseUrl` | `Url` | ✅ | — | Base URL Goran G3 MCP serveru, např. `https://moonfish-g3.goran.cz` |
| `ProjectCode` | `Text` | ✅ | — | Kód projektu, na který se worklog účtuje (např. `000.GOR`) |
| `ProjectPhaseCode` | `Text` | ❌ | — | Fáze projektu (volitelné — některé projekty fáze nemají) |
| `Tags` | `Text` | ❌ | — | Čárkami oddělený seznam tagů |
| `EntraClientId` | `Text` | ✅ | — | Client ID aplikace registrované v Entra |
| `EntraTenantId` | `Text` | ✅ | — | Tenant ID |
| `EntraScopes` | `Text` | ✅ | — | API scope pro přístup na Goran G3 MCP, ve tvaru `api://{goran-api-client-id}/Mcp.Access`; více scopes odděl čárkou. Konkrétní hodnotu získáš od administrátora Goran G3. |

---

## Entra registrace

Postup vychází z [Office 365 Calendar pluginu](office365-calendar.md#entra-registrace). Rozdíly: kvůli device code flow musí být zapnuté **Allow public client flows** a scopes jsou jiné — místo Microsoft Graph používáš **API exposed by Goran G3** (delegated permissions na custom API v Entra). Získej konkrétní scopes od Goran G3 administrátora své organizace.

Pokud tvoje organizace má **centrální app registraci** pro Goran G3 (sdílenou aplikaci pro všechny interní klienty), stačí od IT dostat:

- `EntraClientId`
- `EntraTenantId`
- `EntraScopes` (povinné, žádný default neexistuje)

---

## Autentizace (device code flow)

Goran G3 používá device code flow (Office 365 Calendar se přihlašuje v prohlížeči). Po vyplnění konfigurace:

1. Plugin při inicializaci zavolá `ITokenProviderFactory.CreateAsync(tenantId, clientId, scopes)` a zkusí silent token; když ho má, rovnou se připojí k MCP serveru.
2. **Nastavení → Pluginy → Goran G3 Timesheets → Otestovat připojení**. Pokud je plugin už připojený, autentizace se přeskočí.
3. Jinak se zkusí silent token cache; pokud je prázdná / expirovaná, spustí se device code flow.
4. Pod tlačítkem se objeví kód a URL z MSAL (`⏳ Open browser and enter code: …` a na dalším řádku URL) a aplikace tu URL zároveň otevře v prohlížeči.
5. Po úspěšném přihlášení plugin otestuje MCP connection.

Tokeny jsou v šifrované cache (v release buildu typicky `%LocalAppData%\WorkTracker\keys\`), obnova přes refresh token je automatická. V non‑Production prostředích používá `WorkTrackerPaths` suffix podle `DOTNET_ENVIRONMENT`, takže při `dotnet run` může být reálná cesta `%LocalAppData%\WorkTracker_Development\keys\`. Při ručním mazání cache (reset) smaž složku odpovídající aktuálnímu prostředí.

---

## MCP client

`GoranG3WorklogPlugin` používá `McpClient` z NuGet balíčku `ModelContextProtocol`, který:

1. Hostí HTTP spojení na `{GoranBaseUrl}/mcp`.
2. Všechny requesty procházejí `TokenInjectingHandler` — vlastní `DelegatingHandler`, který:
   - Před každým requestem zavolá `_tokenProvider.AcquireTokenSilentAsync`.
   - Pokud silent auth selže a token je `null`, handler vyhodí `InvalidOperationException` — **žádný interaktivní fallback uvnitř handleru neprobíhá**. Uživatel musí spustit **Otestovat připojení** v Nastavení, které interaktivní device code flow rozběhne explicitně.
   - Do `Authorization` hlavičky vloží `Bearer {token}`.
3. Volá MCP tools (snake_case název, argumenty v JSON objektu):
   - **`create_my_timesheet_item`** — upload jednoho záznamu.
   - **`submit_my_timesheet`** — finální submit timesheetu za jeden den (při `UploadWorklogsAsync`, argument `date`).

### Struktura argumentů pro `create_my_timesheet_item`

Plugin předá nástroji tento dictionary (klíče **snake_case** podle MCP schématu Goran G3):

```json
{
  "project_code": "000.GOR",
  "project_phase_code": "SP",
  "date": "2026-04-09",
  "start_time": "09:00",
  "duration_minutes": 60,
  "text": "PROJ-123 - Bug fix v autentizaci",
  "tags": ["dev", "bugfix"],
  "external_id": 123
}
```

**Mapování polí:**

- `text` — ticket (pokud je) a popis z `PluginWorklogEntry` spojené oddělovačem ` - ` (interní `BuildText` helper). Pokud je výsledek prázdný, `text` se nepošle.
- `external_id` — plugin se pokusí z `PluginWorklogEntry.TicketId` vyparsovat numerické ID (viz `ParseExternalId`). Pokud ticket nelze převést na číslo, pole se do argumentů nepřidá.
- `project_phase_code` — přidá se jen tehdy, když je pole `ProjectPhaseCode` v konfiguraci vyplněné.
- `tags` — přidá se jen tehdy, když je pole `Tags` vyplněné; rozparsuje se split po `,` + trim.

---

## Test connection — co testuje

Pokud je plugin už připojený k MCP serveru, kroky 1 a 2 se přeskočí.

1. **Získání tokenu** — plugin nejdřív zkusí `AcquireTokenSilentAsync`, a pokud je cache prázdná/expirovaná, přejde na `AcquireTokenInteractiveAsync` (device code flow).
2. **Připojení k MCP** — naváže spojení na `{GoranBaseUrl}/mcp` (timeout 30 s).
3. **Ověření dostupnosti required toolu** — zavolá `ListToolsAsync` (MCP `tools/list`) a kontroluje, jestli server nabízí `create_my_timesheet_item`. Pokud ne, test skončí chybou `MCP server does not expose expected timesheet tools`.
4. Do `IProgress<string>` reportuje jen `Authenticating...`, `Connecting to MCP server...` a případně výzvu device code flow (`⏳ Open browser and enter code: …`). Úspěch je samotný výsledek testu.

> **Pozor:** `Test connection` aktuálně **neprovádí** validaci `ProjectCode` ani `ProjectPhaseCode`. Jejich správnost se projeví až při skutečném uploadu worklogu (kdy MCP server případně vrátí chybu).

---

## Jak plugin pracuje při uploadu

1. `UploadWorklogsAsync` přijme kolekci `PluginWorklogEntry` a `WorklogSubmissionMode`.
2. Pro každý záznam:
   - Sestaví argumenty pro MCP tool `create_my_timesheet_item` (snake_case pole viz výše).
   - Pošle přes `_mcpClient.CallToolAsync("create_my_timesheet_item", arguments, ct)`.
   - Pokud server vrátí success, inkrementuje `SuccessfulEntries`.
   - Pokud fail, přidá do `Errors` s `ErrorMessage` a `WorklogIndex`, pozicí záznamu v předané kolekci.
3. Záznamy se zpracovávají seskupené po dnech: po uploadu záznamů jednoho dne plugin zavolá `submit_my_timesheet` s argumentem `date` pro daný den. Každý den se posuzuje samostatně. Pokud některé jeho záznamy selhaly, submit se přeskočí a den se uzavře, až po opravě projde tlačítko **ODESLAT JEN NEÚSPĚŠNÉ**; ostatních dnů se to netýká. Selhání submitu se přidá do `Errors` jako `Timesheet submit failed for yyyy-MM-dd: …`. Plugin si do uzavření dne pamatuje už nahrané záznamy, takže opakování po selhaném submitu den jen uzavře a záznam nevytvoří podruhé. Tahle paměť je jen v běžící instanci pluginu a po uzavření dne, změně konfigurace nebo restartu aplikace zaniká.
4. Vrátí `PluginResult<WorklogSubmissionResult>`.

### Submission módy

Plugin inzeruje pouze `SupportedModes = Timed` (výchozí z `WorklogUploadPluginBase`). Goran G3 MCP vyžaduje pro každý záznam volání `create_my_timesheet_item` s reálným `start_time` a pak finální `submit_my_timesheet` — tato sekvence neumožňuje agregaci více záznamů do jednoho per kód+popis. Když v submission dialogu vybereš GoranG3, volba **Agregovaný** je neaktivní a mód se přepne na **Časový**.

**Připojení** — když spojení chybí (server byl při startu nedostupný, vypršelo přihlášení nebo se spojení při předchozím volání rozbilo), plugin se před další operací zkusí tiše připojit znovu, takže restart aplikace není potřeba. Interaktivní přihlášení pořád spouští jen **Otestovat připojení**. Změna konfigurace spustí novou instanci pluginu a staré spojení skončí s tou původní, takže plugin nezůstane na starém serveru. Kam se plugin připojuje, ukáže **Otestovat připojení** během připojování a každé navázané spojení zapíše do logu aplikace (`Connected to Goran G3 MCP server at …`).

**Retry** — plugin **neretryne** automaticky. Pokud dojde k síťové chybě, záznam skončí v `Errors` a uživatel může kliknout **ODESLAT JEN NEÚSPĚŠNÉ** v dialogu.

Důvod: Goran G3 MCP zatím nemá idempotency token, takže retry by mohl vytvořit duplicitní záznamy. Jakmile bude idempotency, retry logika se doplní.

---

## Časté chyby

| Symptom | Příčina | Řešení |
|---------|---------|--------|
| `Connection failed: …` při testu připojení, případně `MCP server at {url}/mcp did not respond within 30 seconds` | Neplatná `GoranBaseUrl` nebo výpadek serveru | Zkontroluj URL a dostupnost serveru |
| `401 Unauthorized` i po přihlášení | Token má špatné scopes | Zkontroluj `EntraScopes` s IT |
| `Upload failed: …` s chybou o projektu (konkrétní text vrací server Goran G3) | Projekt neexistuje, nebo nemáš k němu přístup | Kontaktuj project managera Goran G3 |
| Device code flow timeoutuje | Uživatel nepotvrdil login do cca 15 minut | Klikni **Otestovat připojení** znovu |

---

## Odhlášení / reset

Stejné jako [Office 365 Calendar](office365-calendar.md#odhlášení--reset-tokenů): smaž `msal_*.bin` ve složce `keys` aktuálního prostředí (`%LocalAppData%\WorkTracker\keys\`, resp. `WorkTracker_Development\keys\`); na macOS a Linuxu i položku v Keychainu / keyringu.

---

## Plugin config schéma (`appsettings.json`)

> **Pozor — dvě klíčová omezení:**
>
> 1. **Samotné `appsettings.json` plugin nezapne.** GUI i `WorkTracker.CLI` přebírají enabled stav i konfiguraci z uživatelského `settings.json`, který zapisuje GUI; `appsettings.json` slouží jen jako záložní konfigurace a enabled stav nenastaví. Plugin povol a nastav v GUI (**Nastavení → Pluginy → Goran G3 Timesheets**).
> 2. **GoranG3 vyžaduje předchozí GUI login.** `TokenInjectingHandler` v pluginu používá **pouze** `AcquireTokenSilentAsync` — při uploadu se nikdy neotevře device code flow. Musíš nejdřív spustit **Otestovat připojení** v Nastavení GUI, které interaktivně získá token a uloží ho do cache. Teprve pak bude plugin fungovat i v CLI.
>
> Schéma níže ukazuje tvar záložní konfigurace.

```json
{
  "Plugins": {
    "gorang3.worklog": {
      "GoranBaseUrl": "https://moonfish-g3.goran.cz",
      "ProjectCode": "000.GOR",
      "EntraClientId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
      "EntraTenantId": "yyyyyyyy-yyyy-yyyy-yyyy-yyyyyyyyyyyy",
      "EntraScopes": "api://{goran-api-client-id}/Mcp.Access",
      "Tags": "dev"
    }
  }
}
```

V `WorkTracker.CLI` plugin funguje (`send`), pokud byl povolen v GUI a token je v cache. Interaktivní device code flow ale CLI nespouští — po vypršení tokenu je potřeba znovu **Otestovat připojení** v GUI.
