# WorkTracker - Uživatelská Příručka

**Kompletní průvodce používáním WorkTracker aplikace**

Verze: 1.1
Datum: Březen 2026

---

## Obsah

1. [Úvod](#1-úvod)
2. [Instalace a První Spuštění](#2-instalace-a-první-spuštění)
3. [CLI Rozhraní](#3-cli-rozhraní)
4. [WPF Aplikace](#4-wpf-aplikace)
5. [Workflow a Best Practices](#5-workflow-a-best-practices)
6. [Integrace s Tempo/Jira](#6-integrace-s-tempojira)
7. [Často Kladené Otázky](#7-často-kladené-otázky)
8. [Troubleshooting](#8-troubleshooting)

---

## 1. Úvod

### 1.1 Co je WorkTracker?

WorkTracker je aplikace pro **sledování pracovní doby** určená pro vývojáře a týmy, kteří potřebují:
- 📊 Přesně zaznamenávat čas strávený na úkolech
- 🎫 Propojit práci s Jira tickety
- 📤 Automaticky odesílat worklogs do Tempo
- 📈 Sledovat svou produktivitu
- ⚡ Rychle a jednoduše spravovat své pracovní záznamy

### 1.2 Klíčové Vlastnosti

| Funkce | Popis |
|--------|-------|
| **Time Tracking** | Start/stop sledování s automatickým ukončením předchozí práce |
| **Jira Integration** | Automatická detekce Jira ticket ID (např. PROJ-123) |
| **Tempo Export** | Jedním příkazem odeslat worklogs do Tempo |
| **Triple Interface** | CLI pro rychlé operace, WPF pro Windows GUI, Avalonia pro cross-platform GUI |
| **Favorite Templates** | Oblíbené pracovní položky jako šablony pro rychlé spuštění |
| **Go to Today** | Rychlá navigace na dnešní datum |
| **Failed Worklog Resubmission** | Opakované odeslání neúspěšných worklogů |
| **Validation** | Detekce překrývajících se časů, kontrola validních dat |
| **Offline First** | Práce bez připojení, sync když je potřeba |

### 1.3 Požadavky

- **Operační systém**: Windows 10/11 (pro WPF), Windows/Linux/macOS (pro Avalonia), Linux/macOS (pouze CLI)
- **.NET Runtime**: 10.0 nebo vyšší
- **Disk**: ~50 MB pro aplikaci + databázi
- **RAM**: Minimum 512 MB
- **Jira/Tempo účet**: Pro export funkcionalitu (volitelné)

---

## 2. Instalace a První Spuštění

### 2.1 Instalace

#### Varianta A: Stažení Release

1. Stáhněte nejnovější release z [GitHub Releases](https://github.com/yourusername/WorkTracker/releases)
2. Rozbalte ZIP soubor
3. Spusťte:
   - **CLI**: `WorkTracker.CLI.exe`
   - **WPF**: `WorkTracker.WPF.exe` (Windows only)
   - **Avalonia**: `WorkTracker.Avalonia.exe` (cross-platform)

#### Varianta B: Build ze Zdrojového Kódu

```bash
# Klonování repository
git clone https://github.com/yourusername/WorkTracker.git
cd WorkTracker

# Build
dotnet build -c Release

# Spuštění CLI
dotnet run --project src/WorkTracker.CLI

# Spuštění WPF (Windows only)
dotnet run --project src/WorkTracker.WPF

# Spuštění Avalonia (cross-platform)
dotnet run --project src/WorkTracker.Avalonia
```

### 2.2 První Spuštění

Při prvním spuštění aplikace:

1. **Databáze se automaticky vytvoří** v:
   ```
   %LocalAppData%\WorkTracker\worktracker.db
   ```

2. **Konfigurace je výchozí** - můžete ji upravit v `appsettings.json`

3. **Ověřte funkčnost**:
   ```bash
   # CLI test
   worktracker status
   # Výstup: "No active work entry"
   ```

### 2.3 Základní Konfigurace

#### appsettings.json

Vytvořte/upravte `appsettings.json` vedle spustitelného souboru:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  },
  "Database": {
    "Path": "%LocalAppData%\\WorkTracker\\worktracker.db"
  },
  "Plugins": {
    "tempo": {
      "TempoBaseUrl": "https://api.tempo.io/core/3",
      "TempoApiToken": "",
      "JiraBaseUrl": "https://your-company.atlassian.net",
      "JiraEmail": "your.email@company.com",
      "JiraApiToken": ""
    }
  }
}
```

**⚠️ DŮLEŽITÉ**: Nikdy necommitujte API tokeny do Git!

---

## 3. CLI Rozhraní

### 3.1 Základní Příkazy

#### Start - Začít Sledovat Práci

**Syntaxe:**
```bash
worktracker start [TICKET_ID] [DESCRIPTION] [OPTIONS]
```

**Příklady:**

```bash
# Start s Jira ticket
worktracker start PROJ-123

# Start s popisem (bez ticketu)
worktracker start "Refaktoring databázového layeru"

# Start s ticket i popisem
worktracker start PROJ-123 "Implementace REST API"

# Start se specifickým časem
worktracker start PROJ-123 --time 09:00
worktracker start PROJ-123 -t 14:30

# Vytvoření dokončené práce (s end time)
worktracker start PROJ-123 --start 09:00 --end 12:00
worktracker start PROJ-123 -s 09:00 -e 12:00
```

**Chování:**
- ✅ Automaticky zastaví předchozí aktivní práci
- ✅ Detekuje Jira ticket ID v různých formátech (PROJ-123, proj-123)
- ✅ Zaokrouhluje čas na minuty
- ✅ Validuje, že alespoň ticket nebo popis je zadán

#### Stop - Zastavit Sledování

**Syntaxe:**
```bash
worktracker stop [OPTIONS]
```

**Příklady:**

```bash
# Zastavit aktuální práci
worktracker stop

# Zastavit se specifickým časem
worktracker stop --time 17:00
worktracker stop -t 17:00
```

**Chování:**
- ✅ Kontroluje, že existuje aktivní práce
- ✅ Validuje, že end time je po start time
- ✅ Detekuje překrývající se časy

#### Status - Aktuální Stav

**Syntaxe:**
```bash
worktracker status
```

**Výstup:**
```
╔══════════════════════════════════════════════╗
║           Active Work Entry                  ║
╠══════════════════════════════════════════════╣
║ Ticket:    PROJ-123                         ║
║ Started:   09:00                            ║
║ Duration:  2h 30m                           ║
║ Desc:      Implementing REST API            ║
╚══════════════════════════════════════════════╝
```

#### List - Zobrazit Záznamy

**Syntaxe:**
```bash
worktracker list [DATE_SPEC] [OPTIONS]
```

**Příklady:**

```bash
# Dnešní záznamy
worktracker list today
worktracker list

# Včerejší záznamy
worktracker list yesterday

# Specifický datum
worktracker list 2025-11-01
worktracker list 2025-11-01

# Tento týden (Po-Ne)
worktracker list week

# Minulý týden
worktracker list lastweek

# Datum range
worktracker list 2025-11-01 2025-11-07
```

**Výstup:**
```
╔════════════════════════════════════════════════════════════════════╗
║                    Work Entries - 2025-11-03                       ║
╠════╦══════════╦═══════════╦═══════════╦══════════╦════════════════╣
║ ID ║ Ticket   ║ Start     ║ End       ║ Duration ║ Description    ║
╠════╬══════════╬═══════════╬═══════════╬══════════╬════════════════╣
║ 1  ║ PROJ-123 ║ 09:00     ║ 12:00     ║ 3h 00m   ║ API impl.      ║
║ 2  ║ PROJ-124 ║ 13:00     ║ 17:00     ║ 4h 00m   ║ Bug fixing     ║
╠════╩══════════╩═══════════╩═══════════╩══════════╩════════════════╣
║ Total: 7h 00m                                                      ║
╚════════════════════════════════════════════════════════════════════╝
```

#### Edit - Upravit Záznam

**Syntaxe:**
```bash
worktracker edit <ID> [OPTIONS]
```

**Příklady:**

```bash
# Změnit ticket ID
worktracker edit 5 --ticket PROJ-456

# Změnit časy
worktracker edit 5 --start 09:00 --end 17:00
worktracker edit 5 -s 09:00 -e 17:00

# Změnit popis
worktracker edit 5 --description "Nový popis práce"
worktracker edit 5 -d "Nový popis"

# Změnit všechno najednou
worktracker edit 5 --ticket PROJ-999 --start 08:00 --end 16:00 --description "Complete rewrite"

# Odstranit end time (udělat active)
worktracker edit 5 --end clear
```

#### Delete - Smazat Záznam

**Syntaxe:**
```bash
worktracker delete <ID>
```

**Příklady:**

```bash
# Smazat záznam ID 5
worktracker delete 5

# S potvrzením
worktracker delete 5 --confirm
```

**⚠️ Varování**: Smazání je permanentní!

#### Send - Odeslat do Tempo

**Syntaxe:**
```bash
worktracker send [DATE_SPEC] [OPTIONS]
```

**Příklady:**

```bash
# Odeslat dnešní práci
worktracker send today
worktracker send

# Odeslat včerejší
worktracker send yesterday

# Odeslat specifický datum
worktracker send 2025-11-01

# Odeslat celý týden
worktracker send week

# Náhled před odesláním (dry-run)
worktracker send today --preview
worktracker send today -p

# Force (bez potvrzení)
worktracker send today --force
worktracker send today -f
```

**Výstup při náhledu:**
```
╔════════════════════════════════════════════════════════════╗
║              Worklog Preview - 2025-11-03                  ║
╠═══════════╦═══════════╦══════════╦══════════════════════════╣
║ Ticket    ║ Start     ║ Duration ║ Status                   ║
╠═══════════╬═══════════╬══════════╬══════════════════════════╣
║ PROJ-123  ║ 09:00     ║ 3h 00m   ║ ✓ Ready                  ║
║ PROJ-124  ║ 13:00     ║ 4h 00m   ║ ⚠ Already exists         ║
╠═══════════╩═══════════╩══════════╩══════════════════════════╣
║ Total: 7h 00m (1 new, 1 duplicate)                         ║
╚════════════════════════════════════════════════════════════╝

Continue with upload? [y/N]:
```

**Výstup po odeslání:**
```
╔════════════════════════════════════════════════════════════╗
║              Upload Results                                ║
╠═══════════╦══════════╦════════════════════════════════════╣
║ Ticket    ║ Duration ║ Result                             ║
╠═══════════╬══════════╬════════════════════════════════════╣
║ PROJ-123  ║ 3h 00m   ║ ✓ Success                          ║
║ PROJ-124  ║ 4h 00m   ║ ✗ Error: Already exists            ║
╠═══════════╩══════════╩════════════════════════════════════╣
║ Uploaded: 1/2 (3h 00m)                                     ║
╚════════════════════════════════════════════════════════════╝
```

### 3.2 Pokročilé Použití

#### Aliasy a Shortcuts

Vytvořte si aliasy v Bash/PowerShell:

**Bash (.bashrc):**
```bash
alias wt='worktracker'
alias wts='worktracker start'
alias wtp='worktracker stop'
alias wtl='worktracker list'
alias wtsend='worktracker send today'
```

**PowerShell (Microsoft.PowerShell_profile.ps1):**
```powershell
function wt { worktracker $args }
function wts { worktracker start $args }
function wtp { worktracker stop }
function wtl { worktracker list $args }
function wtsend { worktracker send today }
```

**Použití:**
```bash
wts PROJ-123 "Quick fix"
wtp
wtl today
wtsend
```

#### Scripting a Automatizace

**Auto-send na konci dne:**

```bash
# cron job (Linux/macOS)
0 18 * * * /usr/local/bin/worktracker send today --force

# Task Scheduler (Windows)
# Vytvořte task, který spouští:
worktracker.exe send today --force
```

**Reminder pro stop:**

```bash
# Připomenutí po 8 hodinách práce
while true; do
    sleep 28800  # 8 hours
    notify-send "WorkTracker" "Don't forget to stop tracking!"
done
```

---

## 4. WPF Aplikace

### 4.1 Úvodní Obrazovka

Po spuštění WPF aplikace uvidíte:

```
┌────────────────────────────────────────────────┐
│  WorkTracker           [_] [□] [X]             │
├────────────────────────────────────────────────┤
│  ┌──────────────────────────────────────────┐  │
│  │  Active Work                             │  │
│  │  ┌────────────────────────────────────┐  │  │
│  │  │ ⏱ PROJ-123: API Implementation     │  │  │
│  │  │   Started: 09:00                    │  │  │
│  │  │   Duration: 2h 30m                  │  │  │
│  │  └────────────────────────────────────┘  │  │
│  │  [Stop Work]                             │  │
│  └──────────────────────────────────────────┘  │
│  ┌──────────────────────────────────────────┐  │
│  │  Start New Work                          │  │
│  │  Ticket ID: [___________]                │  │
│  │  Description: [____________________]     │  │
│  │  [Start Work]                            │  │
│  └──────────────────────────────────────────┘  │
│  ┌──────────────────────────────────────────┐  │
│  │  Today's Work (7h 00m)                   │  │
│  │  ┌────────┬───────┬─────┬────────────┐  │  │
│  │  │ Ticket │ Start │ Dur │ Desc       │  │  │
│  │  ├────────┼───────┼─────┼────────────┤  │  │
│  │  │ P-123  │ 09:00 │ 3h  │ API impl   │  │  │
│  │  │ P-124  │ 13:00 │ 4h  │ Bug fix    │  │  │
│  │  └────────┴───────┴─────┴────────────┘  │  │
│  │  [Edit] [Delete] [Submit to Tempo]      │  │
│  └──────────────────────────────────────────┘  │
└────────────────────────────────────────────────┘
```

### 4.2 Hlavní Funkce

#### Přejít na dnešek

Tlačítko **"Go to today"** (nebo klávesová zkratka) rychle přepne zobrazení na dnešní datum, pokud procházíte záznamy z jiných dnů.

#### Oblíbené položky jako šablony

Často používané pracovní záznamy můžete uložit jako šablony:
1. V seznamu prací klikněte pravým tlačítkem na záznam
2. Vyberte **"Zobrazit jako šablonu"** / **"Show as template"**
3. Šablona se zobrazí v panelu pro rychlé spuštění
4. Jedním klikem na šablonu začnete sledovat práci s předvyplněnými údaji

#### Start Work Panel

1. **Zadejte Ticket ID** (volitelné)
   - Automatická validace formátu (PROJ-123)
   - Tooltip s nápovědou

2. **Zadejte Description** (volitelné, pokud máte ticket)
   - Max 500 znaků
   - Real-time validace

3. **Klikněte "Start Work"**
   - Automaticky zastaví předchozí práci
   - Zobrazí notifikaci
   - Přidá do seznamu dnešních prací

#### Active Work Panel

- 🟢 **Zelený indikátor** - práce běží
- ⏱️ **Live timer** - aktualizace každou minutu
- 🛑 **Stop button** - ukončí práci jedním klikem

#### Today's Work List

- 📋 **Tabulka** s dnešními záznamy
- 🔍 **Detaily** při double-click
- ✏️ **Edit** - upravit záznam
- 🗑️ **Delete** - smazat záznam
- 📤 **Submit** - odeslat do Tempo

### 4.3 Editace Záznamu

**Dialog pro editaci:**

```
┌─────────────────────────────────────┐
│  Edit Work Entry                    │
├─────────────────────────────────────┤
│  Ticket ID:                         │
│  [PROJ-123____________]             │
│                                     │
│  Description:                       │
│  [API Implementation_________]      │
│                                     │
│  Start Time:                        │
│  📅 2025-11-03  🕒 [09:00]          │
│                                     │
│  End Time:                          │
│  📅 2025-11-03  🕒 [12:00]          │
│  ☐ Still active (no end time)      │
│                                     │
│  Duration: 3h 00m                   │
│                                     │
│  [Save]  [Cancel]                   │
└─────────────────────────────────────┘
```

**Validace:**
- ✅ End time po start time
- ✅ Žádné překryvy s ostatními záznamy
- ✅ Alespoň ticket nebo popis
- ✅ Valid ticket format

### 4.4 Submit do Tempo

**Submit Dialog:**

```
┌────────────────────────────────────────┐
│  Submit Worklogs to Tempo              │
├────────────────────────────────────────┤
│  Date: ● Today                         │
│        ○ Yesterday                     │
│        ○ This Week                     │
│        ○ Custom: [_______]             │
│                                        │
│  ┌──────────────────────────────────┐ │
│  │ Preview:                         │ │
│  │ ┌─────┬──────┬─────┬──────────┐ │ │
│  │ │ ☑   │Ticket│Dur  │Status    │ │ │
│  │ ├─────┼──────┼─────┼──────────┤ │ │
│  │ │ ☑   │P-123 │ 3h  │✓ Ready   │ │ │
│  │ │ ☑   │P-124 │ 4h  │✓ Ready   │ │ │
│  │ └─────┴──────┴─────┴──────────┘ │ │
│  └──────────────────────────────────┘ │
│                                        │
│  Total: 7h 00m (2 worklogs)            │
│                                        │
│  [Preview]  [Submit]  [Cancel]         │
└────────────────────────────────────────┘
```

### 4.5 System Tray

**Toggle okna:** Kliknutím na ikonu v system tray se hlavní okno zobrazí/skryje (toggle). Toto chování funguje jak ve WPF, tak v Avalonia verzi.

**Tray Icon Menu:**

```
┌─────────────────────────┐
│ 🟢 WorkTracker          │
├─────────────────────────┤
│ ⏱ Active: PROJ-123      │
│   Duration: 2h 30m      │
├─────────────────────────┤
│ ▶ Start Work...         │
│ ⏸ Stop Work            │
│ 📋 Show Window          │
│ 📤 Submit Today         │
├─────────────────────────┤
│ ⚙ Settings              │
│ ❌ Exit                  │
└─────────────────────────┘
```

**Notifikace:**

```
┌────────────────────────────────┐
│ 🟢 WorkTracker                 │
├────────────────────────────────┤
│ Work started on PROJ-123       │
│ API Implementation             │
└────────────────────────────────┘
```

### 4.6 Nastavení

**Settings Dialog:**

```
┌────────────────────────────────────────┐
│  Settings                              │
├────────────────────────────────────────┤
│  General                               │
│  ☑ Start with Windows                 │
│  ☑ Minimize to tray                   │
│  ☑ Show notifications                 │
│  ☐ Auto-submit at end of day          │
│                                        │
│  Tempo Integration                     │
│  Base URL: [___________________]       │
│  API Token: [*********************]    │
│  [Test Connection]                     │
│                                        │
│  Jira Integration                      │
│  Base URL: [___________________]       │
│  Email: [___________________]          │
│  API Token: [*********************]    │
│  [Test Connection]                     │
│                                        │
│  [Save]  [Cancel]                      │
└────────────────────────────────────────┘
```

---

## 4b. Avalonia Aplikace

Avalonia aplikace nabízí stejnou funkcionalitu jako WPF, ale je dostupná na všech platformách (Windows, Linux, macOS).

### Spuštění

```bash
dotnet run --project src/WorkTracker.Avalonia
```

### Hlavní Rozdíly oproti WPF

- **Cross-platform** - funguje na Windows, Linux i macOS
- **Fluent theme** - moderní vzhled s Avalonia Fluent motivem
- **Přepínání motivů** - Dark/Light mód (One Dark Pro / One Light palety)
  - Settings → General → Appearance → Theme dropdown: Dark / Light
- **Material.Icons.Avalonia** - konzistentní ikony napříč platformami
- **System tray** - podpora system tray ikony na všech platformách

### Přepínání Motivů

V nastavení (Settings → General → Appearance) můžete přepínat mezi:
- **Dark** - One Dark Pro paleta (tmavé téma)
- **Light** - One Light paleta (světlé téma)

Změna se projeví okamžitě bez nutnosti restartovat aplikaci.

---

## 5. Workflow a Best Practices

### 5.1 Denní Workflow

#### Ranní Rutina

```bash
# 1. Začít práci na prvním úkolu
worktracker start PROJ-123 "Morning standup preparation"

# 2. Po standuptu přepnout na hlavní úkol
worktracker start PROJ-124 "Implementing authentication"
# (automaticky zastaví PROJ-123)
```

#### Během Dne

```bash
# Přepínat mezi úkoly
worktracker start PROJ-125 "Code review"
worktracker start PROJ-126 "Bug fixing"

# Kontrola co děláte
worktracker status

# Meeting (bez ticketu)
worktracker start "Team meeting"
```

#### Odpolední Check

```bash
# Zkontrolovat dnešní práci
worktracker list today

# Opravit případné chyby
worktracker edit 3 --end 14:00  # Zapomněl jsem zastavit
worktracker delete 5            # Duplicitní záznam
```

#### Večerní Ukončení

```bash
# Zastavit práci
worktracker stop

# Náhled před odesláním
worktracker send today --preview

# Odeslat do Tempo
worktracker send today
```

### 5.2 Best Practices

#### ✅ DO

1. **Začínejte práci ihned**
   ```bash
   worktracker start PROJ-123  # Zapněte tracking hned jak začnete
   ```

2. **Používejte konzistentní formát**
   ```bash
   # Dobře:
   worktracker start PROJ-123 "Implement user authentication"
   worktracker start PROJ-124 "Fix login bug"

   # Špatně:
   worktracker start "stuff"
   worktracker start "work"
   ```

3. **Přepínejte mezi úkoly**
   ```bash
   # Automaticky zastaví předchozí
   worktracker start PROJ-125
   ```

4. **Kontrolujte před odesláním**
   ```bash
   worktracker send today --preview
   ```

5. **Odesílejte denně**
   ```bash
   # Na konci každého dne
   worktracker send today
   ```

#### ❌ DON'T

1. **Nezapomínejte zastavit**
   ```bash
   # Špatně - tracking celou noc!
   # (Musíte pak editovat)
   ```

2. **Neodesílejte nesmysly**
   ```bash
   # Špatně:
   worktracker start "asdf"  # Nepopisný text
   ```

3. **Nevytvářejte překryvy**
   ```bash
   # Aplikace vás zastaví, ale kontrolujte list
   worktracker list today
   ```

### 5.3 Časté Situace

#### Zapomněl jsem zastavit práci

```bash
# Zjistit ID záznamu
worktracker list today

# Editovat end time
worktracker edit 5 --end 17:00
```

#### Pracoval jsem offline

```bash
# Vytvořit záznamy zpětně
worktracker start PROJ-123 --start 09:00 --end 12:00
worktracker start PROJ-124 --start 13:00 --end 17:00

# Odeslat najednou
worktracker send today
```

#### Meeting bez ticketu

```bash
# Použít jen popis
worktracker start "Sprint planning meeting"
worktracker start "1:1 with manager"
```

#### Multi-tasking

```bash
# Špatně - tracking dvou věcí najednou
# (Aplikace to neumožňuje)

# Dobře - přepínat mezi úkoly
worktracker start PROJ-123  # Hlavní úkol
# ... code review request ...
worktracker start PROJ-124  # Code review (zastaví 123)
# ... code review done ...
worktracker start PROJ-123  # Zpět na hlavní
```

#### Oprava chybného záznamu

```bash
# Zjistit ID
worktracker list today

# Opravit
worktracker edit 3 --ticket PROJ-999
worktracker edit 3 --start 10:00 --end 11:30

# Nebo smazat a vytvořit nový
worktracker delete 3
worktracker start PROJ-999 --start 10:00 --end 11:30
```

---

## 6. Integrace s Tempo/Jira

### 6.1 Získání API Credentials

#### Tempo API Token

1. Přihlaste se do Tempo (https://app.tempo.io)
2. Klikněte na **Settings** (⚙️) → **API Integration**
3. Klikněte **New Token**
4. Zadejte název (např. "WorkTracker")
5. Zkopírujte token (zobrazí se jen jednou!)

#### Jira API Token

1. Jděte na https://id.atlassian.com/manage-profile/security/api-tokens
2. Klikněte **Create API token**
3. Zadejte label (např. "WorkTracker")
4. Zkopírujte token

### 6.2 Konfigurace

**Metoda 1: User Secrets (Doporučeno pro development)**

```bash
cd src/WorkTracker.CLI
dotnet user-secrets init
dotnet user-secrets set "Plugins:tempo:TempoApiToken" "your-tempo-token"
dotnet user-secrets set "Plugins:tempo:JiraApiToken" "your-jira-token"
```

**Metoda 2: Environment Variables**

```bash
# Windows PowerShell
$env:WORKTRACKER_TEMPO_TOKEN="your-tempo-token"
$env:WORKTRACKER_JIRA_TOKEN="your-jira-token"

# Linux/macOS
export WORKTRACKER_TEMPO_TOKEN="your-tempo-token"
export WORKTRACKER_JIRA_TOKEN="your-jira-token"
```

**Metoda 3: appsettings.json (Výchozí)**

```json
{
  "Plugins": {
    "tempo": {
      "TempoBaseUrl": "https://api.tempo.io/core/3",
      "TempoApiToken": "your-tempo-token-here",
      "JiraBaseUrl": "https://your-company.atlassian.net",
      "JiraEmail": "your.email@company.com",
      "JiraApiToken": "your-jira-token-here",
      "JiraAccountId": ""  // Optional, auto-detected
    }
  }
}
```

### 6.3 Test Připojení

**CLI:**
```bash
# Test spuštěním preview
worktracker send today --preview

# Pokud vše funguje, uvidíte:
✓ Connected to Tempo API
✓ Connected to Jira API
✓ Account ID detected: 123456:abcd-1234-efgh-5678
```

**WPF:**
- Otevřete Settings
- Klikněte "Test Connection" u Tempo
- Klikněte "Test Connection" u Jira
- Oba by měly zobrazit ✅

### 6.4 Odesílání Worklogs

#### Single Day

```bash
# Dnešní práce
worktracker send today

# Včerejší
worktracker send yesterday

# Specifický datum
worktracker send 2025-11-01
```

#### Multiple Days

```bash
# Celý týden
worktracker send week

# Custom range (zatím nepodporováno, workaround:)
for day in {01..07}; do
    worktracker send 2025-11-$day --force
done
```

#### Náhled a Validace

```bash
# Vždy použijte preview first
worktracker send today --preview

# Výstup ukáže:
# ✓ Záznamy ready k odeslání
# ⚠ Duplicitní záznamy (už v Tempo)
# ✗ Chyby (invalid ticket, missing data)
```

### 6.5 Troubleshooting Tempo/Jira

#### "Unauthorized" Error

```
❌ Error: Unauthorized (401)
```

**Řešení:**
- Zkontrolujte API token
- Ověřte, že token není expirovaný
- Zkontrolujte email pro Jira

#### "Ticket Not Found"

```
❌ Error: Jira issue 'PROJ-123' not found
```

**Řešení:**
- Zkontrolujte ticket ID (case-sensitive)
- Ověřte, že máte přístup k projektu
- Zkontrolujte Jira Base URL

#### "Worklog Already Exists"

```
⚠ Warning: Worklog already exists for PROJ-123 on 2025-11-03
```

**Řešení:**
- Normální - worklog už byl odeslán
- Použijte `--force` pro přepsání (ne doporučeno)
- Nebo smažte v Tempo manuálně

#### Rate Limiting

```
❌ Error: Too many requests (429)
```

**Řešení:**
- Počkejte 1-5 minut
- Neodesílejte batch příliš často
- Tempo má rate limit: 180 requests/minute

---

## 7. Často Kladené Otázky

### Obecné

**Q: Kde je databáze uložena?**
```
A: %LocalAppData%\WorkTracker\worktracker.db
   (Windows: C:\Users\YourName\AppData\Local\WorkTracker\worktracker.db)
```

**Q: Mohu použít WorkTracker na více počítačích?**
```
A: Ano, ale databáze není synchronizována. Budete muset:
   - Použít sdílenou databázi (např. OneDrive)
   - Nebo ručně mergovat data
   - Cloud sync je plánován pro budoucí verzi
```

**Q: Podporuje WorkTracker teams/multi-user?**
```
A: Ne v současnosti. Každý uživatel má svou lokální databázi.
   Multi-user je plánován pro verzi 1.1.
```

### Time Tracking

**Q: Co se stane, když zapomenu zastavit práci?**
```
A: Musíte ručně editovat end time:
   worktracker edit <ID> --end 17:00
```

**Q: Mohu trackovat více úkolů současně?**
```
A: Ne, WorkTracker podporuje pouze jeden aktivní záznam.
   Start nové práce automaticky zastaví předchozí.
```

**Q: Jak vytvořit záznam zpětně?**
```
A: Použijte start s --start a --end:
   worktracker start PROJ-123 --start 09:00 --end 12:00
```

### Integrace

**Q: Podporuje WorkTracker jiné systémy než Tempo?**
```
A: Ne out-of-the-box, ale můžete vytvořit vlastní plugin.
   Viz PLUGIN_DEVELOPMENT.md
```

**Q: Musím mít Jira/Tempo účet?**
```
A: Ne, můžete používat WorkTracker jen pro lokální tracking.
   Tempo integrace je volitelná.
```

**Q: Jsou moje API tokeny bezpečné?**
```
A: Doporučujeme používat User Secrets nebo Environment Variables.
   NIKDY necommitujte tokeny do Git!
   Zvažte Windows Credential Manager pro produkci.
```

### Technické

**Q: Jaká .NET verze je potřeba?**
```
A: .NET 10.0 runtime
```

**Q: Funguje WorkTracker na Linux/macOS?**
```
A: CLI ano (dotnet run), Avalonia aplikace ano (plná cross-platform podpora),
   WPF ne (pouze Windows).
```

**Q: Jak mohu backupovat data?**
```
A: Zkopírujte databázový soubor:
   copy %LocalAppData%\WorkTracker\worktracker.db backup.db
```

---

## 8. Troubleshooting

### CLI Issues

#### "Command not found"

```bash
# Pokud není worktracker v PATH
dotnet run --project src/WorkTracker.CLI -- [command]

# Nebo přidejte do PATH:
# Windows: Přidejte WorkTracker/bin/Release/net10.0 do PATH
# Linux: sudo ln -s /path/to/worktracker /usr/local/bin/
```

#### "Database locked"

```
Error: SQLite Error 5: 'database is locked'
```

**Řešení:**
- Zavřete všechny instance WorkTracker (CLI, WPF i Avalonia)
- Zkontrolujte Task Manager pro visící procesy
- Smažte `worktracker.db-wal` a `worktracker.db-shm` soubory

### WPF Issues

#### "Application won't start"

**Řešení:**
1. Zkontrolujte .NET 10.0 runtime
2. Podívejte se do Event Viewer (Windows Logs → Application)
3. Smažte %LocalAppData%\WorkTracker\logs

#### "System tray icon missing"

**Řešení:**
- Zkontrolujte Settings → "Minimize to tray" je zapnuté
- Restartujte aplikaci
- Zkontrolujte Windows notification area settings

### Database Issues

#### "Corrupted database"

```
Error: SQLite Error 11: 'database disk image is malformed'
```

**Řešení:**
```bash
# 1. Backup současné databáze
copy worktracker.db worktracker.db.broken

# 2. Pokus o repair
sqlite3 worktracker.db "PRAGMA integrity_check"

# 3. Export/import
sqlite3 worktracker.db .dump > backup.sql
sqlite3 worktracker_new.db < backup.sql

# 4. Restore
move worktracker_new.db worktracker.db
```

### Tempo/Jira Issues

Viz sekce [6.5 Troubleshooting Tempo/Jira](#65-troubleshooting-tempojira)

---

## Podpora

- 📧 **Email**: support@worktracker.example.com
- 🐛 **Issues**: [GitHub Issues](https://github.com/yourusername/WorkTracker/issues)
- 💬 **Diskuze**: [GitHub Discussions](https://github.com/yourusername/WorkTracker/discussions)
- 📖 **Docs**: [Documentation](https://github.com/yourusername/WorkTracker/docs)

---

**Poslední aktualizace**: Březen 2026
**Verze dokumentu**: 1.1
