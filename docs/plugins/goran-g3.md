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
| `EntraScopes` | `Text` | ❌ | (viz níže) | Scopes pro token, čárkami oddělené |

Výchozí scopes (pokud `EntraScopes` zůstane prázdné) jsou specifické pro Goran G3 API — získáš je od administrátora Goran G3.

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

`GoranG3WorklogPlugin` obsahuje vlastní `McpClient`, který:

1. Hostí HTTP spojení na `GoranBaseUrl/mcp` (konkrétní endpoint je specifikovaný MCP protokolem).
2. Všechny requesty procházejí `TokenInjectingHandler` — vlastní `DelegatingHandler`, který:
   - Před každým requestem zavolá `_tokenProvider.AcquireTokenSilentAsync`.
   - Pokud token chybí nebo selže, fallback na `AcquireTokenInteractiveAsync` (spustí device code flow).
   - Do `Authorization` hlavičky vloží `Bearer {token}`.
3. Serializuje MCP zprávy podle protokolu (JSON‑RPC 2.0 over HTTP).
4. Volá dvě hlavní operace:
   - `worklog.create` — upload jednoho záznamu
   - `worklog.search` — query existujících worklogů (pro `GetWorklogsAsync`, `WorklogExistsAsync`)

### Struktura worklog payloadu

```json
{
  "projectCode": "000.GOR",
  "projectPhaseCode": null,
  "date": "2026-04-09",
  "startTime": "09:00",
  "durationMinutes": 60,
  "description": "Bug fix v autentizaci",
  "ticketId": "PROJ-123",
  "tags": ["dev", "bugfix"]
}
```

`TicketId` z `PluginWorklogEntry` se mapuje na `ticketId` pole. Tagy se parsují z konfiguračního pole `Tags` (split po `,` a trim).

---

## Test connection — co testuje

1. **Získání tokenu** — silent nebo device code flow. Reportuje do `IProgress<string>`.
2. **MCP handshake** — pošle `initialize` zprávu na `{GoranBaseUrl}/mcp` a ověří, že server odpoví validním MCP response.
3. **Project lookup** — zavolá `project.get { code: "{ProjectCode}" }` pro ověření, že kód projektu existuje a máš k němu přístup.
4. **Pokud je nastavený `ProjectPhaseCode`**, ověří i ten.
5. Reportuje stavy: „Získávám token…", „Připojuji k MCP…", „Ověřuji projekt {ProjectCode}…", „OK".

---

## Jak plugin pracuje při uploadu

1. `UploadWorklogsAsync` přijme kolekci `PluginWorklogEntry`.
2. Pro každý záznam:
   - Sestaví MCP request z konfigurace + entry + tagů.
   - Pošle přes `McpClient.CallAsync("worklog.create", payload)`.
   - Pokud server vrátí success, inkrementuje `SuccessfulEntries`.
   - Pokud fail, přidá do `Errors` s `ErrorMessage` a `Worklog`.
3. Vrátí `PluginResult<WorklogSubmissionResult>`.

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
      "Tags": "dev"
    }
  }
}
```

Stejně jako u ostatních MSAL pluginů: device code flow v CLI funguje, ale kód a URL se vypíší do terminálu jako stdout.
