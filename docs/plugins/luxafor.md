# Plugin: Luxafor LED

| Plugin ID | `luxafor.status-indicator` |
|-----------|----------------------------|
| Třída | `LuxaforStatusIndicatorPlugin` |
| Rozhraní | `IStatusIndicatorPlugin` |
| Hardware | Luxafor Bluetooth Pro / Flag / Orb |
| Knihovna | `DotLuxafor` 0.2.2 (lokální feed `packages/`) |
| API | `ILuxaforDeviceManager.Open()` → `DeviceOpenResult` → `ILuxaforDevice` (`SetColorAsync`, `TurnOffAsync`) |

Plugin ovládá fyzický LED indikátor Luxafor a přepíná jeho barvu podle aktuální fáze Pomodoro timeru. Cílem je okamžité vizuální signalizování stavu pro sebe i okolí („jsem v zóně, nerušit” vs. „mám pauzu”).

---

## Konfigurační pole

| Pole | Typ | Povinné | Default | Popis |
|------|-----|---------|---------|-------|
| `work_color` | `Text` | ❌ | `#FF0000` (červená) | Barva během fáze **Work** |
| `short_break_color` | `Text` | ❌ | `#00FF00` (zelená) | Barva během krátké pauzy |
| `long_break_color` | `Text` | ❌ | `#0000FF` (modrá) | Barva během dlouhé pauzy |
| `turn_off_on_startup` | `Checkbox` | ❌ | `false` | Po startu aplikace zařízení zhasne (řeší rušivé červené svícení po bootu OS) |

Validační regex: `^#[0-9A-Fa-f]{6}$` — přesně 6 hex znaků s úvodním `#`. Plugin v UI chybí color picker; hodnoty se zadávají jako text. Běžné volby:

| Barva | Hex |
|-------|-----|
| Červená | `#FF0000` |
| Oranžová | `#FF8000` |
| Žlutá | `#FFFF00` |
| Zelená | `#00FF00` |
| Azurová | `#00FFFF` |
| Modrá | `#0000FF` |
| Fialová | `#8000FF` |
| Bílá | `#FFFFFF` |

Fáze `Idle` (Pomodoro neběží) LED **zhasne** — plugin zavolá `device.TurnOffAsync()`.

---

## Požadavky na hardware

Podporovaná zařízení:

- **Luxafor Bluetooth Pro** (BT4.0) — doporučené, bezdrátové, ale vyžaduje Bluetooth párování.
- **Luxafor Flag** (USB) — pohodlné pro stacionární setup.
- **Luxafor Orb** (USB).

Všechna zařízení komunikují přes **HID API**. Plugin používá knihovnu [`DotLuxafor`](https://github.com/vesnicancz/dotluxafor) — na nuget.org publikovaná **není**, `.nupkg` leží v repu ve složce `packages/` a `nuget.config` ji mapuje jako zdroj `local`. Upgrade = stáhnout nový `.nupkg` z GitHub releases do `packages/` a přepsat verzi v `Directory.Packages.props`. Knihovna nabízí vysokoúrovňové API `ILuxaforDeviceManager` / `ILuxaforDevice` specifické pro Luxafor protokol — plugin sám žádnou HID logiku neimplementuje.

### Ovladače

- **Windows**: HID class driver je v systému — nic neinstaluj.
- **Linux**: Potřebuješ `libusb` a udev pravidla, aby uživatel měl přístup k zařízení bez roota:

  ```
  # /etc/udev/rules.d/99-luxafor.rules
  SUBSYSTEM=="usb", ATTRS{idVendor}=="04d8", ATTRS{idProduct}=="f372", MODE="0666"
  ```

  Po vytvoření: `sudo udevadm control --reload-rules && sudo udevadm trigger`.

- **macOS**: HID class driver v systému — nic neinstaluj. Pro Bluetooth Pro je potřeba zařízení nejdřív spárovat v **System Settings → Bluetooth**.

---

## Jak plugin pracuje

### Inicializace

1. `OnInitializeAsync` načte barvy z konfigurace, zkusí je naparsovat a při neúspěchu použije defaulty. Zařízení se v tomto kroku samo o sobě **neotevírá**; otevřít se může jen v případě popsaném v bodě 4, kdy je zapnuté `turn_off_on_startup`.
2. Teprve při prvním volání `SetStateAsync` plugin zavolá interní helper `GetOrOpenDevice()`, který přes `ILuxaforDeviceManager.Open()` otevře HID connection k aktuálně připojenému Luxaforu.
3. Pokud zařízení v tu chvíli **není** připojené, `_device` zůstane `null` a volání je no‑op; další pokus proběhne při příštím `SetStateAsync`. Rozdíl mezi „nic není zapojené” a „zařízení je zapojené, ale nejde otevřít” plugin **loguje**: `NotFound` jako `Debug`, `AccessDenied` / `InUse` / `Failed` jako `Warning` včetně hlášky knihovny s platformním hintem (udev pravidlo na Linuxu, Input Monitoring na macOS, jiná aplikace držící zařízení).
4. Pokud je zapnuté `turn_off_on_startup`, plugin při **první** inicializaci okamžitě zavolá `SetStateAsync(Idle)`. Pokud je zařízení v tu chvíli připojené, otevře se a zhasne; pokud připojené není, volání je stejně jako v bodě 3 no‑op. Při re-inicializaci (změna configu za běhu) už k zhasnutí nedojde, aby plugin nerušil probíhající Pomodoro.

### Reakce na Pomodoro

Avalonia aplikace si předplatí `IPomodoroService.PhaseChanged`. Při eventu:

```csharp
void OnPhaseChanged(PomodoroPhase newPhase)
{
    var state = newPhase switch
    {
        PomodoroPhase.Work => StatusIndicatorState.Work,
        PomodoroPhase.ShortBreak => StatusIndicatorState.ShortBreak,
        PomodoroPhase.LongBreak => StatusIndicatorState.LongBreak,
        _ => StatusIndicatorState.Idle,
    };

    foreach (var indicator in _pluginManager.StatusIndicatorPlugins)
    {
        if (indicator.IsDeviceAvailable)
        {
            _ = indicator.SetStateAsync(state, CancellationToken.None);
        }
    }
}
```

`LuxaforStatusIndicatorPlugin.SetStateAsync`:

1. Zamkne `SemaphoreSlim` (operace se zařízením **nesmí** běžet paralelně — DotLuxafor i HID obecně očekávají sériový přístup).
2. Zapamatuje si stav (`_lastState`), aby ho **Test connection** uměl po probliknutí obnovit.
3. Přes `GetOrOpenDevice()` získá otevřený `ILuxaforDevice` (nebo ho lazy otevře, pokud jím plugin ještě nedisponuje).
4. Podle stavu zavolá buď `device.SetColorAsync(color)` s barvou odpovídající fázi, nebo `device.TurnOffAsync()` pro `Idle`.
5. Uvolní semafor.

### Odpojení uprostřed příkazu

`ILuxaforDevice.IsConnected` **není** dotaz na sběrnici — říká jen „handle jsme nezavřeli", takže vytažený kabel property nezaznamená. Zmizelé zařízení se pozná až tím, že příkaz vyhodí `LuxaforDeviceDisconnectedException`. Kdyby plugin jen zavřel handle a čekal na další fázi, LED by se pro tu aktuální neaktualizovala vůbec — a další přechod je klidně za 25 minut.

Helper `RunWithReopenAsync` proto příkaz **jednou zopakuje**:

1. Chytí `LuxaforDeviceDisconnectedException`, zavře handle.
2. Znovu otevře zařízení — přednostně to, které výjimka pojmenovala (`ex.Descriptor` → `Open(descriptor)`), s fallbackem na `Open()`, kdyby se zařízení vrátilo na jiné cestě (jiný USB port). **Pozor:** ten fallback může při víc připojených Luxaforech trefit jiný kus; plugin s jedním zařízením počítá, takže je to přijatelné, ale kdyby někdy uměl výběr zařízení, fallback musí pryč a reopen zůstane jen podle `DevicePath`.
3. Příkaz pustí znovu. Druhé selhání už znamená, že zařízení opravdu není: výjimka probublá do `SetStateAsync`, kde se zaloguje jako `Debug` a handle se zavře.

Jiná než disconnect výjimka se neopakuje — jde do logu jako `Warning` a zařízení se zavře.

Knihovna nabízí i `ILuxaforDeviceManager.IsPresent(descriptor)` jako skutečnou liveness kontrolu, ale stojí enumeraci zařízení; plugin ji nepoužívá, protože reakce na výjimku pokryje totéž bez ceny za každý příkaz.

### Thread safety

Ačkoli WorkTracker v běžném provozu nevolá `SetStateAsync` z více vláken, plugin je napsaný defenzivně pro případ že:

- Aplikace zpracovává víc event sourců zároveň (Pomodoro + ruční trigger).
- V budoucnu plugin dostane vlastní watchdog thread.

---

## `IsDeviceAvailable`

Property `IsDeviceAvailable` vrací `true`, když má plugin otevřený handle (`_device` je non-null a `IsConnected`). Vzhledem k tomu, co `IsConnected` doopravdy znamená (viz výše), je to **„handle držíme"**, ne „zařízení je fyzicky připojené" — po odpojení zůstane `true`, dokud se nepokusíme o příkaz. Přesnou odpověď by dalo `IsPresent(descriptor)`, ale property by pak při každém čtení enumerovala USB zařízení; plugin proto zůstává u levné varianty.

Při selhání operace v `SetStateAsync` plugin zařízení zavře a další volání se pokusí o znovupřipojení přes `GetOrOpenDevice()`. V aktuální implementaci **není počítaný limit** reconnect pokusů — plugin bude při každém dalším volání zkoušet reconnect znovu, dokud se nepodaří nebo dokud zařízení nezmizí nadobro.

---

## Ověření funkčnosti

Plugin implementuje `ITestablePlugin`, takže se v Settings UI zobrazí tlačítko **Test connection** (viditelnost řídí `PluginViewModel.SupportsTestConnection`). Test:

1. Otevře zařízení — a když to nejde, vrátí **důvod** z `DeviceOpenResult.Description` místo obecného selhání. Kategorie chyby: `NotFound` (nic není zapojené), `Authentication` (`AccessDenied` — chybí udev pravidlo / Input Monitoring), jinak `Internal` (např. zařízení drží jiná aplikace).
2. Rozsvítí LED barvou fáze **Work** na 1 sekundu, takže je vidět, které zařízení odpovídá. Pokud byl handle zastaralý, projde se stejným reopen jako `SetStateAsync`.
3. Vrátí LED na aktuální Pomodoro fázi (nebo ji zhasne, pokud timer neběží) — test ze Settings nesmí nechat běžící Pomodoro na špatné barvě.

Kromě testu tlačítkem lze funkčnost ověřit i prakticky:

1. Ujisti se, že je plugin **enabled** a konfigurace je uložená.
2. Připoj Luxafor zařízení (USB nebo Bluetooth Pro).
3. Spusť Pomodoro a sleduj, jestli LED přepíná barvy podle aktuální fáze: **Work**, **Short Break**, **Long Break**. Mimo běh Pomodora plugin LED zhasne (idle).
4. Pokud se LED nemění, zkontroluj v `logs/worktracker-YYYYMMDD.log` hlášky z `LuxaforStatusIndicatorPlugin` a stav property `IsDeviceAvailable`.

Tímhle postupem ověříš obě důležité věci v jednom kroku:

- Že zařízení je připojené a plugin s ním umí komunikovat (jinak by `IsDeviceAvailable` zůstal `false`).
- Že barvy v konfiguraci vypadají tak, jak očekáváš při reálném přechodu fází.

---

## Časté chyby

| Symptom | Příčina | Řešení |
|---------|---------|--------|
| Plugin loaded, ale `IsDeviceAvailable = false` | Zařízení není připojené nebo uživatel nemá přístup k HID | Spusť **Test connection** — řekne, která z těch dvou možností to je. Na Linuxu zkontroluj udev rules. |
| `Luxafor device unavailable: ...` (Warning v logu) | Zařízení je zapojené, ale OS ho nepustil — chybějící udev pravidlo, macOS Input Monitoring, nebo ho drží jiná aplikace | Hláška obsahuje konkrétní důvod i hint pro danou platformu. Viz sekci **Ovladače**. |
| LED svítí, ale pořád stejnou barvou | Pomodoro neběží → plugin drží poslední stav; zavoláním `Start Pomodoro` se obnoví. Nebo konfigurace má všechny barvy stejné. | Zkontroluj konfiguraci a stav Pomodoro. |
| Bluetooth Pro se připojuje a hned odpojuje | Slabá baterie, interference, nebo spárování není dokončené | Nabij zařízení, znovu spáruj. |
| `DeviceNotFound` i když je zařízení zapojené | Konflikt s jiným ovladačem (např. stará verze Luxafor desktop app) | Zavři konkurenční aplikaci. |

---

## Co plugin **nedělá**

- **Neukládá barvy mimo `settings.json`** — žádná vlastní cache.
- **Nepoužívá síť** — čistě lokální HID.
- **Nereaguje na work entries přímo** — jen na Pomodoro fáze. Pokud chceš jinou logiku (např. červená, když běží tracking, zelená, když ne), by to vyžadovalo úpravu pluginu nebo další plugin s vlastním event hookem.

---

## Plugin config schéma (`appsettings.json`)

CLI samotné Luxafor nepoužívá (nemá Pomodoro), takže aktuálně nemá smysl plugin konfigurovat mimo GUI. Pokud bys psal/a vlastní host, který volá `IPluginManager.StatusIndicatorPlugins`, schéma vypadá takto:

```json
{
  "Plugins": {
    "luxafor.status-indicator": {
      "work_color": "#FF0000",
      "short_break_color": "#00FF00",
      "long_break_color": "#0000FF",
      "turn_off_on_startup": false
    }
  }
}
```

> **Pozor:** Samotná přítomnost configu v `appsettings.json` plugin **nezapne**. `IPluginManager.StatusIndicatorPlugins` vrací jen **enabled** pluginy; enabled stav se předává při bootstrapu přes `InitializePluginsAsync(..., enabledPlugins: new Dictionary<string, bool> { ["luxafor.status-indicator"] = true })`. Aktuální `WorkTracker.CLI` tuto mapu nepředává, takže ani s configem v souboru by Luxafor z CLI nefungoval. Plugin konfiguruj v GUI.
