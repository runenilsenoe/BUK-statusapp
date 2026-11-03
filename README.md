# BUK Status

Viser aksjonsstatus for grupper og lag av medlemmer, med data fra BCC Contributions API. Løsningen er en nettside nå, og en Telegram-bot kommer senere.

- **Grupper:** Frie samlinger av personer med ajour-status per person.
- **Lag (lagordning):** Hver person er på ett lag. Laget har lagleder og et tillegg på målet. Lagene kan importeres fra regnearket.
- **Månedsrapport:** Mål mot innsamlet per lag for valgt måned, akkumulert og for hele året, basert på månedsmålene.
- **Ukerapport:** Innsamlet per lag per ISO-uke mot ukebudsjett (årsmål / antall uker).

| Prosjekt | Innhold |
|---|---|
| `src/BukStatus.Core` | BCC Contributions-klient (OAuth client credentials), medlemsstatus og bidrag med cache, grupper, lag og rapportberegning i SQLite |
| `src/BukStatus.Web` | Blazor Server-nettside og JSON-API (`/api/groups`, `/api/teams`), beskyttet med BCC-login og API-nøkler |
| `tools/BukStatus.ApiCheck` | Sjekker at API-nøklene virker |
| `tests/` | Enhets- og integrasjonstester |
| `PACEFEST`, `MyBlazorProgressApp` | Eldre statusskjermer mot myshare. Krever egen `Myshare`-konfigurasjon. |

## Kjøre lokalt

```bash
dotnet run --project src/BukStatus.Web
```

I Development brukes testdata (`BccApi:UseFakeData`), og du logges inn automatisk som utvikler. Ekte nøkler legges inn med `dotnet user-secrets`, aldri i `appsettings.json`.

## Utrulling

Se [DEPLOY.md](DEPLOY.md) for Coolify.
