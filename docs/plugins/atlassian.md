# Plugin: Atlassian (Tempo + Jira)

Balíček `WorkTracker.Plugin.Atlassian` obsahuje **dva pluginy** sdílející společný `JiraClient`:

| Plugin ID | Třída | Rozhraní | Účel |
|-----------|-------|----------|------|
| `tempo.worklog` | `TempoWorklogPlugin` | `IWorklogUploadPlugin` | Upload worklogů do Tempo Timesheets |
| `jira.suggestions` | `JiraSuggestionsPlugin` | `IWorkSuggestionPlugin` | Work suggestions z Jira issues (JQL) |

Oba pluginy se instalují jako jeden `.dll` soubor, ale jsou nezávisle enable/disable v Settings. Každý má vlastní sadu konfiguračních polí.

---

## Tempo Worklog (`tempo.worklog`)

### Konfigurační pole

| Pole | Typ | Povinné | Default | Popis |
|------|-----|---------|---------|-------|
| `TempoBaseUrl` | `Url` | ✅ | `https://api.eu.tempo.io/4` | Base URL Tempo REST API. Pro US klienty použij `https://api.tempo.io/4`. |
| `TempoApiToken` | `Password` | ✅ | — | API token z Tempo. Uložený v secure storage. |
| `JiraAccountId` | `Text` | ✅ | — | Jira account ID (24‑znakové GUID). Uživatele identifikuje pro Tempo. |

### Získání tokenu a account ID

**Tempo API Token:**

1. V Jira/Tempo otevři **Settings → Apps → Tempo → API Integration**.
2. Klikni **New Token**, pojmenuj ho např. „WorkTracker“.
3. Zkopíruj vygenerovaný token (zobrazí se jen jednou!) a vlož do pole `TempoApiToken`.

**Jira Account ID:**

1. V Jira otevři svůj profil (ikona vpravo nahoře → **Profile**).
2. V URL je tvůj account ID: `https://vase-firma.atlassian.net/jira/people/{accountId}`.
3. Zkopíruj 24‑znakový řetězec a vlož do pole `JiraAccountId`.

Alternativně API: `GET https://vase-firma.atlassian.net/rest/api/3/myself` s Basic auth (email + Jira API token) vrátí `accountId` v JSON response.

### Jak plugin pracuje

Při volání `UploadWorklogAsync(entry)`:

1. **Překlad Jira issue key → issue ID** — Tempo vyžaduje numerické ID, ne klíč `PROJ-123`. Plugin volá Jira REST API `GET /rest/api/3/issue/{key}` a extrahuje `id` z response.
2. **Cache** — výsledek se ukládá do `ConcurrentDictionary<string, (int Id, DateTime Expiry)>` s TTL **1 hodina**. Opakované submity stejného ticketu během hodiny nepingají Jira.
3. **POST worklog** — `POST /worklogs` s payloadem:
   ```json
   {
     "authorAccountId": "…",
     "issueId": 12345,
     "timeSpentSeconds": 3600,
     "startDate": "2026-04-09",
     "startTime": "09:00:00",
     "description": "Bug fix v autentizaci"
   }
   ```
4. **Retry** — na 408/429/500–504 až 2× s exponenciálním backoffem (~500 ms, 2 s). Ostatní chyby (401, 403, 404, 400) propagují okamžitě s odpovídající `PluginErrorCategory`.

### Test connection

Stiskem **Test connection** v Settings plugin:

1. Pingne Tempo `GET /worklogs?accountId={JiraAccountId}&limit=1` s tokenem.
2. Pokud odpoví 200, pingne Jira `GET /rest/api/3/myself` (pro ověření, že issue key lookup bude fungovat).
3. Reportuje do `IProgress<string>` stavy „Checking Tempo…“, „Checking Jira…“, „OK“.

### Časté chyby

| Symptom | Příčina | Řešení |
|---------|---------|--------|
| `401 Unauthorized` z Tempa | Neplatný token | Vygeneruj nový |
| `403 Forbidden` | Token nemá scope `worklog_write` | Při generování tokenu vyber všechny scopes |
| `404 Not Found` při uploadu | Ticket key neexistuje / překlep | Zkontroluj ticket v Jiře |
| `400 Bad Request` — `Invalid startDate` | Timezone konflikt (server vs. klient) | Plugin formátuje datum v local time — pokud Tempo instance má nastavený UTC, občas dochází k +1 den. Issue známý, workaround v roadmapě. |

---

## Jira Suggestions (`jira.suggestions`)

### Konfigurační pole

| Pole | Typ | Povinné | Default | Popis |
|------|-----|---------|---------|-------|
| `BaseUrl` | `Url` | ✅ | — | Jira base URL, např. `https://vase-firma.atlassian.net` |
| `Email` | `Email` | ✅ | — | Login email pro Basic auth |
| `ApiToken` | `Password` | ✅ | — | Jira API token (viz níže) |
| `JqlFilter` | `MultilineText` | ❌ | `assignee = currentUser() AND status != Done ORDER BY updated DESC` | JQL pro list úkolů v Suggestions dialogu |
| `SearchJqlFilter` | `MultilineText` | ❌ | — | JQL pro search pole; použij `{query}` jako placeholder |
| `MaxResults` | `Number` | ❌ | `20` | Max počet výsledků |

### Získání API tokenu

1. Jdi na <https://id.atlassian.com/manage-profile/security/api-tokens>.
2. **Create API token** → pojmenuj → zkopíruj.
3. Vlož do `ApiToken` (uloží se do secure storage).

Autentizace je **Basic** = `base64(email:token)` v `Authorization` hlavičce. Jira to tak vyžaduje i pro API token; nepleť si to s OAuth.

### Použití JQL

**Výchozí filter** vrátí tvé otevřené tickety řazené podle poslední úpravy. Uprav podle potřeby. Příklady:

```jql
# Moje tickety v aktuálním sprintu
assignee = currentUser() AND sprint in openSprints() ORDER BY priority DESC

# Pouze bugy s vysokou prioritou
assignee = currentUser() AND issuetype = Bug AND priority >= High

# Tickety, ke kterým jsem dneska přispěl
assignee = currentUser() AND updated >= startOfDay()
```

**Search JQL** se použije, když v Suggestions dialogu napíšeš do search pole text. Aplikace nahradí `{query}` za escapovaný text. Příklad:

```jql
text ~ "{query}" AND project in (PROJ, WORK) ORDER BY updated DESC
```

Pokud `SearchJqlFilter` nenastavíš, search je pro tento plugin zakázaný (dialog ukáže „Search není podporovaný“).

### Jak se vrácené issues mapují na `WorkSuggestion`

| `WorkSuggestion` | Zdroj z Jira |
|------------------|--------------|
| `Title` | `fields.summary` |
| `TicketId` | `key` (např. `PROJ-123`) |
| `Description` | `fields.description` (plaintext extract) |
| `Source` | `"Jira"` |
| `SourceId` | `key` |
| `SourceUrl` | `{BaseUrl}/browse/{key}` |

### Test connection

1. Volá `GET /rest/api/3/myself`.
2. Pokud jsou vyplněné JQL, zkusí i `GET /rest/api/3/search?jql=…&maxResults=1`, aby zjistil, že JQL je validní.

---

## Fallback: appsettings.json pro CLI

CLI nemá Settings UI, takže pro něj můžeš plugin konfiguraci vložit do `appsettings.json` (sekce `Plugins`):

```json
{
  "Plugins": {
    "tempo.worklog": {
      "TempoBaseUrl": "https://api.eu.tempo.io/4",
      "TempoApiToken": "vas-tempo-token",
      "JiraAccountId": "5b10a2844c20165700ede21g"
    },
    "jira.suggestions": {
      "BaseUrl": "https://vase-firma.atlassian.net",
      "Email": "vas@email.cz",
      "ApiToken": "vas-jira-api-token",
      "JqlFilter": "assignee = currentUser() AND status != Done",
      "MaxResults": "20"
    }
  }
}
```

> **Pro vývoj použij User Secrets**, ne `appsettings.json`:
> ```bash
> cd src/WorkTracker.CLI
> dotnet user-secrets init
> dotnet user-secrets set "Plugins:tempo.worklog:TempoApiToken" "vas-token"
> ```

Aplikace při startu načte hodnoty z configu jako **výchozí** pro pluginy, ale uživatelské nastavení v `settings.json` (pokud existuje) má přednost.
