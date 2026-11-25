# Plugin: MCP Server

| Plugin ID | `mcp.server` |
|-----------|--------------|
| Třída | `McpPlugin` |
| Rozhraní | `IRemoteControlPlugin` |
| Transport | Streamable HTTP na `http://localhost:<port>/mcp` |

Plugin zpřístupní běžící WorkTracker AI asistentům, jako je Claude Code, přes [Model Context Protocol](https://modelcontextprotocol.io/). Asistent umí zjistit, co běží, spustit a zastavit práci, přidat záznam, opravit popis nebo ticket a, pokud to povolíš, odeslat worklogy.

Server běží v GUI a jen se zapnutým pluginem. CLI plugin sice načte, ale server nespustí. Změny jdou přes běžící aplikaci, takže se hned projeví v okně i na Stream Decku.

---

## Nástroje

| Nástroj | Co dělá |
|---------|---------|
| `get_status` | Aktuální čas, běžící práce, probíhající záznam s pevným časem, poslední práce a odpracovaný čas |
| `list_work_entries` | Záznamy za zvolené dny |
| `get_worked_time` | Odpracovaný čas za den nebo týden |
| `get_suggestions` | Dnešní návrhy ze zapnutých pluginů, sdílené s dialogem návrhů včetně cache |
| `get_favorites` | Oblíbené položky |
| `start_work`, `stop_work`, `resume_last_work` | Spuštění, zastavení a navázání na poslední práci. Navázat jde, jen když se nic nesleduje |
| `add_work_entry` | Přidá záznam s časem. Běžící práci, která začala dřív a překrývá se s ním, ukončí na jeho začátku. Při jiném překryvu selže |
| `update_work_entry` | Nahradí ticket a popis záznamu, časy nemění. Vynechaná hodnota se smaže |
| `list_worklog_providers` | Zapnuté pluginy pro odesílání a jejich režimy |
| `preview_worklogs` | Náhled toho, co by se odeslalo |
| `submit_worklogs` | Odešle den nebo týden. Jen se zapnutým **Allow submitting worklogs** |

Mazání záznamů ani změna jejich časů přes MCP nejde.

Výsledek akcí WorkTracker oznamuje stejně jako u ostatních externích ovladačů, viz [Notifikace](../user-guide.md#notifikace).

## Konfigurace

| Pole | Klíč | Výchozí | Poznámka |
|------|------|---------|----------|
| Port | `port` | `47821` | 1024–65535 |
| Access token | `access_token` | náhodný | Povinný, aspoň 16 znaků bez mezer. Nastavení ho předvyplní náhodnou hodnotou a platí od uložení |
| Allow submitting worklogs | `allow_worklog_submission` | vypnuto | Bez něj se `submit_worklogs` klientovi vůbec nenabídne |

Když je port obsazený, WorkTracker při startu nebo uložení nastavení ukáže chybu. Změň port a uprav adresu i v klientovi.

## Připojení klienta

V **Nastavení → Pluginy → MCP Server** je pod konfigurací sekce **Údaje pro připojení**: kompletní adresa a hlavička s tokenem, obojí s tlačítkem pro zkopírování (hlavička se jen kopíruje, nezobrazuje se). Ukazuje hodnoty z formuláře, server je začne používat až po uložení se zapnutým pluginem.

Claude Code:

```
claude mcp add --transport http worktracker http://localhost:47821/mcp --header "Authorization: Bearer <token>"
```

Claude Desktop (**Settings → Developer → Edit Config**) se k lokálnímu HTTP serveru připojí přes můstek [`mcp-remote`](https://www.npmjs.com/package/mcp-remote), který potřebuje Node.js:

```json
{
  "mcpServers": {
    "worktracker": {
      "command": "npx",
      "args": ["-y", "mcp-remote", "http://localhost:47821/mcp", "--transport", "http-only", "--header", "Authorization:${AUTH_HEADER}"],
      "env": { "AUTH_HEADER": "Bearer <token>" }
    }
  }
}
```

Za dvojtečkou v `Authorization:${AUTH_HEADER}` mezera být nesmí, na Windows se argumenty s mezerou mohou rozbít. Po uložení Claude Desktop úplně ukonči a spusť znovu. `mcp-remote` je komunitní balíček a token prochází přes něj.

VS Code (`.vscode/mcp.json` nebo uživatelská konfigurace):

```json
{
  "servers": {
    "worktracker": {
      "type": "http",
      "url": "http://localhost:47821/mcp",
      "headers": { "Authorization": "Bearer ${input:worktracker-token}" }
    }
  },
  "inputs": [{ "id": "worktracker-token", "type": "promptString", "password": true, "description": "WorkTracker MCP token" }]
}
```

## Bezpečnost

- Server poslouchá jen na `localhost` a každý požadavek musí mít token. Požadavky z cizích webových stránek (hlavička `Origin`) odmítá.
- Kdo zná token, může v tvém účtu na tomto počítači číst a měnit záznamy. Klient ho ukládá do své konfigurace, nesdílej ji.
- Názvy událostí z kalendáře a tickety z Jiry se dostanou k modelu. Nástrojům, které něco mění, proto v klientovi nepovoluj automatické schvalování, hlavně ne `submit_worklogs`: odeslaný worklog nejde vzít zpět a opakované odeslání může vytvořit duplicity.
