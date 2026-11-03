# BUK Status

Viser aksjonsstatus for grupper av medlemmer, med data fra BCC Contributions API. Løsningen er en nettside nå, og en Telegram-bot kommer senere.

| Prosjekt | Innhold |
|---|---|
| `src/BukStatus.Core` | BCC Contributions-klient (OAuth client credentials), medlemsstatus med cache, grupper i SQLite |
| `src/BukStatus.Web` | Blazor Server-nettside og JSON-API (`/api/groups`), beskyttet med BCC-login og API-nøkler |
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
