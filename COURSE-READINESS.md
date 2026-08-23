# Agentic Coding Course Readiness

This document records the changes recommended before using AuroraTranslator as
the subject repository for the Agentic Coding Projects training course. It is a
repository-readiness plan, not a product roadmap and not a specification for the
eventual training failure.

Assessment snapshot:

- Date: 2026-08-19
- Public repository: <https://github.com/Xellarant/AuroraTranslator>
- Assessed public commit: `c76388b37e0997b06ed16f7cf0169d860643df72`
- Default branch: `master`
- Primary language: C#

The repository is a strong subject candidate after the P0 items below are
resolved. Its Aurora XML importer, SQLite schema and migrations, expression
engine, package-resolution behavior, and character-state evaluator provide
substantial runtime behavior and realistic multi-file engineering work.

## Training Selection Criteria

The relevant course requirements are:

- The repository must be public.
- It must use an accepted license: MIT, Apache-2.0, or BSD-3-Clause.
- The codebase must contain substantive runtime behavior rather than being
  primarily documentation, configuration, static data, or a thin wrapper.
- The language must be one the participant can confidently review.
- A fresh checkout must build and its verification suite must pass without
  private machine state.
- Build and verification should finish comfortably within ten minutes.
- The codebase should support at least two or three realistic, independently
  verifiable engineering tasks.
- The exact training problem or failure seed must not be published in the
  repository, an issue, a pull request, or other public discussion.

AuroraTranslator already meets the public-repository, language, codebase-depth,
runtime-behavior, and task-variety criteria. Licensing and self-contained
verification are the current hard blockers.

## Current Evidence

The public repository currently contains 22 C# source files with approximately
17,000 lines of C# outside generated build output. GitHub recognizes C# as its
primary and only detected programming language.

A fresh archive of public commit `c76388b` was restored, built, and executed
through the current console regression harness on 2026-08-19. Restore and build
completed in approximately 15 seconds, which is comfortably inside the course
limit. The run then exited unsuccessfully:

- 3 of 8 regression cases passed.
- 5 cases failed because
  `5eApiTranslator/Data/aurora-first-party-regression.sqlite` was unavailable.
- The missing database exists in the local development checkout, is 37,433,344
  bytes, and is excluded by the repository-wide `*.sqlite` ignore rule.
- Restore also emitted `NU1903` for transitive dependency
  `SQLitePCLRaw.lib.e_sqlite3` version `2.1.11` and advisory
  [GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q).

The repository currently has no root license file and no GitHub Actions
workflow. The README documents the solution build and application commands but
does not document the regression-harness command.

## Recommended Changes

### P0: Add an accepted repository license

Add a root `LICENSE` file. MIT is the simplest match for the course and the
license already used by related Xellarant tooling, provided the repository owner
has the right to license all included code.

Before adding the license:

1. Review source, SQL, fixtures, and generated examples for copied third-party
   code or data.
2. Preserve any required third-party notices separately.
3. Do not imply that Wizards of the Coast, Aurora Builder, or other third-party
   game content is covered by the repository's code license.
4. Confirm that committed fixtures contain synthetic or redistributable content.

Acceptance criteria:

- `LICENSE` contains an unmodified MIT, Apache-2.0, or BSD-3-Clause license.
- GitHub recognizes the license on the repository page.
- Third-party notices and content boundaries are explicit where needed.

### P0: Make the regression suite self-contained

Do not solve the missing-fixture problem by committing the existing 37 MB
first-party regression database. Besides being unnecessarily large, that
database may contain third-party game content that should not be redistributed.

Replace the dependency on `aurora-first-party-regression.sqlite` with a small,
deterministic fixture builder. The preferred approach is:

1. Author minimal synthetic Aurora XML using `ID_TEST_...` identifiers and
   original placeholder names and descriptions.
2. Import that XML into a temporary SQLite database through the production
   importer.
3. Add only the elements, rules, requirements, support tags, and choices needed
   by each regression scenario.
4. Give each test an isolated temporary database or a fresh copy of a generated
   synthetic baseline.
5. Delete temporary databases after the test run.

Tests must not depend on:

- an installed Aurora application or personal Aurora content directory;
- ignored files from the developer's checkout;
- copyrighted first-party rule text;
- absolute user-profile paths;
- network services; or
- a previously generated local database.

Acceptance criteria:

- A fresh clone can run every committed regression case.
- All test inputs are tracked, synthetic, and small enough for normal Git use.
- Tests exercise production import, migration, and evaluation paths rather than
  bypassing them with unrealistic mocks.
- The complete suite passes on Windows and the selected CI operating system.

### P1: Establish one canonical verification command

The current `AuroraTranslator.Tests` project is a console harness with custom
assertions and a correct nonzero failure exit. It can remain in that form for
the smallest readiness change, but the repository must clearly expose one
command that performs all verification.

Minimum option:

```powershell
dotnet run --project .\AuroraTranslator.Tests\AuroraTranslator.Tests.csproj
```

Preferred longer-term option:

- Convert the harness to xUnit, NUnit, or MSTest.
- Make `dotnet test .\AuroraTranslator.sln` the canonical command.
- Preserve focused filtering so individual regression scenarios remain quick to
  diagnose.

Update the README with prerequisites, the canonical verification command,
expected runtime, and any legitimate non-fatal warnings.

Acceptance criteria:

- One documented command builds and runs all committed verification.
- The command returns zero only when every test passes.
- A contributor does not need undocumented local files or setup.

### P1: Add continuous integration

Add a GitHub Actions workflow that runs for pushes to the default branch and for
pull requests. It should:

1. Check out the repository.
2. Install the required .NET 10 SDK.
3. Restore dependencies.
4. Build `AuroraTranslator.sln` in a clean configuration.
5. Run the canonical verification command.

CI should use only tracked synthetic fixtures and should not download Aurora or
game-content data.

Acceptance criteria:

- The workflow succeeds at the selected baseline commit.
- A deliberately failing assertion makes the workflow fail.
- Normal execution remains comfortably under ten minutes.

### P1: Resolve the vulnerable SQLite native dependency

Determine which direct package version brings in
`SQLitePCLRaw.lib.e_sqlite3` `2.1.11`, then update to a compatible package set
that resolves `GHSA-2m69-gcr7-jv3q`. Do not suppress `NU1903` merely to produce a
clean log.

Acceptance criteria:

- Restore and build no longer report the advisory.
- Import, migration, and temporary-database tests pass on each supported
  platform.
- SQLite initialization and native library loading are exercised by CI.

### P2: Improve repository orientation

These changes are helpful but are not eligibility blockers:

- Add a concise GitHub repository description and D&D/C#/SQLite topics.
- Add a short "Testing" section near the README build instructions.
- Explain which fixtures are synthetic and which local corpus files are
  intentionally ignored.
- Document the supported .NET SDK in `global.json` if reproducible SDK selection
  becomes important.
- Optionally add `CONTRIBUTING.md` with build, test, fixture, and licensing
  expectations.

## Clean-Clone Readiness Gate

Before selecting AuroraTranslator for the course, validate the exact public
baseline from a new directory rather than from the long-lived development
checkout:

1. Clone the public repository.
2. Confirm that GitHub recognizes an accepted license.
3. Run restore, build, and the canonical verification command.
4. Confirm that all tests pass without copied local files.
5. Record total runtime and confirm it is below ten minutes.
6. Confirm CI is green for the same commit.
7. Confirm the checkout contains no proprietary corpus or generated first-party
   database.

Do not treat a test run from the normal development checkout as sufficient
proof because ignored local files can conceal missing public fixtures.

## Training-Problem Isolation

Repository-readiness changes may be committed and published because they create
a legitimate clean baseline. The actual training failure must follow a separate
workflow:

1. Select and record the clean public baseline commit.
2. Create the training workspace from that commit.
3. Introduce the failure only inside the private training workspace.
4. Do not push the failure seed or its exact expected fix.
5. Do not open a public issue or pull request describing the seeded problem.
6. Do not reuse a defect whose complete solution is already visible in public
   history.

Suitable problem areas include importer behavior, SQLite migration invariants,
expression evaluation, package precedence, and character-choice resolution.
Keep any eventual seed description private and independently verifiable.

## Recommended Implementation Order

1. Preserve and review any existing uncommitted work before beginning readiness
   changes.
2. Confirm code and fixture licensing boundaries, then add the accepted license.
3. Replace the ignored first-party database dependency with synthetic generated
   fixtures.
4. Make the full public regression harness pass from a clean archive or clone.
5. Document the canonical verification command.
6. Add CI and verify the clean baseline.
7. Resolve the SQLite dependency advisory and rerun the full clean-clone gate.
8. Only then create a separate private course workspace and design the training
   failure.

## Definition of Ready

AuroraTranslator is ready to select when all of the following are true:

- [ ] GitHub recognizes MIT, Apache-2.0, or BSD-3-Clause.
- [ ] Every committed test input is available from a fresh clone.
- [ ] The complete verification suite passes without personal or proprietary
      data.
- [ ] One canonical verification command is documented.
- [ ] CI passes on the selected baseline commit.
- [ ] Restore no longer reports the known SQLite native-package advisory.
- [ ] Clean restore, build, and verification finish in under ten minutes.
- [ ] The selected baseline contains enough runtime depth for multiple realistic
      tasks.
- [ ] The actual training failure and solution remain unpublished.
