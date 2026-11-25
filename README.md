# BUK Status

Viser aksjonsstatus for grupper og lag av medlemmer, med data fra BCC Contributions API. Løsningen er en nettside nå, og en Telegram-bot kommer senere.

- **Grupper:** Frie samlinger av personer med ajour-status per person.
- **Lag (lagordning):** Hver person er på ett lag. Laget har lagleder og et tillegg på målet. Lagene kan importeres fra regnearket.
- **Månedsrapport:** Mål mot innsamlet per lag for valgt måned, akkumulert og for hele året, basert på månedsmålene.
- **Ukerapport:** Innsamlet per lag per ISO-uke mot ukebudsjett (årsmål / antall uker).
- **Oversikt (`/oversikt`):** Viser BUK-ajour, Samvirk og samlet score, med en stolpe per person. Ekstrabidrag hentes automatisk fra klubbens transaksjoner og består av alle bidrag fra person-ID-er uten bidragsplan, inkludert refusjoner og transaksjoner uten person-ID. Lagtilhørighet og «Counted in aksjon» endrer ikke denne avgrensningen. Perioden er som standard 1. januar til i dag. Simulert ekstrapott legges oppå det faktiske beløpet under `/settings/oversikt`, der også milepælene settes. BUK-ajourgrensen kan overstyres direkte på Oversikt. Samvirk-prosenten beregnes fra aktive deltakeres coins i siste bekreftede eksport, med manuelt prosenttall som reserve uten import. Oversikten kan avgrenses til et lag eller en gruppe, oppdateres hvert 5. sekund og har storskjermmodus. Den valgfrie åpne skjermen `/skjerm` viser bare totaler og anonyme stolper.

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
