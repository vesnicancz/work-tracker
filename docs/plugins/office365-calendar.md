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
5. Tokeny se uloží do šifrované cache v `%LocalAppData%\WorkTracker\keys\` (DPAPI/Keychain/libsecret dle OS). V non‑Production prostředích se adresář aplikace suffixuje podle `DOTNET_ENVIRONMENT`, takže přesná cesta může být např. `%LocalAppData%\WorkTracker_Development\keys\`.
6. Progress dialog zobrazí „OK“ a plugin je ready.

Od tohoto okamžiku plugin vždy zkusí `AcquireTokenSilentAsync` — tokeny se obnovují automaticky přes refresh token (typicky platnost refresh tokenu je 90 dnů nebo dle konfigurace tenanta). Jakmile refresh token expiruje, plugin spustí device code flow znovu.

---

## Jak se události mapují na `WorkSuggestion`

Plugin volá Microsoft Graph:

```
GET https://graph.microsoft.com/v1.0/me/calendarView?
    startDateTime={date}T00:00:00&
    endDateTime={date}T23:59:59
```

Plugin volá Graph s explicitním `$select=subject,start,end,webLink,id,isAllDay` — a pouze tato pole se mapují na `WorkSuggestion`. Popis události (body / bodyPreview) plugin **nenačítá**.

| `WorkSuggestion` | Zdroj z Graph |
|------------------|---------------|
| `Title` | `subject` |
| `Description` | — (plugin `bodyPreview` ani `body` nenačítá) |
| `StartTime` | `start.dateTime` (konvertováno na local time) |
| `EndTime` | `end.dateTime` (konvertováno na local time) |
| `Source` | `"O365 Calendar"` |
| `SourceId` | `id` (unikátní event ID v kalendáři) |
| `SourceUrl` | `webLink` (odkaz do Outlook Web) |

**Filtrace:**

- `IncludeAllDayEvents = false` → události s `isAllDay: true` jsou přeskočené.
- Aktuální implementace samostatně **nefiltruje** eventy podle `showAs` / stavu (Cancelled, Tentative, Free, …) — do suggestions se propíší všechny nenulové ne‑all‑day eventy vrácené Graph API.

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

1. Získá token přes `AcquireTokenInteractiveAsync` (spustí device code flow, pokud není platná silent cache).
2. Zavolá `GET https://graph.microsoft.com/v1.0/me` — ověří, že token funguje a že je možné načíst identitu přihlášeného uživatele.
3. Reportuje průběh do `IProgress<string>` (stavy typu „Získávám token…”, „Ověřuji identitu…”, „OK”).

> **Pozor:** Test connection **neověřuje** scope `Calendars.Read` ani endpoint `calendarView`. Pokud by oprávnění chybělo, chyba se projeví až při prvním skutečném načítání návrhů v Suggestions dialogu. Pokud v Entra registraci zapomeneš delegated permission `Calendars.Read`, Test connection projde, ale načtení eventů selže s 403.

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

## Plugin config schéma (`appsettings.json`)

> **Pozor — tři omezení:**
>
> 1. **CLI pluginy neenable-uje.** `WorkTracker.CLI` volá `InitializePluginsAsync` bez enable mapy, takže i správně vyplněné `appsettings.json` pluginy nespustí.
> 2. **CLI suggestions vůbec nepoužívá.** Office 365 Calendar je `IWorkSuggestionPlugin` a žádný CLI příkaz suggestions nenačítá — tento plugin se dá prakticky využít jen v GUI.
> 3. **Plugin vyžaduje předchozí GUI login.** `GetSuggestionsAsync` volá **pouze** `AcquireTokenSilentAsync` — při `null` rovnou vrátí chybu „Not authenticated — please use Test Connection in Settings to sign in first." Interaktivní device code flow běží **jen** v `TestConnectionAsync`, kterou musí uživatel spustit z GUI Settings (to je shodné s Goran G3 pluginem).
>
> Plugin tedy konfiguruj v GUI (**Nastavení → Pluginy → Office 365 Calendar**). Schéma níže je referenční pro integrátory, kteří si píšou vlastního hosta nad `WorkTracker.Infrastructure`.

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
