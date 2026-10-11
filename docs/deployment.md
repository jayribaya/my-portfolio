# Deployment

## Container on a Linux VM

Build `docker build -t jay-portfolio .`. Use a supported Linux host and an HTTPS reverse proxy (Caddy or Nginx). Run the image as its nonroot default user. Bind the container port to loopback, not a public interface. Set `ASPNETCORE_ENVIRONMENT=Production`, `Mongo__ConnectionString` to an authenticated TLS Atlas connection, `Mongo__Database`, `AllowedHosts` to the exact public hostname, and `TrustedProxy` to the actual reverse proxy IP seen by the container. Never enable DemoMode. ASP.NET trusts only the configured proxy and its default loopback proxies.

Persist `/app/App_Data/keys` in a private volume writable by container UID 1654. Secure the volume with host disk encryption and restrictive permissions; Data Protection keys are sensitive and not encrypted by this sample's file provider. All instances must share keys and application name. Keep that volume across deployments or existing logins and form tokens become invalid. GridFS lives in MongoDB and needs no filesystem media volume.

Before the first launch, execute the same image with `--seed` and `--create-admin` against your intended database. Pass admin variables just to the setup process, then remove them. Setup refuses to overwrite an existing account. The host must serve HTTPS before sign-in; Production cookies are Secure, and HSTS/HTTPS redirection are enabled. Forward `Host` and `X-Forwarded-Proto` from the trusted proxy. Configure `AllowedHosts` correctly to prevent attacker-controlled canonical/sitemap hosts.

Liveness/readiness: GET `/health` pings MongoDB and returns 503 when unavailable. Configure an external probe using HTTPS (Production redirects HTTP). Startup also initializes indexes and fails if the database is unreachable, so orchestrators should retry startup rather than route traffic to a broken instance. Send stdout logs to your host log system and rotate them. Do not log request bodies or secrets.

## Azure

Use Azure App Service for Containers or a Linux VM supporting the .NET 10 container. Supply settings through App Service configuration/Key Vault; do not bake secrets into an image. Set the app's port to 8080 as required by the selected hosting product. Use a persistent private mount for Data Protection keys (or replace with Azure Blob/Key Vault Data Protection providers before scaling). Configure the actual trusted ingress proxy and forwarded protocol, domain, HTTPS-only setting and certificate. Verify secure cookies, login, uploads, `/health` and redirects after deployment. The application has not been deployed to Azure in this workspace.

Compare the current regional compute, egress, persistent volume and database costs before choosing a plan. No permanent free Azure tier or unlimited image storage is assumed. A small Linux VM with Atlas may be an inexpensive option, but pricing and limits change. GridFS shares your MongoDB storage quota: set upload budgets and monitor it.

## Atlas & data protection

Create a database-specific user with only the needed read/write and index permissions. Restrict the Atlas network access list to the application's actual egress IPs or private endpoint; avoid `0.0.0.0/0`. Store the SRV connection string in your secret manager. Use separate databases and credentials for development, test and production. TLS certificate validation remains enabled. Single-document compare-and-swap ordering works on standalone MongoDB and Atlas without multi-document transactions.

Back up the whole database, including `media.files`/`media.chunks` GridFS collections, `orders`, and `admins`, with the backup capability of your Atlas plan or MongoDB Database Tools (`mongodump --archive --gzip`). Pass the URI through your secured environment/tool configuration, keep backup credentials out of shell history, encrypt backups, restrict access and test restoring into a separate database. Back up the private Data Protection volume separately. The authenticated `/admin/export` JSON download exports portfolio content and ordering metadata, excludes accounts/contact PII, and is **not** a complete binary media/database backup. Audit TTL is one year and contact TTL is 90 days; MongoDB TTL removal is asynchronous.

Before going live: replace illustrative content, upload authorized screenshots, set verified profile URLs, enter actual work/education/certification details, attach your approved CV and test contact messages. Uploaded files are MIME/signature checked, but this is not malware scanning. Add a malware scanning service if accepting uploads from anyone beyond the trusted owner. PDFs download as attachments; the resume page uses an editable accessible text preview to avoid embedding active PDF content.

For multiple instances, replace the built-in per-process login/contact limiter with an ingress or distributed limiter. Current persistent account lockout and security stamp validation work across instances, but rate-limit counters do not. Start with one instance unless you configure shared keys, distributed limits and proxy ingress deliberately.
