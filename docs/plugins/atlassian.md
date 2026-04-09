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
| `JiraBaseUrl` | `Url` | ✅ | — | Jira base URL (např. `https://vase-firma.atlassian.net`). Plugin ho používá pro překlad issue key → numerické issue ID, které Tempo vyžaduje. |
| `JiraEmail` | `Email` | ✅ | — | Login email pro Jira Basic auth. |
| `JiraApiToken` | `Password` | ✅ | — | Jira API token pro Basic auth. Uložený v secure storage. |
| `JiraAccountId` | `Text` | ❌ | (auto‑detekce) | Atlassian accountId (typicky 24znakový opaque řetězec, **ne GUID**). Když necháš prázdné, plugin si ho při inicializaci sám zjistí přes `GET /rest/api/3/myself`. |

### Získání tokenů

**Tempo API Token:**

1. V Jira/Tempo otevři **Settings → Apps → Tempo → API Integration**.
2. Klikni **New Token**, pojmenuj ho např. „WorkTracker".
3. Zkopíruj vygenerovaný token (zobrazí se jen jednou!) a vlož do pole `TempoApiToken`.

**Jira API Token:**

1. Otevři <https://id.atlassian.com/manage-profile/security/api-tokens>.
2. **Create API token** → pojmenuj → zkopíruj.
3. Vlož do pole `JiraApiToken`.

**Jira Account ID** (volitelné):

Plugin si ho umí zjistit sám — stačí nechat pole prázdné. Pokud ho chceš zadat ručně (například pro troubleshooting nebo aby init byl o jedno HTTP volání rychlejší):

1. V Jira otevři svůj profil (ikona vpravo nahoře → **Profile**).
2. V URL je tvůj account ID: `https://vase-firma.atlassian.net/jira/people/{accountId}`.
3. Zkopíruj 24‑znakový řetězec a vlož do pole `JiraAccountId`.

### Jak plugin pracuje

Při volání `UploadWorklogAsync(entry)`:

1. **Překlad Jira issue key → issue ID** — Tempo vyžaduje numerické ID, ne klíč `PROJ-123`. Plugin volá Jira REST API `GET /rest/api/3/issue/{key}` a extrahuje `id` z response.
2. **Cache** — výsledek se ukládá do `ConcurrentDictionary<string, (int Id, DateTime CachedAt)>` s TTL **1 hodina**. Platnost se vyhodnocuje jako `UtcNow - CachedAt < CacheTtl`. Opakované submity stejného ticketu během hodiny nepingají Jira.
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
4. **Retry** — na 408/429/500–504 až 2× s exponenciálním backoffem. První retry čeká ~2 s, druhý ~4 s (formule `2^(attempt+1)` sekund). Ostatní HTTP chyby (401, 403, 404, 400…) se neretryují a propagují se okamžitě. **Pozor:** aktuální `TempoWorklogPlugin.UploadWorklogAsync` kategorizuje všechny neúspěšné HTTP statusy obecně jako `PluginErrorCategory.Network`, ne podle jednotlivých status code (401 tedy nedostane kategorii `Authentication`, jak by leckoho napadlo).

### Test connection

Stiskem **Test connection** v Settings plugin aktuálně:

1. Zavolá Jira přes interní `_jiraClient.TestConnectionAsync` (pingne `GET /rest/api/3/myself` s Basic auth).
2. Pokud Jira odpoví úspěšně, plugin vrátí success; při 401/403 kategorizuje chybu jako `Authentication`, jinak jako `Network`.

> **Pozor:** Tempo endpoint **ani platnost `TempoApiToken`** se tímto testem neověřuje. Tempo autorizace se projeví až při skutečném volání (upload worklogu). Pokud tedy máš špatný Tempo token, ale Jira funguje, Test connection projde a chyba se objeví až při `Odeslat záznamy práce`.

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

Plugin sdílí konfigurační pole Jira s Tempo plugin (`JiraConfigFields` v kódu), takže klíče začínají prefixem `Jira`:

| Pole | Typ | Povinné | Default | Popis |
|------|-----|---------|---------|-------|
| `JiraBaseUrl` | `Url` | ✅ | — | Jira base URL, např. `https://vase-firma.atlassian.net` |
| `JiraEmail` | `Email` | ✅ | — | Login email pro Basic auth |
| `JiraApiToken` | `Password` | ✅ | — | Jira API token (viz níže) |
| `JqlFilter` | `MultilineText` | ❌ | `assignee = currentUser() AND status != Done ORDER BY updated DESC` | JQL pro list úkolů v Suggestions dialogu |
| `SearchJqlFilter` | `MultilineText` | ❌ | — | JQL pro search pole; použij `{query}` jako placeholder |
| `MaxResults` | `Number` | ❌ | `20` | Max počet výsledků |

### Získání API tokenu

1. Jdi na <https://id.atlassian.com/manage-profile/security/api-tokens>.
2. **Create API token** → pojmenuj → zkopíruj.
3. Vlož do `JiraApiToken` (uloží se do secure storage).

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

Plugin fetchuje issues přes `GET /rest/api/3/search/jql?jql=…&maxResults=…&fields=summary,status,issuetype,priority`. Z výsledku ale při mapování na `WorkSuggestion` používá jen `key` a `summary`:

| `WorkSuggestion` | Zdroj z Jira |
|------------------|--------------|
| `Title` | `fields.summary` |
| `TicketId` | `key` (např. `PROJ-123`) |
| `Description` | — (plugin `Description` nenastavuje) |
| `Source` | `"Jira"` |
| `SourceId` | `key` |
| `SourceUrl` | `{BaseUrl}/browse/{key}` |

### Test connection

Stejně jako Tempo plugin volá **jen** `_jiraClient.TestConnectionAsync` (pingne `GET /rest/api/3/myself` s Basic auth). **JQL validace při testu neprobíhá** — pokud máš syntakticky nevalidní `JqlFilter`, test projde, ale Suggestions dialog vrátí chybu až při pokusu o načtení návrhů.

---

## Plugin config schéma (`appsettings.json`)

> **Pozor:** Tohle je **referenční schéma** struktury plugin configu, jakou přijímá metoda `InitializePluginsAsync`. **Není to funkční CLI fallback** — aktuální `WorkTracker.CLI` volá `InitializePluginsAsync` bez enabled‑plugin mapy, takže i když hodnoty vložíš do `appsettings.json`, pluginy zůstanou vypnuté a `send` skončí s „No worklog upload plugin available". Primárně konfiguruj Atlassian plugin v GUI (**Nastavení → Pluginy**). Schéma níže je užitečné, pokud si píšeš vlastního hosta nad `WorkTracker.Infrastructure` a voláš `InitializePluginsAsync(host.Services, configuration, enabledPlugins: {...})` sám.

```json
{
  "Plugins": {
    "tempo.worklog": {
      "TempoBaseUrl": "https://api.eu.tempo.io/4",
      "TempoApiToken": "vas-tempo-token",
      "JiraBaseUrl": "https://vase-firma.atlassian.net",
      "JiraEmail": "vas@email.cz",
      "JiraApiToken": "vas-jira-api-token",
      "JiraAccountId": "5b10a2844c20165700ede21g"
    },
    "jira.suggestions": {
      "JiraBaseUrl": "https://vase-firma.atlassian.net",
      "JiraEmail": "vas@email.cz",
      "JiraApiToken": "vas-jira-api-token",
      "JqlFilter": "assignee = currentUser() AND status != Done",
      "MaxResults": "20"
    }
  }
}
```

> **Nikdy necommituj API tokeny** do Gitu. Pokud testuješ s reálnými hodnotami mimo GUI, použij [User Secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets):
> ```bash
> cd src/WorkTracker.CLI
> dotnet user-secrets init
> dotnet user-secrets set "Plugins:tempo.worklog:TempoApiToken" "vas-token"
> ```

Aplikace při startu načte hodnoty z configu jako **výchozí** pro pluginy, ale uživatelské nastavení v `settings.json` (pokud existuje) má přednost.
