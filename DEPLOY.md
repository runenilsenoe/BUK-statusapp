# Utrulling med Coolify

Websiden (`src/BukStatus.Web`) kjøres som én container. Coolify bygger den fra [`docker-compose.yaml`](docker-compose.yaml) og [`Dockerfile`](Dockerfile), og Coolify sin proxy (Traefik) tar seg av HTTPS.

## 1. Legg koden i et Git-repo

Coolify henter koden fra Git. Push prosjektet til et privat repo (GitHub, GitLab eller Gitea), og koble det til Coolify under **Sources**: GitHub App for GitHub, deploy key for andre.

## 2. Opprett ressursen

1. **Project → New → Resource**, og velg repoet og branchen.
2. **Build Pack:** `Docker Compose`, **Docker Compose Location:** `/docker-compose.yaml`.
3. Under tjenesten **web**: sett **Domains** til `https://status.<ditt-domene>:8080`. `:8080` forteller Coolify hvilken port i containeren trafikken skal til. Brukerne ser bare `https://status.<ditt-domene>`.

## 3. Miljøvariabler (Environment Variables)

Alle hemmeligheter legges inn her, aldri i `appsettings.json`. Kryss av for **Is Secret** på nøklene.

| Variabel | Påkrevd | Innhold |
|---|---|---|
| `BCC_API_CLIENT_ID` | ja | Client ID for Contributions-API-et |
| `BCC_API_CLIENT_SECRET` | ja | Client secret for Contributions-API-et |
| `BCC_API_ENVIRONMENT` | nei | `prod` (standard) eller `sandbox` |
| `BCC_API_USE_FAKE_DATA` | nei | `true` gir testdata i stedet for BCC-data. Nyttig før API-nøklene er på plass (sett da dummyverdier over). |
| `AUTH_CLIENT_ID` | ja | Client ID for BCC-login (OIDC) |
| `AUTH_CLIENT_SECRET` | ja | Client secret for BCC-login |
| `AUTH_ALLOWED_PERSON_ID_1` | ja | Din BCC person-ID. Flere personer: se under. |
| `API_KEY_BOT` | nei | Nøkkel for boten/skript mot `/api` (header `X-Api-Key`). Lag en med `openssl rand -hex 32`. |

**Flere personer med tilgang:** Legg til variablene `Auth__AllowedPersonIds__1`, `Auth__AllowedPersonIds__2` og så videre direkte i Coolify, med person-ID som verdi. Redeploy etterpå.

Appen nekter å starte i produksjon hvis BCC-login mangler. Slik unngår vi at medlemsdata blir liggende åpent.

## 4. BCC-login: adresser som må registreres hos BCC

Be it@bcc.no registrere følgende for login-klienten:

- **Allowed Callback URL:** `https://status.<ditt-domene>/signin-auth0`
- **Allowed Logout URL:** `https://status.<ditt-domene>/`

Adressen må være nøyaktig den samme som domenet i Coolify, med `https`.

## 5. Lagring og backup

- Databasen (`bukstatus.db`) og nøklene for innloggingscookien ligger i volumet `bukstatus-data`, montert på `/app/data`. Volumet overlever nye utrullinger.
- Coolify tar ikke automatisk backup av slike volumer. Ta en jevnlig kopi av `/app/data`, for eksempel med en cron-jobb på serveren som kopierer volumet til en annen disk.

## 6. Lokal server og HTTPS

- BCC-login krever HTTPS. Coolify henter sertifikat fra Let's Encrypt automatisk, men da må domenet peke til serveren og være nåbart fra internett på port 80/443.
- Hvis serveren bare skal være tilgjengelig på det lokale nettet, trenger du et sertifikat på en annen måte. Det kan være DNS-utfordring i Traefik eller en tunnel som Cloudflare Tunnel eller Tailscale Funnel.
- Serveren må kunne nå `login.bcc.no` og `api.bcc.no` ut på internett. BCC trenger ikke å kunne nå serveren, for innloggingen går via brukerens nettleser.

## 7. Sjekk at det virker

1. `https://status.<ditt-domene>/healthz` skal svare `Healthy`. Coolify bruker også dette som health check.
2. Gå til forsiden. Du skal sendes til login.bcc.no og tilbake igjen.
3. Hvis du kommer til «Ingen tilgang», står person-ID-en din der. Legg den inn i `AUTH_ALLOWED_PERSON_ID_1`.
4. API-nøklene kan testes lokalt med verktøyet `tools/BukStatus.ApiCheck` (se kommentaren øverst i `Program.cs`).

## Teste containeren lokalt

```bash
docker build -t bukstatus-web .
```

```bash
docker run --rm -p 8080:8080 -e BccApi__UseFakeData=true -e BccApi__ClientId=x -e BccApi__ClientSecret=x -e Auth__ClientId=x -e Auth__ClientSecret=x -e Auth__AllowedPersonIds__0=1 -e Auth__ApiKeys__0=test bukstatus-web
```

Uten ekte BCC-login kommer du ikke forbi innloggingen på websiden, men `/healthz` og `/api` (med `X-Api-Key: test`) fungerer.
