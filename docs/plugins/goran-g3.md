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
| `EntraScopes` | `Text` | ✅ | — | API scope pro přístup na Goran G3 MCP, ve tvaru `api://{goran-api-client-id}/Mcp.Access`. Konkrétní hodnotu získáš od administrátora Goran G3. |

---

## Entra registrace

Postup je shodný s [Office 365 Calendar pluginem](office365-calendar.md#entra-registrace), ale scopes jsou jiné — místo Microsoft Graph používáš **API exposed by Goran G3** (delegated permissions na custom API v Entra). Získej konkrétní scopes od Goran G3 administrátora své organizace.

Pokud tvoje organizace má **centrální app registraci** pro Goran G3 (sdílenou aplikaci pro všechny interní klienty), stačí od IT dostat:

- `EntraClientId`
- `EntraTenantId`
- `EntraScopes` (pokud se liší od defaultu)

---

## Autentizace (device code flow)

Stejně jako Office 365 Calendar plugin. Po vyplnění konfigurace:

1. **Settings → Plugins → Goran G3 → Test connection**.
2. Plugin zavolá `ITokenProviderFactory.CreateAsync(tenantId, clientId, scopes)`.
3. Silent token cache se zkusí první; pokud cache prázdná / expirovaná, spustí se device code flow.
4. Progress dialog ukáže user code + URL `https://microsoft.com/devicelogin`.
5. Po úspěšném přihlášení plugin otestuje MCP connection.

Tokeny jsou v šifrované cache (`%LocalAppData%\WorkTracker\keys\`), obnova přes refresh token je automatická.

---

## MCP client

`GoranG3WorklogPlugin` používá `McpClient` z NuGet balíčku `ModelContextProtocol`, který:

1. Hostí HTTP spojení na `{GoranBaseUrl}/mcp`.
2. Všechny requesty procházejí `TokenInjectingHandler` — vlastní `DelegatingHandler`, který:
   - Před každým requestem zavolá `_tokenProvider.AcquireTokenSilentAsync`.
   - Pokud silent auth selže a token je `null`, handler vyhodí `InvalidOperationException` — **žádný interaktivní fallback uvnitř handleru neprobíhá**. Uživatel musí spustit **Test connection** v Settings, která interaktivní device code flow rozběhne explicitně.
   - Do `Authorization` hlavičky vloží `Bearer {token}`.
3. Volá MCP tools (snake_case název, argumenty v JSON objektu):
   - **`create_my_timesheet_item`** — upload jednoho záznamu.
   - **`submit_my_timesheet`** — finální submit sady záznamů (při `UploadWorklogsAsync`).
   - **`get_my_timesheet_items_list`** — query existujících worklogů (`GetWorklogsAsync`, `WorklogExistsAsync`).

### Struktura argumentů pro `create_my_timesheet_item`

Plugin předá nástroji tento dictionary (klíče **snake_case** podle MCP schématu Goran G3):

```json
{
  “project_code”: “000.GOR”,
  “project_phase_code”: “SP”,
  “date”: “2026-04-09”,
  “start_time”: “09:00”,
  “duration_minutes”: 60,
  “text”: “PROJ-123 Bug fix v autentizaci”,
  “tags”: [“dev”, “bugfix”],
  “external_id”: 123
}
```

**Mapování polí:**

- `text` — kombinace ticketu (pokud je) a popisu z `PluginWorklogEntry` (plugin používá interní `BuildText` helper).
- `external_id` — plugin se pokusí z `PluginWorklogEntry.TicketId` vyparsovat numerické ID (viz `ParseExternalId`). Pokud ticket nelze převést na číslo, pole se do argumentů nepřidá.
- `project_phase_code` — přidá se jen tehdy, když je pole `ProjectPhaseCode` v konfiguraci vyplněné.
- `tags` — přidá se jen tehdy, když je pole `Tags` vyplněné; rozparsuje se split po `,` + trim.

---

## Test connection — co testuje

1. **Získání tokenu** — plugin nejdřív zkusí `AcquireTokenSilentAsync`, a pokud je cache prázdná/expirovaná, přejde na `AcquireTokenInteractiveAsync` (device code flow). Průběh reportuje do `IProgress<string>`.
2. **Připojení k MCP** — naváže spojení na `{GoranBaseUrl}/mcp`.
3. **Ověření dostupnosti required toolu** — zavolá MCP `list_tools` a kontroluje, jestli server nabízí `create_my_timesheet_item`.
4. Reportuje stavy typu: „Získávám token…”, „Připojuji k MCP…”, „Ověřuji dostupné tools…”, „OK”.

> **Pozor:** `Test connection` aktuálně **neprovádí** validaci `ProjectCode` ani `ProjectPhaseCode`. Jejich správnost se projeví až při skutečném uploadu worklogu (kdy MCP server případně vrátí chybu).

---

## Jak plugin pracuje při uploadu

1. `UploadWorklogsAsync` přijme kolekci `PluginWorklogEntry`.
2. Pro každý záznam:
   - Sestaví argumenty pro MCP tool `create_my_timesheet_item` (snake_case pole viz výše).
   - Pošle přes `_mcpClient.CallToolAsync(“create_my_timesheet_item”, arguments, ct)`.
   - Pokud server vrátí success, inkrementuje `SuccessfulEntries`.
   - Pokud fail, přidá do `Errors` s `ErrorMessage` a `Worklog`.
3. Po zpracování všech záznamů plugin zavolá `submit_my_timesheet` pro finální uložení celé sady.
4. Vrátí `PluginResult<WorklogSubmissionResult>`.

**Retry** — plugin **neretryne** automaticky. Pokud dojde k síťové chybě, záznam skončí v `Errors` a uživatel může kliknout **Retry failed** v dialogu.

Důvod: Goran G3 MCP zatím nemá idempotency token, takže retry by mohl vytvořit duplicitní záznamy. Jakmile bude idempotency, retry logika se doplní.

---

## `WorklogExistsAsync` a `GetWorklogsAsync`

Plugin podporuje i čtení existujících worklogů (přes `worklog.search` MCP call) — to aplikaci dovoluje zkontrolovat před uploadem, jestli už záznam existuje (a tedy neposílat duplicitu).

`GetWorklogsAsync(startDate, endDate)`:

1. Pošle `worklog.search` s filtrem `{ from: startDate, to: endDate, user: "me" }`.
2. Zmapuje výsledky zpět na `PluginWorklogEntry`.

`WorklogExistsAsync(worklog)`:

1. Volá `GetWorklogsAsync(worklog.StartTime.Date, worklog.StartTime.Date)`.
2. Hledá záznam se stejným `ticketId` a přibližným `startTime` (±1 min).
3. Vrací `true` / `false`.

---

## Časté chyby

| Symptom | Příčina | Řešení |
|---------|---------|--------|
| `Unable to connect to MCP endpoint` | Neplatná `GoranBaseUrl` nebo výpadek serveru | Zkontroluj URL a dostupnost serveru |
| `401 Unauthorized` i po přihlášení | Token má špatné scopes | Zkontroluj `EntraScopes` s IT |
| `Project code 000.GOR not found` | Projekt neexistuje, nebo nemáš k němu přístup | Kontaktuj project managera Goran G3 |
| Device code flow timeoutuje | Uživatel nepotvrdil login do cca 15 minut | Klikni **Test connection** znovu |
| `Duplicate worklog detected` | Retry po úspěšném uploadu — server vrací konflikt | Nech aktuální stav, worklog je uložený |

---

## Odhlášení / reset

Stejné jako [Office 365 Calendar](office365-calendar.md#odhlášení--reset-tokenů): smaž soubory v `%LocalAppData%\WorkTracker\keys\` pro reset MSAL cache.

---

## Fallback: appsettings.json pro CLI

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

Stejně jako u ostatních MSAL pluginů: device code flow v CLI funguje, ale kód a URL se vypíší do terminálu jako stdout.
