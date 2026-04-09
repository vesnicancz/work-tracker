# Plugin: Office 365 Calendar

| Plugin ID | `o365.calendar` |
|-----------|-----------------|
| Třída | `Office365CalendarPlugin` |
| Rozhraní | `IWorkSuggestionPlugin` |
| Autentizace | MSAL Device Code Flow (Entra ID) |
| API | Microsoft Graph |

Plugin čte události z uživatelova Outlook/Office 365 kalendáře a nabízí je jako work suggestions v dialogu návrhů. Typické použití: automaticky vygeneruj worklog pro meeting, kterému jsi věnoval/a čas.

---

## Konfigurační pole

| Pole | Typ | Povinné | Default | Popis |
|------|-----|---------|---------|-------|
| `TenantId` | `Text` | ✅ | — | GUID tvé Entra ID organizace |
| `ClientId` | `Text` | ✅ | — | GUID aplikace zaregistrované v Entra |
| `IncludeAllDayEvents` | `Checkbox` | ❌ | `false` | Zahrnout celodenní události (typicky dovolené, narozeniny) |

**Scopes** plugin si sám nakonfiguruje: `Calendars.Read`, `User.Read`. Tyto uděluj v Entra registraci jako delegated permissions.

---

## Entra registrace

Plugin potřebuje aplikační registraci v Microsoft Entra ID. Pokud nejsi admin, požádej IT:

### Postup (admin)

1. **Azure Portal → Microsoft Entra ID → App registrations → New registration**.
2. **Name** — libovolný, např. „WorkTracker Desktop“.
3. **Supported account types** — **Accounts in this organizational directory only** (single tenant) nebo **Any organizational directory** podle potřeby.
4. **Redirect URI** — nech prázdné. Device code flow ho nepoužívá.
5. **Register**.
6. Na stránce registrace si zkopíruj **Application (client) ID** a **Directory (tenant) ID** — ty půjdou do pluginu.
7. **Authentication** → **Advanced settings** → **Allow public client flows** → **Yes**. **Save**.
   - Bez tohoto device code flow selže s „AADSTS9002331: Application is configured for use by …“.
8. **API permissions** → **Add a permission** → **Microsoft Graph** → **Delegated permissions**:
   - ✅ `Calendars.Read`
   - ✅ `User.Read` (obvykle už je)
9. **Grant admin consent** (pokud je třeba pro daného tenanta).

### Propagace uživatelům

Sděl uživatelům:

- **Tenant ID**: `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`
- **Client ID**: `yyyyyyyy-yyyy-yyyy-yyyy-yyyyyyyyyyyy`
- **Instrukce**: *„V Settings → Plugins → Office 365 Calendar vlož tyto dvě GUID a klikni **Test connection**. Aplikace ti dá odkaz a kód — otevři odkaz, zadej kód a přihlas se svým pracovním účtem. Hotovo.“*

---

## První přihlášení (device code flow)

Po vyplnění `TenantId` a `ClientId` klikni v Settings → Plugins → Office 365 Calendar na **Test connection**. Plugin spustí **MSAL device code flow**:

1. V progress dialogu se objeví text podobný:
   ```
   Otevři https://microsoft.com/devicelogin a zadej kód ABCD-EFGH
   ```
2. Aplikace se současně pokusí otevřít browser na této URL (`Process.Start`). Pokud ne, otevři ji ručně.
3. V browseru zadej kód, přihlaš se pracovním účtem, potvrď consent (pokud se zobrazí).
4. MSAL mezitím pollne Entra a jakmile tvůj login prošel, vrátí access + refresh token.
5. Tokeny se uloží do šifrované cache v `%LocalAppData%\WorkTracker\keys\` (DPAPI/Keychain/libsecret dle OS).
6. Progress dialog zobrazí „OK“ a plugin je ready.

Od tohoto okamžiku plugin vždy zkusí `AcquireTokenSilentAsync` — tokeny se obnovují automaticky přes refresh token (typicky platnost refresh tokenu je 90 dnů nebo dle konfigurace tenanta). Jakmile refresh token expiruje, plugin spustí device code flow znovu.

---

## Jak se události mapují na `WorkSuggestion`

Plugin volá Microsoft Graph:

```
GET https://graph.microsoft.com/v1.0/me/calendarview?
    startDateTime={date}T00:00:00&
    endDateTime={date}T23:59:59
```

Každý event se mapuje:

| `WorkSuggestion` | Zdroj z Graph |
|------------------|---------------|
| `Title` | `subject` |
| `Description` | `bodyPreview` (textový náhled) |
| `StartTime` | `start.dateTime` (konvertováno na local time) |
| `EndTime` | `end.dateTime` (konvertováno na local time) |
| `Source` | `"Office 365 Calendar"` |
| `SourceId` | `id` (unikátní event ID v kalendáři) |
| `SourceUrl` | `webLink` (odkaz do Outlook Web) |

**Filtrace:**

- `IncludeAllDayEvents = false` → události s `isAllDay: true` jsou přeskočené.
- Cancelled eventy jsou přeskočené.
- Tentative eventy (user má status „tentatively accepted“) jsou **zahrnuté** — možná na nich pracoval/a.

---

## Jak to použít v UI

1. V hlavním okně → **Suggestions** (ikona žárovky).
2. Vyber datum.
3. Sekce **Office 365 Calendar** ukáže všechny události.
4. Dvojklik (nebo tlačítko **Use**) vytvoří nový `WorkEntry` s předvyplněným:
   - ticketem = prázdné (kalendářové eventy obvykle nemají Jira kód)
   - popisem = `event.subject`
   - startem / koncem = časy eventu

Pokud chceš ticket zadat, uprav záznam před uložením.

---

## Test connection — co testuje

1. Získá token (silent nebo device code flow).
2. Zavolá `GET /me` — ověří, že token funguje a má scope `User.Read`.
3. Zavolá `GET /me/calendarview?startDateTime={dnes}&endDateTime={dnes}` — ověří, že scope `Calendars.Read` funguje.
4. Reportuje průběh do `IProgress<string>`: „Získávám token…“, „Ověřuji identitu…“, „Ověřuji přístup ke kalendáři…“, „OK“.

---

## Časté chyby

| Symptom | Příčina | Řešení |
|---------|---------|--------|
| `AADSTS9002331: Application is configured for use by X` | „Allow public client flows“ není zapnuté v Entra registraci | Azure Portal → App → Authentication → zapni |
| `AADSTS65001: The user or administrator has not consented` | Chybí consent pro `Calendars.Read` | V API permissions klikni **Grant admin consent** |
| `Token cache file cannot be encrypted, falling back to plaintext` | Linux bez libsecret (headless) | Nainstaluj `libsecret-1-0` nebo přijmi plaintext cache (logován jako warning) |
| Device code flow skončí timeoutem | Uživatel nepotvrdil login do ~15 min | Klikni **Test connection** znovu |
| Plugin vrací prázdný seznam eventů | Není žádný meeting daný den — nebo timezone konflikt | Zkontroluj, že datum ve vyhledávání odpovídá místnímu času |
| Test connection hlásí `403 Forbidden` | Delegated permission chybí nebo není admin consent | Zkontroluj API permissions v Entra |

---

## Odhlášení / reset tokenů

1. Zavři WorkTracker.
2. Smaž soubory v `%LocalAppData%\WorkTracker\keys\` (obsahuje MSAL cache pro všechny pluginy — smaž jen ty, co začínají hash‑em pro Office 365).
3. Spusť aplikaci. Při příštím `Test connection` proběhne nový device code flow.

Nebo rovnou smaž celou složku `keys/` — způsobí reset všech MSAL pluginů.

---

## Fallback: appsettings.json pro CLI

```json
{
  "Plugins": {
    "o365.calendar": {
      "TenantId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
      "ClientId": "yyyyyyyy-yyyy-yyyy-yyyy-yyyyyyyyyyyy",
      "IncludeAllDayEvents": "false"
    }
  }
}
```

Pozn.: device code flow v CLI fungovat bude, ale uživatel musí kód zkopírovat z konzole (`progress` se v CLI vypíše jako běžný `stdout`).
