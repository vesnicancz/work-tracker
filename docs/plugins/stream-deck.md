# Plugin: Stream Deck

| Plugin ID | `streamdeck.remote-control` |
|-----------|-----------------------------|
| Třída | `StreamDeckPlugin` |
| Rozhraní | `IRemoteControlPlugin` |
| Hardware | Elgato Stream Deck + (ovladač a dotykový pruh) |
| Druhá strana | Plugin pro aplikaci Stream Deck v `streamdeck-plugin/` (UUID `com.vesnicancz.worktracker`) |
| Stream Deck | 7.1 a novější |

Integrace má dvě části. **WorkTracker plugin** běží v GUI a na named pipe zpřístupňuje stav a příkazy aplikace. **Stream Deck plugin** běží v aplikaci Stream Deck, kreslí na dotykový pruh a posílá příkazy. CLI plugin sice načte, ale pipe neotevře: stav i Pomodoro žijí jen v GUI.

---

## Akce

### Work (ovladač)

| Stav | Dotykový pruh | Stisk ovladače / klepnutí na pruh |
|------|---------------|-----------------------------------|
| Práce běží | Ticket (nebo popis) a uplynulý čas | Zastaví práci |
| Nic neběží | Poslední práce a `Stopped` | Znovu spustí poslední práci |
| Žádná práce v nastaveném limitu | `WorkTracker` a `No work` | Ukáže varování na pruhu |
| WorkTracker neběží nebo má plugin vypnutý | `WorkTracker` a `Offline` | Ukáže varování na pruhu |

Pruh se překreslí sám, jakmile se stav změní kdekoli, například v okně aplikace.

WorkTracker navíc výsledek akcí ze Stream Decku oznamuje sám, jako toast v aktivním hlavním okně, jinak jako systémovou notifikaci. Potvrzení jde vypnout v **Nastavení → Obecné → Oznámení**, chyby se ukazují vždy. Viz [Notifikace](../user-guide.md#notifikace).

**Otáčení ovladače** opravuje začátek běžící práce, o minutu na jeden krok. Po směru hodinových ručiček čas přidává (začátek se posouvá dřív), proti směru ubírá. Pruh ukazuje výsledek hned, do WorkTrackeru se změna uloží chvíli poté, co se ovladač zastaví. Začátek nejde posunout do budoucnosti. Pokud by se práce překryla s jiným záznamem, pruh ukáže varování a vrátí původní čas. Když žádná práce neběží, otáčení nic nedělá.

### Návrhy (tlačítka)

Tlačítko **Suggestions** přepne celé zařízení na profil WorkTracker a načte dnešní návrhy ze všech zapnutých suggestion pluginů. Výsledky sdílí s dialogem návrhů v aplikaci, včetně jeho pětiminutové cache.

| Tlačítko | Co ukazuje | Stisk |
|----------|------------|-------|
| **Suggestion** | Čas události nebo ticket a pod ním název, běžící práce zeleně, skončená událost ztlumeně | Událost vloží s jejím časem od–do, ticket spustí od aktuálního času, už běžící práci zastaví. Pak se vrátí na předchozí profil. |
| **Next suggestions** | Stránku, zdroje, které selhaly, a `Loading`, `None` nebo `Failed` | Další stránka, po poslední zase první |
| **Close suggestions** | Šipku zpět | Vrátí se na předchozí profil, nic nespustí |

Seznam je společný pro všechny pluginy: nejdřív události, které ještě neskončily, podle času, potom skončené události od nejnovější a nakonec položky bez času v pořadí pluginů. Ticket, popis a časy se berou z návrhu stejně jako v dialogu a běžící práce začatá před událostí skončí na jejím začátku. Pokud by vložení změnilo jiné záznamy, aplikace se v dialogu ptá, tady se nemá kde zeptat: tlačítko ukáže varování, nic se nezmění a událost je potřeba vložit v aplikaci. Velikost stránky se řídí počtem tlačítek **Suggestion** v profilu a pořadí jejich pozicí, po řádcích zleva doprava. Vyhledávat na Stream Decku nejde.

### Oblíbené (tlačítka)

Tlačítko **Favorites** přepne celé zařízení na profil WorkTracker Favorites a načte oblíbené položky z Nastavení v tamním pořadí. Seznam se čte při každém otevření profilu, změna v Nastavení se tedy na už otevřeném profilu neprojeví.

| Tlačítko | Co ukazuje | Stisk |
|----------|------------|-------|
| **Favorite** | Ticket, pokud ho položka má, a pod ním její název, běžící práce zeleně, šablona fialově | Spustí práci s ticketem a popisem položky, už běžící práci zastaví. Šablona otevře v aplikaci dialog nového záznamu s předvyplněnými hodnotami. Pak se vrátí na předchozí profil. |
| **Next favorites** | Stránku a `Loading`, `None` nebo `Failed` | Další stránka, po poslední zase první |
| **Close favorites** | Šipku zpět | Vrátí se na předchozí profil, nic nespustí |

Na rozdíl od nabídky v tray ikoně stisk už běžící položku zastaví, stejně jako u návrhů. Za běžící se položka považuje, když se shoduje ticket i popis práce, šablona nikdy. Dialog se otevře jako z tray ikony, a pokud už je otevřený, jen se přenese dopředu a zůstanou v něm jeho hodnoty.

---

## Konfigurační pole

| Pole | Typ | Povinné | Default | Popis |
|------|-----|---------|---------|-------|
| `resume_window_days` | `Number` | ❌ | `7` | Kolik dní zpět může stisk ovladače obnovit poslední práci, celé číslo 1 až 999. Starší práce se nenabízí, aby stisk nespustil dávno zapomenutý ticket. |

Limit se počítá od začátku poslední práce. Po uložení v Nastavení se projeví hned, i na už připojeném pruhu.

---

## Instalace

1. Stáhni z releasu `WorkTracker-Plugin-StreamDeck-{verze}.zip` a rozbal ho do adresáře s pluginy (viz [README](../../README.md)).
2. Povol **Stream Deck** v **Nastavení → Pluginy**.
3. V rozbalené složce otevři dvojklikem `WorkTracker-StreamDeck-{verze}.streamDeckPlugin`, tím se nainstaluje do aplikace Stream Deck.
4. V aplikaci Stream Deck přetáhni akci **WorkTracker → Work** na ovladač a **WorkTracker → Suggestions** a **WorkTracker → Favorites** na tlačítka.
5. Při prvním stisku **Suggestions** a **Favorites** potvrď instalaci profilu. Profily jsou určené pro Stream Deck + a nejdou upravovat.

Na macOS zatím integrace na zařízení ověřená není, vyzkoušená je jen na Windows.

## Vývoj

Testy spouštějí TypeScript přímo, použij proto Node.js 24 jako CI (manifest pro běh pluginu uvádí Node.js 20).

```bash
cd streamdeck-plugin
npm ci
npm test
npm run build
npx streamdeck link com.vesnicancz.worktracker.sdPlugin   # jednou, napojí složku do aplikace Stream Deck
npm run watch                                             # build a restart pluginu při každé změně
```

WorkTracker plugin se v Debug buildu načítá z `plugins/WorkTracker.Plugin.StreamDeck/bin/Debug/net10.0`. Povolit ho je potřeba i tady, protože Debug běh má vlastní nastavení.

Logy Stream Deck pluginu jsou v `streamdeck-plugin/com.vesnicancz.worktracker.sdPlugin/logs/`.

Přibalené profily jsou v `com.vesnicancz.worktracker.sdPlugin/profiles/`. `WorkTracker.streamDeckProfile` vzniká exportem z aplikace Stream Deck. `WorkTrackerFavorites.streamDeckProfile` je jeho kopie s novými UUID profilu, stránek a akcí, ve které jsou akce návrhů nahrazené akcemi oblíbených. Změnu rozložení je tedy potřeba udělat v obou. Pomocné akce jsou v manifestu skryté přes `VisibleInActionsList`, takže pro novou úpravu rozložení je potřeba je dočasně zapnout. Export je ZIP: než ho přidáš do repa, zkontroluj v manifestu profilu klíč `AppIdentifier`. Pokud je profil navázaný na aplikaci, je v něm absolutní cesta z tvého počítače a musí se odstranit.

---

## Protokol

Named pipe `worktracker-streamdeck` (na macOS a Linuxu socket `worktracker-streamdeck.sock` v dočasném adresáři), přístupná jen aktuálnímu uživateli. Klient je vždy jeden, nový se připojí, až se ten předchozí odpojí. Zprávy jsou JSON objekty, jeden na řádek.

Protokol je popsaný na dvou místech, která je potřeba udržovat v souladu: `RemoteControlProtocol.cs` a `streamdeck-plugin/src/protocol.ts`. Hlídají je ukázkové zprávy v `streamdeck-plugin/test/fixtures/protocol`, které čtou testy obou stran. Změna, kterou druhá strana nezvládne, zvyšuje číslo verze protokolu (`Version` v `RemoteControlProtocol.cs`, `PROTOCOL_VERSION` v `client.ts`). Server ji posílá ve zprávě se stavem a klient jiné číslo odmítne s hláškou, že je potřeba aktualizovat obě části.

Načítání návrhů běží mimo čtení příkazů, takže stisk ovladače na něj nečeká. Odpovědi se k příkazům párují podle `id` a můžou přijít v jiném pořadí.

**Příkaz od klienta:**

```json
{"id":1,"command":"shiftStart","minutes":-5}
```

| Příkaz | Argumenty | Akce |
|--------|-----------|------|
| `toggleWork` | | Zastaví běžící práci, nebo znovu spustí poslední |
| `startWork` | `ticketId`, `description` | Spustí novou práci |
| `stopWork` | | Zastaví práci |
| `resumeWork` | | Znovu spustí poslední práci |
| `shiftStart` | `minutes` | Posune začátek běžící práce, kolize s jiným záznamem skončí chybou |
| `addWork` | `ticketId`, `description`, `startTime`, `endTime` | Vloží záznam s daným časem (`startTime` je povinný, bez `endTime` práce běží dál), běžící práce začatá před ním skončí na jeho začátku, každá jiná kolize skončí chybou |
| `getSuggestions` | | Vrátí dnešní návrhy v poli `suggestions` odpovědi |
| `getFavorites` | | Vrátí oblíbené položky v poli `favorites` odpovědi |
| `showNewWorkDialog` | `ticketId`, `description` | Otevře v aplikaci dialog nového záznamu s těmito hodnotami, odpověď nečeká na jeho zavření |
| `startPomodoro`, `stopPomodoro`, `skipPomodoro` | | Ovládá Pomodoro, start selže, když je Pomodoro v Nastavení vypnuté |

Neznámý příkaz vrátí chybu `Unknown command '…'`.

**Odpověď na příkaz:**

```json
{"type":"result","id":1,"success":false,"error":"..."}
```

**Stav**, posílaný po připojení a při každé změně:

```json
{
  "type": "state",
  "activeWork": { "ticketId": "PROJ-1", "description": "Coding", "startedAt": "2026-11-04T09:30:00+01:00" },
  "lastWork": null,
  "pomodoro": { "phase": "shortBreak", "remainingSeconds": 90, "completedPomodoros": 2, "pomodorosBeforeLongBreak": 4 }
}
```

Plynutí času se neposílá, klient si ho počítá sám.

Ovladač Work používá `toggleWork` a `shiftStart`, tlačítka návrhů `getSuggestions`, `addWork`, `startWork` a `stopWork`, tlačítka oblíbených `getFavorites`, `showNewWorkDialog`, `startWork` a `stopWork`. Ostatní příkazy a stav Pomodora jsou připravené pro další akce.
