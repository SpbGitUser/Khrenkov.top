# SQLite storage implementation plan

**Goal:** Add a persistent SQLite database, versioned schema/data migrations, and an initial import of the existing uploaded files and public links.

**Specification:** The owner's request on 2026-10-19 authorizes implementation of the previously discussed SQLite step, extended to file metadata. Binary file content remains on disk. Preserve the frontend/API contracts and existing links. Do not commit or deploy to the live VPS.

**Architecture:** Use Microsoft.Data.Sqlite directly with parameterized SQL. Keep a short-lived connection per operation. Store file metadata in Files, share limits in ShareLinks with a cascading foreign key, and migration versions in SchemaMigrations. Apply pending migrations transactionally before serving requests. A data migration scans Uploads and imports legacy JSON once, with its completion recorded in the same transaction.

**Files and responsibilities:**

- `backend/Khrenkov.top/Data/ApplicationDatabase.cs`: database location, connections, versioned migration runner.
- `backend/Khrenkov.top/Data/Migrations/001_InitialSchema.sql`: schema, constraints and indexes.
- `backend/Khrenkov.top/Data/Migrations/ImportExistingFiles.cs`: one-time data import and validation.
- `backend/Khrenkov.top/Services/FileCatalogService.cs`: metadata queries and updates used by file endpoints.
- `backend/Khrenkov.top/Services/ShareLinkService.cs`: SQL-backed link creation and atomic consumption.
- `backend/Khrenkov.top/Models/StoredFile.cs`: file metadata returned through the existing API.
- Existing controllers and `Program.cs`: wire storage, run migrations, keep metadata consistent with upload/delete.
- `backend/Khrenkov.top.Tests/`: real SQLite/filesystem/HTTP integration tests using isolated temporary directories.
- Existing workflow, deploy script and documentation: tests before publication, persistent private Data directory and startup migrations.

## 1. Database and migration behavior

- [x] Add a net8.0 xUnit integration-test project with WebApplicationFactory and real temporary files. Run tests against the existing application and observe failures for missing database/import behavior.
- [x] Add SQLite dependency, connection factory and transactional versioned migrations. Configure an absolute database path via `Database:Path`, defaulting to `Data/application.db` under the content root. Reject paths inside Uploads.
- [x] Migration 1 creates Files and ShareLinks with primary/foreign keys and nonnegative size/download constraints. Migration 2 scans existing files, excludes `_shares.json`, and imports valid legacy links without changing tokens, timestamps or remaining counts. Invalid JSON must fail visibly and leave the import unapplied so it can be retried after correction.
- [x] Test first startup, empty directory, Unicode names, repeat startup, rollback/retry, missing legacy files and schema constraints. Use `dotnet test backend/Khrenkov.top.Tests/Khrenkov.top.Tests.csproj`.

## 2. Use the database in existing scenarios

- [x] Add failing tests for upload/overwrite/delete metadata, persistence across restarts and one successful consumption under concurrent requests for a single-use link.
- [x] Implement file catalog reads/upserts/deletes and SQL-backed link storage. Record completed uploads after closing the file stream. Cascade link removal when a file is deleted. Never expose the legacy store or database as downloadable user files.
- [x] Consume limits in a short database transaction, checking that the file exists before consuming. Keep HTTP transfer outside the transaction; count authorized transfers rather than completed client downloads.
- [x] Run the integration suite against actual SQLite, not mocks.

## 3. Deployment and verification

- [x] Add the test project to the solution and run it in GitHub Actions before publication. Keep the application self-contained linux-x64.
- [x] Create `${DEPLOY_PATH}/shared/Data`, give the service write access and pass the permanent database path in its environment. Do not include runtime DB files or development uploads in published artifacts. Keep Uploads linked to its existing persistent directory.
- [x] Document local startup, table relationships, both migrations, initial import, error/retry behavior and safe backup/restore. Document the consistency boundary between filesystem operations and SQL transactions.
- [x] Verify Release build, full integration suite, linux-x64 publish and deployment shell syntax. Request a focused code review of data preservation, migration idempotency and deployment behavior; resolve findings.
- [x] Inspect Git diff/status, confirm no commit, and report actual verification results and any untested server actions.

Verified: 21 Release integration tests passed; self-contained linux-x64 publish succeeded with native SQLite and without runtime Data/Uploads; deploy script passed bash -n; local database integrity and foreign keys passed, with one existing file imported. Focused review findings were reproduced and resolved. No commit or live deployment was performed.
