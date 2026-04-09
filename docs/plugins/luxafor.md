# Plugin: Luxafor LED

| Plugin ID | `luxafor.status-indicator` |
|-----------|----------------------------|
| Třída | `LuxaforStatusIndicatorPlugin` |
| Rozhraní | `IStatusIndicatorPlugin` |
| Hardware | Luxafor Bluetooth Pro / Flag / Orb |
| Knihovna | `DotLuxafor` (NuGet) |
| API | `ILuxaforDeviceManager` → `ILuxaforDevice` (`SetColorAsync`, `TurnOffAsync`) |

Plugin ovládá fyzický LED indikátor Luxafor a přepíná jeho barvu podle aktuální fáze Pomodoro timeru. Cílem je okamžité vizuální signalizování stavu pro sebe i okolí („jsem v zóně, nerušit” vs. „mám pauzu”).

---

## Konfigurační pole

| Pole | Typ | Povinné | Default | Popis |
|------|-----|---------|---------|-------|
| `work_color` | `Text` | ❌ | `#FF0000` (červená) | Barva během fáze **Work** |
| `short_break_color` | `Text` | ❌ | `#00FF00` (zelená) | Barva během krátké pauzy |
| `long_break_color` | `Text` | ❌ | `#0000FF` (modrá) | Barva během dlouhé pauzy |

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

Všechna zařízení komunikují přes **HID API**. Plugin používá NuGet knihovnu [`DotLuxafor`](https://www.nuget.org/packages/DotLuxafor/), která nabízí vysokoúrovňové API `ILuxaforDeviceManager` / `ILuxaforDevice` specifické pro Luxafor protokol — plugin sám žádnou HID logiku neimplementuje.

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

1. `OnInitializeAsync` načte barvy z konfigurace (s fallbackem na defaulty) a provede validaci hex formátu. Zařízení se v tomto kroku **neotevírá**.
2. Teprve při prvním volání `SetStateAsync` plugin zavolá interní helper `GetOrOpenDevice()`, který přes `ILuxaforDeviceManager.TryOpen()` otevře HID connection k aktuálně připojenému Luxaforu.
3. Pokud zařízení v tu chvíli **není** připojené, `_device` zůstane `null` a volání je no‑op; další pokus proběhne při příštím `SetStateAsync`.

### Reakce na Pomodoro

Avalonia/WPF aplikace si předplatí `IPomodoroService.PhaseChanged`. Při eventu:

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
2. Přes `GetOrOpenDevice()` získá otevřený `ILuxaforDevice` (nebo ho lazy otevře, pokud jím plugin ještě nedisponuje).
3. Podle stavu zavolá buď `device.SetColorAsync(color)` s barvou odpovídající fázi, nebo `device.TurnOffAsync()` pro `Idle`.
4. Uvolní semafor.

### Thread safety

Ačkoli WorkTracker v běžném provozu nevolá `SetStateAsync` z více vláken, plugin je napsaný defenzivně pro případ že:

- Aplikace zpracovává víc event sourců zároveň (Pomodoro + ruční trigger).
- V budoucnu plugin dostane vlastní watchdog thread.

---

## `IsDeviceAvailable`

Property `IsDeviceAvailable` vrací `true`, když má plugin otevřené spojení s reálným zařízením (`_device` je non-null a `IsConnected`).

Při selhání operace v `SetStateAsync` plugin zařízení zavře a další volání se pokusí o znovupřipojení přes `GetOrOpenDevice()`. V aktuální implementaci **není počítaný limit** reconnect pokusů — plugin bude při každém dalším volání zkoušet reconnect znovu, dokud se nepodaří nebo dokud zařízení nezmizí nadobro.

---

## Ověření funkčnosti

`StatusIndicatorPluginBase` **nedědí** `ITestablePlugin`, takže se pro Luxafor v Settings UI nezobrazí tlačítko **Test connection** (to je dostupné jen pro worklog a suggestion pluginy). Žádná samostatná Preview akce v aktuálním UI také není.

Funkčnost pluginu proto ověř prakticky:

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
| Plugin loaded, ale `IsDeviceAvailable = false` | Zařízení není připojené nebo uživatel nemá přístup k HID | Připoj zařízení. Na Linuxu zkontroluj udev rules. |
| `AccessDeniedException` v logu | Linux bez udev rules | Viz sekci **Ovladače → Linux**. |
| LED svítí, ale pořád stejnou barvou | Pomodoro neběží → plugin drží poslední stav; zavoláním `Start Pomodoro` se obnoví. Nebo konfigurace má všechny barvy stejné. | Zkontroluj konfiguraci a stav Pomodoro. |
| Bluetooth Pro se připojuje a hned odpojuje | Slabá baterie, interference, nebo spárování není dokončené | Nabij zařízení, znovu spáruj. |
| `DeviceNotFound` i když je zařízení zapojené | Konflikt s jiným ovladačem (např. stará verze Luxafor desktop app) | Zavři konkurenční aplikaci. |

---

## Co plugin **nedělá**

- **Neukládá barvy mimo `settings.json`** — žádná vlastní cache.
- **Nepoužívá síť** — čistě lokální HID.
- **Nereaguje na work entries přímo** — jen na Pomodoro fáze. Pokud chceš jinou logiku (např. červená, když běží tracking, zelená, když ne), by to vyžadovalo úpravu pluginu nebo další plugin s vlastním event hookem.

---

## Fallback: appsettings.json pro CLI

CLI samo Luxafor nepoužívá (nemá Pomodoro), ale pokud bys napsal/a vlastní skript, který volá `IPluginManager.StatusIndicatorPlugins`, konfigurace by vypadala:

```json
{
  "Plugins": {
    "luxafor.status-indicator": {
      "work_color": "#FF0000",
      "short_break_color": "#00FF00",
      "long_break_color": "#0000FF"
    }
  }
}
```
