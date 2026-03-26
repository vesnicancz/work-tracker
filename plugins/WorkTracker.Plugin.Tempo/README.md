# WorkTracker.Plugin.Tempo

Plugin pro nahrávání worklogů do Tempo Timesheets (Jira time tracking system).

## Funkce

- Automatické nahrávání worklogů do Tempo
- Podpora pro JIRA API v3
- Překlad issue key (např. "PROJ-123") na číselné issue ID
- Validace existujících worklogů
- Automatická detekce JIRA account ID

## Konfigurace

Plugin vyžaduje následující konfigurační parametry:

### Tempo API

- **TempoBaseUrl**: URL adresa Tempo API (výchozí: `https://api.tempo.io/core/3`)
- **TempoApiToken**: API token pro Tempo (získejte z Tempo Settings > API Integration)

### JIRA API

- **JiraBaseUrl**: URL adresa vaší JIRA instance (např. `https://your-domain.atlassian.net`)
- **JiraEmail**: Email pro JIRA autentizaci
- **JiraApiToken**: API token pro JIRA (získejte z Atlassian Account Settings > Security > API tokens)
- **JiraAccountId**: (volitelné) JIRA account ID - pokud není zadán, automaticky se zjistí

## Jak získat API tokeny

### Tempo API Token

1. Přihlaste se do Tempo
2. Přejděte do Settings > API Integration
3. Vytvořte nový API token
4. Zkopírujte token (zobrazí se pouze jednou)

### JIRA API Token

1. Přihlaste se do Atlassian Account: https://id.atlassian.com/manage-profile/security/api-tokens
2. Klikněte na "Create API token"
3. Zadejte název pro token
4. Zkopírujte vygenerovaný token

## Požadavky na Tempo API

Plugin používá oficiální Tempo API a vyžaduje:

- **issueId** (integer) - číselné ID issue z JIRA (NE issue key jako "PROJ-123")
- **authorAccountId** (string) - JIRA account ID uživatele
- **timeSpentSeconds** (integer) - čas strávený v sekundách
- **startDate** (string) - datum ve formátu "YYYY-MM-DD"
- **startTime** (string) - čas ve formátu "HH:mm:ss"
- **description** (string) - popis práce (volitelné)

## Jak to funguje

1. Plugin nejprve zavolá JIRA API pro překlad issue key → issue ID
2. Pokud není zadán account ID, automaticky ho získá z JIRA API
3. Vytvoří worklog request s požadovanými daty
4. Odešle worklog do Tempo API

## Vývoj

### Build

```bash
dotnet build
```

### Závislosti

- WorkTracker.Plugin.Abstractions
- Microsoft.Extensions.Logging
- System.Net.Http.Json

## Verze

**1.0.0** - První verze pluginu

- Základní funkce pro upload worklogů
- JIRA API integrace pro překlad issue keys
- Automatická detekce account ID
- Validace existujících worklogů
