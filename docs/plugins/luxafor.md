# Plugin: Luxafor LED

| Plugin ID | `luxafor.status-indicator` |
|-----------|----------------------------|
| Třída | `LuxaforStatusIndicatorPlugin` |
| Rozhraní | `IStatusIndicatorPlugin` |
| Hardware | Luxafor Bluetooth Pro / Flag / Orb |
| Komunikace | HID přes `Luxafor.HidSharp` |

Plugin ovládá fyzický LED indikátor Luxafor a přepíná jeho barvu podle aktuální fáze Pomodoro timeru. Cílem je okamžité vizuální signalizování stavu pro sebe i okolí („jsem v zóně, nerušit" vs. „mám pauzu").

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

Fáze `Idle` (Pomodoro neběží) LED **zhasne** — plugin pošle `#000000`.

---

## Požadavky na hardware

Podporovaná zařízení:

- **Luxafor Bluetooth Pro** (BT4.0) — doporučené, bezdrátové, ale vyžaduje Bluetooth párování.
- **Luxafor Flag** (USB) — pohodlné pro stacionární setup.
- **Luxafor Orb** (USB).

Všechna zařízení komunikují přes **HID API**. Plugin používá knihovnu `Luxafor.HidSharp`, která je ve stromě jako samostatný projekt `src/Luxafor.HidSharp/`.

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

1. `OnInitializeAsync` zkusí otevřít HID connection přes `Luxafor.HidSharp`.
2. Pokud zařízení **není** připojené, plugin zůstane inicializovaný, ale `IsDeviceAvailable => false`. Aplikace bude volat `SetStateAsync` jako no‑op.
3. Pokud zařízení je připojené, načte barvy z konfigurace (s fallbackem na defaulty) a je připraven.

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

1. Přeloží `StatusIndicatorState` na hex barvu podle konfigurace.
2. Zamkne `SemaphoreSlim` (HID operace **nesmí** běžet paralelně — zařízení zamrzne).
3. Pošle HID command s RGB payloadem.
4. Uvolní semafor.

### Thread safety

Ačkoli WorkTracker v běžném provozu nevolá `SetStateAsync` z více vláken, plugin je napsaný defenzivně pro případ že:

- Aplikace zpracovává víc event sourců zároveň (Pomodoro + ruční trigger).
- V budoucnu plugin dostane vlastní watchdog thread.

---

## `IsDeviceAvailable`

Property `IsDeviceAvailable` vrací `true`, když:

- Plugin je inicializovaný.
- HID connection je otevřená.
- Poslední `SetStateAsync` neselhal s fatal I/O chybou.

Při selhání HID operace plugin zkusí reconnectovat při dalším `SetStateAsync`. Pokud reconnect 3× selže, zůstane v `IsDeviceAvailable => false` až do restartu aplikace.

---

## Test connection

`StatusIndicatorPluginBase` **nedědí** `ITestablePlugin` (indicator pluginy nemají „externí službu" na otestování). Místo toho Settings UI nabízí tlačítko **Preview** (pokud je plugin enabled), které postupně rozsvítí LED ve všech třech fázových barvách + idle, každou na ~1 sekundu, a umožní ti tak ověřit:

- Že zařízení je připojené.
- Že barvy v tvé konfiguraci vypadají tak, jak chceš.

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

## Knihovna `Luxafor.HidSharp`

`src/Luxafor.HidSharp/` je vlastní knihovna projektu, která obaluje `HidSharp` (NuGet) s vyšším API specifickým pro Luxafor protokol. Cíle: `netstandard2.0` + `net8.0` pro použití i mimo WorkTracker.

Plugin knihovnu referencuje přes `<ProjectReference>` a kopíruje její DLL do své publish složky. Hlavní aplikace `HidSharp` nevidí (je v plugin izolaci).

### Vlastnost: standalone použití

Protože knihovna je multi‑target (`netstandard2.0`), dá se použít i z jiných projektů — například .NET Framework 4.8. Uvažujeme o jejím vyčlenění do samostatného repozitáře a publikaci na NuGet. Do té doby je její verze vázaná na WorkTracker release cyklus.

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
