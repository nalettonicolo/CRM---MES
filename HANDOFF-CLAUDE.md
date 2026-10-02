# Handoff / punto della situazione

Aggiornato: **2 ottobre 2026, sera (contro chiusi in codice)**.

## Situazione in una riga

**Working tree + Neon migrato** (incluso `AddSsoAndLocale`): gap di mercato e **contro** sviluppati.
**Prossimo passo tuo:** commit + push su `main`/Render.

## Contro chiusi in questa sessione

| Contro | Cosa c’è ora |
|---|---|
| SdI intermediario | `Sdi:Provider=http` + `HttpSdiProvider` (BaseUrl/ApiKey/SubmitPath) oltre stub |
| Campo macchine | `POST …/demo-feed` + `scripts/machine-gateway/demo_simulator.py` |
| SSO | `POST /api/auth/external`, link login web, ExternalProvider/Subject |
| Multilingua / valuta | `CompanyProfile.Locale` + `Currency`; FatturaPA Divisa; I18n web IT/EN |
| Assistente IA | `/assistente`, stub o OpenAI (`Ai:Provider`) |
| Prec. (sera) | MRP avanzato, G13, planning web, CAPA/taratura, presenze, Swagger |

## Resta fuori codice (dopo il push)

1. Contratto intermediario SdI reale (URL/chiavi su Render).
2. Macchina/contatore fisici in officina (demo già usabile).
3. G16: hosting a pagamento, referenze, firma .exe, Stripe.
4. IdP OIDC production mapper (oggi endpoint di scambio; abilitare `Auth:External:Enabled` solo dietro IdP fidato).

## Verifica

```text
dotnet test CrmMes.sln -nologo -v q
dotnet ef database update --project CrmMes.Api --startup-project CrmMes.Api
```

Config produzione tipica dopo push:
- `Sdi:Provider=http`, `Sdi:BaseUrl`, `Sdi:ApiKey`
- `Ai:Provider=stub` o `openai` + `Ai:ApiKey`
- `Auth:External:Enabled=true` solo con IdP
