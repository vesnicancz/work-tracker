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
| `TenantId` | `Text` | ✅ | — | GUID tvé Entra ID organizace, nebo `organizations` pro multi‑tenant scénář |
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
- **Instrukce**: *„V Nastavení → Pluginy → Office 365 Calendar vlož tyto dvě GUID a klikni **Otestovat připojení**. Aplikace ti dá odkaz a kód — otevři odkaz, zadej kód a přihlas se svým pracovním účtem. Hotovo.“*

---

## První přihlášení (device code flow)

Po vyplnění `TenantId` a `ClientId` klikni v Nastavení → Pluginy → Office 365 Calendar na **Otestovat připojení**. Plugin spustí **MSAL device code flow**:

1. Pod tlačítkem se místo výsledku testu objeví text:
   ```
   ⏳ Open browser and enter code: ABCD-EFGH
   https://microsoft.com/devicelogin
   ```
2. Aplikace se současně pokusí otevřít browser na této URL (`Process.Start`). Pokud ne, otevři ji ručně.
3. V browseru zadej kód, přihlaš se pracovním účtem, potvrď consent (pokud se zobrazí).
4. MSAL mezitím pollne Entra a jakmile tvůj login prošel, vrátí access + refresh token.
5. Tokeny se uloží do šifrované cache — na Windows soubor v `%LocalAppData%\WorkTracker\keys\` (DPAPI), na macOS do Keychainu a na Linuxu do keyringu (libsecret). V non‑Production prostředích se adresář aplikace suffixuje podle `DOTNET_ENVIRONMENT`, takže přesná cesta může být např. `%LocalAppData%\WorkTracker_Development\keys\`.
6. Text pod tlačítkem nahradí výsledek testu a plugin je ready.

Od tohoto okamžiku plugin při běžném použití zkouší jen `AcquireTokenSilentAsync` — tokeny se tiše obnovují přes refresh token (platnost refresh tokenu bývá kolem 90 dnů nebo dle konfigurace tenanta). Jakmile ale refresh token expiruje nebo silent auth selže z jiného důvodu, `GetSuggestionsAsync` **nespustí device code flow automaticky** — rovnou vrátí chybu `Not authenticated — please use Test Connection in Settings to sign in first.` Uživatel musí ručně otevřít **Nastavení → Pluginy → Office 365 Calendar → Otestovat připojení**, které interaktivní device code flow spustí a obnoví cache.

---

## Jak se události mapují na `WorkSuggestion`

Plugin volá Microsoft Graph:

```
GET https://graph.microsoft.com/v1.0/me/calendarView?
    startDateTime={date}T00:00:00+01:00&
    endDateTime={date}T23:59:59+01:00&
    $select=subject,start,end,webLink,id,isAllDay&
    $orderby=start/dateTime&
    $top=250
```

Začátek a konec dne se posílají v ISO 8601 s lokálním offsetem (URL‑encoded). Plugin volá Graph s explicitním `$select` — a pouze tato pole se mapují na `WorkSuggestion`. Popis události (body / bodyPreview) plugin **nenačítá**. Výsledky stránkuje po 250 událostech přes `@odata.nextLink` (max. 10 stránek). Každý request posílá hlavičku `Prefer: outlook.timezone="{TimeZoneInfo.Local.Id}"`, takže Graph vrací časy rovnou v lokální zóně a plugin je už nepřevádí.

| `WorkSuggestion` | Zdroj z Graph |
|------------------|---------------|
| `Title` | `subject` (bez předmětu `"(No subject)"`) |
| `Description` | — (plugin `bodyPreview` ani `body` nenačítá) |
| `StartTime` | `start.dateTime` (v lokálním čase díky hlavičce `Prefer`; u celodenních událostí prázdné) |
| `EndTime` | `end.dateTime` (v lokálním čase díky hlavičce `Prefer`; u celodenních událostí prázdné) |
| `Source` | `"O365 Calendar"` |
| `SourceId` | `id` (unikátní event ID v kalendáři) |
| `SourceUrl` | `webLink` (odkaz do Outlook Web) |

**Filtrace:**

- `IncludeAllDayEvents = false` → události s `isAllDay: true` jsou přeskočené. S `true` se zobrazí taky, ale bez vyplněného začátku a konce.
- Aktuální implementace samostatně **nefiltruje** eventy podle `showAs` / stavu (Cancelled, Tentative, Free, …) — do suggestions se propíší všechny eventy vrácené Graph API (celodenní jen se zapnutým `IncludeAllDayEvents`).

---

## Jak to použít v UI

1. V hlavním okně vyber den a otevři **Návrhy** (ikona žárovky). Návrhy se načtou pro datum vybrané v hlavním okně.
2. Sekce **Office 365 Calendar** ukáže všechny události.
3. Kliknutí na událost otevře dialog nového záznamu s předvyplněným:
   - ticketem = prázdné (kalendářové eventy obvykle nemají Jira kód)
   - popisem = `event.subject`
   - startem / koncem = časy eventu

Pokud chceš ticket zadat, uprav záznam před uložením.

---

## Test connection — co testuje

1. Získá token přes `AcquireTokenInteractiveAsync` (spustí device code flow, pokud není platná silent cache).
2. Zavolá `GET https://graph.microsoft.com/v1.0/me` — ověří, že token funguje a že je možné načíst identitu přihlášeného uživatele.
3. Do `IProgress<string>` se reportuje jen výzva s kódem a URL pro device code flow; výsledek testu (úspěch / chyba) zobrazí Nastavení.

> **Pozor:** Test connection **neověřuje** scope `Calendars.Read` ani endpoint `calendarView`. Pokud by oprávnění chybělo, chyba se projeví až při prvním skutečném načítání návrhů v Suggestions dialogu. Pokud v Entra registraci zapomeneš delegated permission `Calendars.Read`, Test connection projde, ale načtení eventů selže s 403.

---

## Časté chyby

| Symptom | Příčina | Řešení |
|---------|---------|--------|
| `AADSTS9002331: Application is configured for use by X` | „Allow public client flows“ není zapnuté v Entra registraci | Azure Portal → App → Authentication → zapni |
| `AADSTS65001: The user or administrator has not consented` | Chybí consent pro `Calendars.Read` | V API permissions klikni **Grant admin consent** |
| Warning v logu `Encrypted token cache not available, falling back to unprotected file` | Linux bez libsecret (headless) | Nainstaluj `libsecret-1-0` nebo přijmi nešifrovanou cache (soubor `msal_…_plain.bin` v `keys/`) |
| Device code flow skončí timeoutem | Uživatel nepotvrdil login do ~15 min | Klikni **Otestovat připojení** znovu |
| Plugin vrací prázdný seznam eventů | Není žádný meeting daný den — nebo timezone konflikt | Zkontroluj, že datum ve vyhledávání odpovídá místnímu času |
| Test connection hlásí `403 Forbidden` | Delegated permission chybí nebo není admin consent | Zkontroluj API permissions v Entra |

---

## Odhlášení / reset tokenů

1. Zavři WorkTracker.
2. Smaž soubory `msal_*.bin` ve složce `keys` aktuálního prostředí (`%LocalAppData%\WorkTracker\keys\`, resp. `WorkTracker_Development\keys\`). Soubory se jmenují `msal_<SHA256 z TenantId:ClientId>.bin` (případně `…_plain.bin`), takže je podle pluginu nepoznáš — pluginy se stejným TenantId + ClientId sdílejí jednu cache. Na macOS je cache v Keychainu (služba `WorkTracker`, účet `msal-<hash>`) a na Linuxu v keyringu (položka „MSAL token cache“) — tam smaž i ty.
3. Spusť aplikaci. Při příštím **Otestovat připojení** proběhne nový device code flow.

Nebo rovnou smaž celou složku `keys/` (a položky v Keychainu / keyringu) — způsobí reset všech MSAL pluginů.

---

## Plugin config schéma (`appsettings.json`)

> **Pozor — tři omezení:**
>
> 1. **Samotné `appsettings.json` plugin nezapne.** GUI i `WorkTracker.CLI` čtou enabled stav a konfiguraci pluginů z uživatelského `settings.json`, který zapisuje GUI; sekce `Plugins` v `appsettings.json` je jen záložní konfigurace.
> 2. **CLI suggestions vůbec nepoužívá.** Office 365 Calendar je `IWorkSuggestionPlugin` a žádný CLI příkaz suggestions nenačítá — tento plugin se dá prakticky využít jen v GUI.
> 3. **Plugin vyžaduje předchozí GUI login.** `GetSuggestionsAsync` volá **pouze** `AcquireTokenSilentAsync` — při `null` rovnou vrátí chybu „Not authenticated — please use Test Connection in Settings to sign in first.“ Interaktivní device code flow běží **jen** v `TestConnectionAsync`, kterou musí uživatel spustit z GUI Nastavení (to je shodné s Goran G3 pluginem).
>
> Plugin tedy konfiguruj v GUI (**Nastavení → Pluginy → Office 365 Calendar**). Schéma níže ukazuje tvar záložní konfigurace.

```json
{
  "Plugins": {
    "o365.calendar": {
      "TenantId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
      "ClientId": "yyyyyyyy-yyyy-yyyy-yyyy-yyyyyyyyyyyy",
      "IncludeAllDayEvents": false
    }
  }
}
```
