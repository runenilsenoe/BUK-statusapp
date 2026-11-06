# BUK Status

Viser aksjonsstatus for grupper og lag av medlemmer, med data fra BCC Contributions API. Løsningen er en nettside nå, og en Telegram-bot kommer senere.

- **Grupper:** Frie samlinger av personer med ajour-status per person.
- **Lag (lagordning):** Hver person er på ett lag. Laget har lagleder og et tillegg på målet. Lagene kan importeres fra regnearket.
- **Månedsrapport:** Mål mot innsamlet per lag for valgt måned, akkumulert og for hele året, basert på månedsmålene.
- **Ukerapport:** Innsamlet per lag per ISO-uke mot ukebudsjett (årsmål / antall uker).
- **Pott (`/pot`):** Statusskjermen fra PACEFEST og MyBlazorProgressApp. Viser BUK-ajour, Samvirk og samlet score mot målene (f.eks. 70 og 75 %), med en stolpe per person. Ekstrabidrag (pott) fordeles på dem som ikke er ajour, likt eller slik at flest mulig blir ajour, og du kan simulere en større pott. Siden gjelder alle lag, ett lag eller én gruppe, oppdaterer seg hvert 30. sekund, har storskjermmodus og kan spille lyd når noen blir ajour. Samvirk-prosent legges inn under `/settings/pot`. Ekstrabidragene hentes automatisk fra BCC-API-et (bidrag til en org-ID i en periode, filtrert på beskrivelse og uten medlemmer på lag og i grupper), med myshare (bearer-token) som reserve og et manuelt beløp som siste utvei. De kan også legges inn bare manuelt.

| Prosjekt | Innhold |
|---|---|
| `src/BukStatus.Core` | BCC Contributions-klient (OAuth client credentials), medlemsstatus og bidrag med cache, grupper, lag og rapportberegning i SQLite |
| `src/BukStatus.Web` | Blazor Server-nettside og JSON-API (`/api/groups`, `/api/teams`, `/api/pot`), beskyttet med BCC-login og API-nøkler |
| `tools/BukStatus.ApiCheck` | Sjekker at API-nøklene virker |
| `tests/` | Enhets- og integrasjonstester |
| `PACEFEST`, `MyBlazorProgressApp` | Eldre statusskjermer mot myshare. Erstattet av Pott-siden (myshare-hentingen ligger nå i `BukStatus.Core/Pot`) og kan fjernes. |

## Kjøre lokalt

```bash
dotnet run --project src/BukStatus.Web
```

I Development brukes testdata (`BccApi:UseFakeData`), og du logges inn automatisk som utvikler. Ekte nøkler legges inn med `dotnet user-secrets`, aldri i `appsettings.json`.

## Utrulling

Se [DEPLOY.md](DEPLOY.md) for Coolify.
