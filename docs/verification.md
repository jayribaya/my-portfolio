# Verification

Run `npm ci --prefix src/Portfolio.Web`, `npm run check --prefix src/Portfolio.Web`, `npm run build --prefix src/Portfolio.Web`, `dotnet restore Portfolio.slnx`, `dotnet build Portfolio.slnx -m:1 --no-restore`, then `dotnet test --solution Portfolio.slnx --no-build --no-restore`. xUnit v3 uses Microsoft.Testing.Platform through `global.json`; do not use old VSTest `--logger` syntax. Single MSBuild worker is useful in restricted Windows environments.

Domain tests cover slug normalization/collisions, publication rules, project appending, independent scope ordering, concurrent version checks, invalid/duplicate/incomplete ordering, and MIME/signature/size rejection. WebApplicationFactory tests run the real MVC controllers, Razor views, authorization, cookie authentication and antiforgery with the explicit isolated memory adapter. They cover create/publish/unpublish/archive/restore, stale edits, XSS encoding, section visibility, protected media, lockout and session stamp revocation. No production credentials are used.

Set `PORTFOLIO_TEST_MONGO=mongodb://localhost:27017` to run the database integration test as well. It creates a unique `portfolio_test_*` database, verifies unique/query indexes, compare-and-swap, adapter recreation persistence, publication filtering, section changes and GridFS bytes, then drops only that generated database. Without this variable the database test is visibly skipped. CI always supplies its disposable MongoDB service.

## Manual browser checks

At 390 px, 768 px and 1440 px, verify no horizontal overflow, sticky navigation, mobile menu expanded state, card legibility, gallery images, table scrolling and usable buttons. Check both themes and OS reduced-motion mode. At 200% zoom, use Tab/Shift+Tab through public navigation, project filters and admin editors; verify focus outlines, skip link, descriptive labels, alt text and error summary. Confirm the modal supports Escape and returns focus. Run browser accessibility tooling/axe or Lighthouse and check contrast and landmark/heading order; this repository does not claim a formal accessibility certification.

1. Set up the admin in MongoDB, sign in, create a draft with cover/gallery and inspect the private preview. Anonymous detail/API/sitemap/media must exclude it.
2. Publish and confirm it appears on gallery/detail routes without rebuilding. Change its cover, then verify the new asset is shown.
3. Reorder the complete project list using keyboard arrows, save and refresh. Reorder featured separately; public homepage and gallery should follow their own scopes. Drag handles are an additional pointer option.
4. Filter by category and confirm global ordering controls are disabled. Reset filters and verify the original global order remains.
5. Open two editors/order screens. Save in one and then the other: the stale save should return 409 with refresh guidance.
6. Hide and reorder a homepage section; refresh anonymously. Edit skills and experience and verify updates. Upload a resume PDF and set its accessible text preview.
7. Unpublish, archive, soft-delete and restore a project. Restored content is a draft until explicitly published.
8. Submit a contact message, mark it read and delete it in the inbox. Confirm audit entries omit the message/email content.
9. Check missing CSRF tokens, wrong passwords, lockout, logout, expired cookies and password-change session invalidation. In Production check HttpOnly/Secure/SameSite cookies and HTTPS behavior.
10. Restart the application against the same MongoDB and Data Protection volume; verify content, ordering and media remain available.

## Local verification on 11 October 2026

- Frontend build and TypeScript checks passed.
- .NET solution build passed with zero warnings and errors.
- All 28 tests passed with PORTFOLIO_TEST_MONGO enabled against an isolated MongoDB 8.2.0 server; no tests skipped. Database tests use a generated database and remove it after verification.
- Desktop public homepage reviewed visually. At 390 px, homepage and project gallery had no horizontal overflow; mobile navigation expanded/collapsed correctly and light/dark switching worked.
- Docker/container execution, Azure/Atlas deployment and automated accessibility auditing were not executed. CI is supplied but has not run on GitHub from this workspace.
