# Utrulling med Coolify

Websiden (`src/BukStatus.Web`) kjøres som én container. Coolify bygger den fra [`docker-compose.yaml`](docker-compose.yaml) og [`Dockerfile`](Dockerfile), og Coolify sin proxy (Traefik) tar seg av HTTPS.

**Slik kjører den i dag:** ressursen `buk-status-web` i prosjektet «BUK Telegram» (production) på https://buk.bttr.no. Kilden er GitHub-appen `simplrserver-app`, og Auto deploy står på «Deploy on push (webhooks)». Hver push til `main` ruller altså ut automatisk.

## 1. Legg koden i et Git-repo

Coolify henter koden fra Git. Koble repoet til Coolify under **Sources**: GitHub App for GitHub (gir webhooks og automatisk utrulling ved push), deploy key for andre. Et offentlig repo kan også brukes uten app, men da må hver utrulling startes manuelt.

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
| `MYSHARE_BASE_URL`, `MYSHARE_BEARER`, `MYSHARE_CLUB_ID` | nei | Reserve for ekstrabidrag på Pott-siden hvis BCC-API-et ikke svarer. Bearer-tokenet er en hemmelighet. |

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

## Klubbtilgang og personvalg fra MyShare

`MYSHARE_CLUB_ACCESS_ENABLED=true` aktiverer tilgang via `/Teams?userId=<innlogget BCC-person-ID>`.
Alle klubber fra `/Clubs/<teamId>` blir tilgjengelige, uten krav om at brukeren står i klubbens `members`.
Personvalg og klubboversikt bruker hele medlemslisten derfra, med navn og alder. Fødselsdato lagres ikke i
lokale lag eller grupper, og returneres ikke fra medlems-API-et; alder beregnes i norsk tid.

Konfigurer og test MyShare-tilgangen før aktivering:

- `MYSHARE_BASE_URL=https://myshare-admin-api-prod-2ymy6hgl7q-ew.a.run.app/api/`
- `MYSHARE_BEARER`: gyldig token for MyShare-admin-API-et, lagt inn som hemmelighet i Coolify.
  Det er ikke bekreftet at Contributions-klientens token kan brukes her. Statisk bearer må fornyes når den utløper.
- `MYSHARE_LEGACY_CLUB_ID=4050`: eksisterende lag, grupper og innstillinger tilhører BUK Sørlandet.
  Bekreft denne koblingen før aktivering. Andre klubber starter med egne lokale data.
- `MYSHARE_API_PERSON_ID`: eksplisitt MyShare-personidentitet for eksisterende maskin-API-nøkler.
  Mangler den, får maskinklienter ikke tilgang når klubbtilgang er aktiv. De kan velge en autorisert klubb
  med `X-Club-Id`, men ikke overstyre personidentiteten via forespørselen.

Klubben velges under `/clubs`. Lag, grupper, mål og pottinnstillinger isoleres i separate SQLite-filer på
samme vedvarende volum (`club-<id>.db`); den opprinnelige databasen beholdes for den konfigurerte eksisterende
klubben. Ta backup av **alle** databasefiler. MyShare-tilgang caches i opptil 30 sekunder per brukerscope;
ved API-feil etter utløp brukes ikke gamle tillatelser. Person-ID og klubbtilgang kontrolleres på serveren.

Pottens standardvisning omfatter alle medlemmene i valgt klubb, også uten lokale lag.
Automatiske ekstrabidrag bruker MyShare med valgt klubb og team. Contributions-org-ID brukes ikke som en
antatt klubbkobling. Ved feil vises merket manuelt reservebeløp som før.

I klubbmodus er anonym `/skjerm` deaktivert: en anonym besøkende har ingen verifiserbar teamtilgang.
Innlogget Pott-side har fortsatt storskjermmodus. Tidligere `Auth:AllowedPersonIds` erstattes av MyShare-
tilgangen i klubbmodus. Uten aktivering beholdes den tidligere tilgangsmodellen.

Nye lesende endepunkter: `/api/clubs`, `/api/clubs/{id}/members`, `/api/clubs/{id}/status`.
Ukjente eller uautoriserte klubb-ID-er gir 404, og ingen medlemsstatus hentes for dem.

## Administratorer

BCC person-ID **21410** er fast superadministrator. Den krever ikke en oppføring i
`Auth:AllowedPersonIds`, kan ikke fjernes og får menyvalget **Administratorer** (`/administrators`).
Bare denne innloggede personen kan legge til eller fjerne administratorer. Maskin-API-nøkler kan ikke
administrere roller, heller ikke når de er knyttet til 21410. Rollen lagres i tabellen `Administrators`
i den opprinnelige databasen, med hvem som la den til og tidspunkt. Tilbakekalling kontrolleres på nytt
ved hver endring, uten rollecaching i innloggingscookien.

I klubbmodus kan vanlige brukere lese klubbene MyShare gir tilgang til. Administratorer kan endre
lag, grupper, import, månedsmål og pottinnstillinger i disse klubbene. Superadministrator- og
administratorrollen overstyrer **ikke** MyShare-teamtilgang. Før klubbmodus aktiveres beholdes
skriverettene til de tidligere eksplisitt tillatte brukerne og maskinnøklene.

## BCC-datatest (13. november 2026)

Superadministrator 21410 kan åpne `/diagnostics/bcc` og velge **Kjør datatest**.
Testen bruker appens eksisterende `BccApi`-klient med separate lesescopes, faste API-adresser
fra BCC SDK og maksimalt 120 sekunder per kjøring. Den endrer ingen appdata eller upstream-data.
API-nøkler og andre administratorer får ikke kjøre den. Token og rå feilmeldinger vises ikke.
Fødselsdato vises bare som tilgjengelig/mangler. Utvalg av bidrag og tilgangsdata vises kun her.
Resultatene blir ikke lagret; de vises i den innloggede nettleserøkten.

Gjennomgåtte kilder: https://api.bcc.no/core/swagger.json og
https://api.bcc.no/contributions/swagger.json (samt Swagger UI i utviklerportalen).

- Core: persons inneholder navn, personID, UID, birthDate og churchUid. Affiliations har
  personUid, orgUid, type, isActive og gyldighetsdatoer. Orgs har UID og numerisk orgID.
- Core: groups, group members og checkGroupMemberships beskriver gruppemedlemskap.
  Rollenavn og roleAssignments finnes også, men er ikke i seg selv bekreftet MyShare-tilgang.
  Consents, legalBases, relations, countries og anonym statistikk er ikke nødvendige for BUK-status.
- Contributions: TeamConfig har teamID, teamName, orgIDsWithAccess og userIDsWithAccess.
  Dette er en mulig offisiell kilde for MyShare-teamtilgang; organisasjonsbasert tilgang må
  vurderes sammen med aktive tilknytninger og bekreftes med ekte svar.
- Contributions: ClubOrgMapping er mulig kobling mellom klubb og Core-organisasjon.
  Swagger deklarerer en ContributionReadCollectionResponse som svar; dette fremstår som
  upresist og må avklares fra faktisk respons. Diagnostikken viser feltnavn og et filtrert utvalg.
- Contributions: memberStatus, batch, memberClubContributions og status history dekker status
  og sesonghistorikk. Contributions dekker generelle bidrag. Targets/AllTargets, Projects,
  Registrations og Milestones finnes også; disse utgjør ingen entydig komplett medlemsliste.
- Action-endepunktene gir spesialiserte statusoversikter og projeksjoner. Disse brukes ikke
  som autoritativt medlemsregister eller tilgangsliste.

Swagger har også scope-avvik: group members oppgir persons.groups#read som ikke står i den
annonserte scope-listen, og roleAssignments-listen oppgir persons.persons.role_assignments#read.
Diagnostikken tester dokumentert medlems-scope uten å endre produksjonens vanlige scopes.
HTTP 400 kan bety at Core-filteret må tilpasses; HTTP 200 med et lite utvalg beviser verken
fullstendig medlemsliste eller at en Core-gruppe tilsvarer MyShare-klubb 4050/6817.
Ingen overgang til Core som tilgangsregister gjøres automatisk basert på testen.
