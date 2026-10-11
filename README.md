# Jay Carlo — Portfolio & Content Studio

A .NET 10 MVC/Razor portfolio with an authenticated owner CMS. Production content lives in MongoDB; uploaded images and PDFs live in durable MongoDB GridFS behind publication-aware routes. Tailwind 4, TypeScript and locally bundled SortableJS power the responsive navy/blue interface. No JWTs or credentials are stored in browser storage; localStorage is used only for theme preference.

## Architecture and source

- `src/Portfolio.Domain`: project lifecycle, editorial content types, media metadata, account and audit entities.
- `src/Portfolio.Application`: validation, safe URLs, slug collision strategy, publication rules, order scopes and service contracts.
- `src/Portfolio.Infrastructure`: official MongoDB C# driver, indexes, atomic compare-and-swap persistence, GridFS, explicit memory test/preview adapter and deterministic seed content.
- `src/Portfolio.Web`: MVC controllers, cookie authentication, Identity password hashing, CSRF, rate limits, Razor public/admin screens, TypeScript and Tailwind source.
- `tests/Portfolio.Tests`: xUnit v3 domain, real HTTP/MVC tests and optional disposable MongoDB integration tests.
- `Dockerfile`, `compose.yaml`, `.github/workflows/ci.yml`: packaging, local database and automated validation.

Public routes: `/`, `/about`, `/projects`, `/projects/{slug}`, `/skills`, `/experience`, `/education`, `/certifications`, `/resume`, `/contact`, `/articles`. Admin: `/admin`, with modules in its sidebar. `/health`, `/sitemap.xml`, `/robots.txt` and paginated `/api/projects` are public. There is no public registration route. Articles are an optional plain-text editorial module, hidden until entries are enabled.

## Prerequisites and build

Install the .NET 10 SDK, Node 24 LTS (or a compatible newer runtime), and MongoDB Community/Atlas or Docker Desktop. The checked-in `global.json` selects .NET 10 and Microsoft.Testing.Platform. Dependencies are pinned in project files and npm's lockfile. MongoDB.Driver 3.12.0, Serilog.AspNetCore 10.0.0, ASP.NET test host 10.0.12, Tailwind 4.3.3, TypeScript 7.0.2 and SortableJS 1.15.7 were checked against official package registries during implementation.

From the repository root:

```powershell
npm ci --prefix src/Portfolio.Web
npm run build --prefix src/Portfolio.Web
dotnet restore Portfolio.slnx
dotnet build Portfolio.slnx -m:1 --no-restore
dotnet test --solution Portfolio.slnx --no-build --no-restore
```

Compiled local CSS/JS is committed for `dotnet run` convenience. Rebuild the frontend after editing `Styles/site.css`, `Scripts/site.ts` or Razor utility classes. `npm run check --prefix src/Portfolio.Web` checks TypeScript without emitting. Stop running Windows app/test processes before rebuilding to avoid DLL file locks.

## Persistent local setup (PowerShell)

1. Start your local MongoDB, or run `docker compose up -d mongo`. The Compose MongoDB port is bound only to loopback and is intentionally unauthenticated for local development. Never use this database configuration on a public host.
2. Set configuration for the current terminal:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Mongo__ConnectionString = 'mongodb://127.0.0.1:27017'
$env:Mongo__Database = 'jay_portfolio'
$env:DemoMode = 'false'
dotnet run --project src/Portfolio.Web --no-launch-profile -- --seed
```

For Atlas replace the URI in your terminal/secret manager with your private authenticated TLS URI. `dotnet run` does not read `.env`. `.env.example` is for Docker Compose only. No actual credentials belong in source control, CLI arguments or the tracked appsettings file.

3. Create your first admin through the CLI. The email is normalized; the password must be 14–128 characters and include uppercase, lowercase, a digit and a symbol. The process refuses to overwrite an existing account.

```powershell
$env:PORTFOLIO_ADMIN_EMAIL = Read-Host 'Admin email'
$portfolioPassword = Read-Host 'Admin password' -AsSecureString
$env:PORTFOLIO_ADMIN_PASSWORD = ConvertFrom-SecureString $portfolioPassword -AsPlainText
dotnet run --project src/Portfolio.Web --no-launch-profile -- --create-admin
Remove-Item Env:PORTFOLIO_ADMIN_EMAIL, Env:PORTFOLIO_ADMIN_PASSWORD
$portfolioPassword.Dispose()
```

4. Launch and open `/admin/login`:

```powershell
dotnet run --project src/Portfolio.Web --no-launch-profile --urls http://localhost:5080
```

Visit `http://localhost:5080`. Local Development accepts HTTP cookies for localhost; Production always requires HTTPS/Secure cookies. Change the password from Profile & security. Sign-in expires after 30 minutes, locks the persistent account for 15 minutes after five failures, and validates the security stamp on every authenticated request.

On Linux, set the same variables with `export`, supply the password from a secret manager or `read -s`, and unset setup variables after running the command. Do not pass passwords in command arguments.

## Docker development

```text
copy .env.example .env
docker compose up -d --build

docker compose run --rm web --seed
```

For admin setup, export `PORTFOLIO_ADMIN_EMAIL` and `PORTFOLIO_ADMIN_PASSWORD` securely in your shell, then use `docker compose run --rm -e PORTFOLIO_ADMIN_EMAIL -e PORTFOLIO_ADMIN_PASSWORD web --create-admin`. Clear those shell variables. Named volumes retain MongoDB content/media and Data Protection keys across restarts. Do not delete the volumes if you need your data. The app is available at `http://localhost:5080`. Docker image build has not been executed in this Windows workspace because Docker is unavailable; CI includes the build.

## Quick public preview without MongoDB

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:DemoMode = 'true'
dotnet run --project src/Portfolio.Web --no-launch-profile --urls http://localhost:5080
```

This explicitly labeled in-memory preview resets on restart and has no admin account by default. To test the CMS in preview mode, set PORTFOLIO_ADMIN_EMAIL and PORTFOLIO_ADMIN_PASSWORD securely before starting it; an ephemeral account is created using the same password rules. To use the CMS, use the persistent MongoDB setup above. Production refuses DemoMode. Seed examples are added only by `--seed` or DemoMode, not automatically on normal production startup. Seed reruns preserve existing records using deterministic IDs.

## Owner workflow

Edit or add a project, select its work status, choose Draft/Published/Archived independently, pin it if desired, assign categories, add technology names and case-study text, then save. A draft stays private, including direct detail routes, public JSON and sitemap. Preview is authenticated. Soft-delete retains a project and its ordering position. Restore always returns it to Draft; publish explicitly after reviewing.

Upload 1–4 signature-checked PNG/JPEG/WebP/PDF files at a time, with descriptive alt text. Assign cover/gallery assets in the editor. SVG is rejected. Images are not anonymous until assigned to published work; enabled resume PDFs are public attachments. Reusing an asset in published work makes that asset public, even if it is also used by a draft. Avoid mixing confidential and public assets. Resume uses an editable accessible text preview plus a PDF download.

The complete project list supports SortableJS drag handles and up/down keyboard buttons. Save persists the order; refreshing does not reset it. Featured projects have their own ordered list. Filters/pagination disable global ordering controls to avoid overwriting hidden rows. New records append by default. Use the dedicated Order all projects screen for complete ordering at any project count; it also links to the independent featured and section scopes. The paginated list deliberately disables partial reordering. Homepage sections also have independent ordering and enabled flags. Skills/experience/education/certifications use editable numeric display order. Bulk visibility actions report per-record successes or stale/conflicting skips.

Edit Site & branding for your name, navigation name, professional title, footer tagline and verified GitHub/LinkedIn links. The initial six case studies are illustrative and explicitly labeled. Replace all unverified descriptions and placeholder dates/employment/education before publication. No employer-confidential screenshots, metrics or URLs are invented.

## Ordering API and consistency

Authenticated POST endpoints `/admin/api/projects/reorder`, `/admin/api/featured/reorder`, `/admin/api/sections/reorder` accept `{ "ids": ["id1", "id2"], "version": 0 }`. Include the current antiforgery value in `X-CSRF-TOKEN`; tokens appear in the admin layout meta element. The entire eligible scope is required; unknown, duplicate or omitted IDs return 400. Send the version rendered by the admin screen; stale concurrent changes return 409. Success returns the canonical document and next version. Each scope uses one atomic MongoDB compare-and-swap update, so there is no requirement for replica-set transactions.

The global scope includes retained archived/deleted projects so restores keep their place. Featured eligibility includes pinned, nondeleted, nonarchived projects. Public results apply publication filtering to the independent order. Items created or eligibility changes concurrent with reordering are merged into canonical reads: saved eligible IDs retain their order and newly eligible IDs append. Legacy SortOrder/FeaturedSortOrder fields are retained in the requested model but order documents are authoritative. Project/content editors also use optimistic version checks. Audit writes occur after content writes; they are not atomic with content, so a logging failure can leave the change applied—refresh before retrying.

## Security, scale and deployment

Read `docs/deployment.md` for Linux/Azure, Atlas access restrictions, HTTPS/proxy setup, Data Protection volumes, backups and costs. Read `docs/verification.md` for responsive/accessibility and CMS acceptance checks.

This is a single-owner portfolio, not a multi-tenant publishing platform. The Mongo adapter intentionally materializes the small portfolio collections before filtering/pagination/merging global order. For thousands of records, replace those reads with server-side paginated queries and an ordering-aware aggregation. GridFS shares database quota; monitor media growth. Rate limits are per process; configure distributed/ingress limits before scaling. Password hashing uses ASP.NET Core Identity's supported PasswordHasher; the custom Mongo account store/cookie flow does not include the full UserManager/2FA/recovery-email UI. Rich content is deliberately plain text and Razor encoded. External URLs are HTTPS only.

Implemented security includes policy authorization, antiforgery on every modifying MVC route, Secure/HttpOnly/SameSite cookies in Production, account lockout/security stamps, CSP/no-sniff/frame restrictions, no public signup, MIME/signature/size guards and privacy-conscious contacts/audit. The built-in contact inbox saves messages; it does not send email. Contacts have a 90-day MongoDB TTL and audit events a one-year TTL. Upload signatures are not a virus scanner. Protect Data Protection keys with encrypted host storage/private permissions and configure trusted ingress before deployment.

## Verification record and remaining launch work

Locally verified: frontend build, TypeScript checks, .NET build (zero warnings/errors) and all 28 domain/HTTP/MongoDB tests passing; the MongoDB integration test is enabled by `PORTFOLIO_TEST_MONGO` and also runs in CI. Browser review covers responsive public UI and theme/menu behavior. See `docs/verification.md` for the current checklist. The disposable database is not a configured production Atlas account.

Before a live launch: provide Atlas/hosting secrets, create your own admin, replace illustrative content, upload the authorized CV/screenshots, configure your public hostname and HTTPS reverse proxy, run your deployment smoke checks and test backups. Azure provisioning, live publication, container execution and a formal accessibility audit are not verified in this workspace.
