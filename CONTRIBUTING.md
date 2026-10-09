# ✨ Contributing to WinUI.TableView

Thank you for your interest in contributing to **WinUI.TableView**! We welcome all contributions and appreciate your help in making this project better for everyone.

---

## ❔ Questions

Please search existing [Discussions](../../discussions) first to see if your question has already been answered. If not, feel free to start a new discussion for general questions. Keep GitHub issues focused on actionable bug reports and enhancements.

---

## 🐛 Reporting Bugs

Before reporting a bug, please search existing [Issues](../../issues) to see if it has already been reported. If you find a similar issue, add any additional details as a comment. If not, please [open a new issue](../../issues/new?template=bug_report.md) and provide as much detail as possible to help us reproduce and fix the problem.

---

## 💡 Suggesting Features

Please search existing [Issues](../../issues) and [Discussions](../../discussions) to see if your feature has already been suggested. If you find a similar request, consider adding your thoughts to the existing conversation. If not, please [open a feature request](../../issues/new?template=feature_request.md) and describe your idea.

---

## 🚀 Pull Requests

Before creating a Pull Request, please start a Discussion or open an Issue to describe your planned changes. This helps the maintainers and community provide feedback early and ensures your contribution aligns with the project goals. You can skip this step for minor fixes like typos or small documentation updates.

- Do NOT open pull requests from your `main` branch. Always create a new feature ( for example `add-cell-tests` ) in your fork before submitting a PR.
- Ensure your changes are tested with both WinUI 3 and Uno Platform targets.
- Add or update unit tests and integration tests to cover your changes.
- Complete the PR checklist, and ensure your code is tested with both targets.
- Update documentation as needed to reflect your changes.

[How to create a pull request from a fork (GitHub Docs)](https://help.github.com/en/github/collaborating-with-issues-and-pull-requests/creating-a-pull-request-from-a-fork)

---

## 🧪 Testing Dev Packages

When you open a pull request, our CI pipeline automatically builds and publishes a dev package to NuGet.org after a successful build. This allows you to test your changes before they are merged.

**Version Format:** `0.0.{buildnumber}-dev` (e.g., `0.0.1234-dev`)

---

## 📝 Code Style

- Follow the existing coding conventions and structure.
- Write clear, concise commit messages.
- Include comments where necessary.

---

## 💙 Thank You

Thank you for being a part of the WinUI.TableView community. Every contribution counts!

---

## Datgel fork: publishing `Datgel.WinUI.TableView`

This fork (`Datgel/WinUI.TableView`) repacks upstream plus Datgel's patches as
**`Datgel.WinUI.TableView`** on the internal `datgel-github` feed
(`https://nuget.pkg.github.com/Datgel/index.json`), which Datgel.Hub restores from.
The working branch is `datgel/v<X.Y.Z>-patched` (today `datgel/v1.5.0-patched`); fork
PRs target it, never `main` (which mirrors upstream).

Publishing is done by `.github/workflows/datgel-publish.yml` (DH-2508), never from a dev box.

**Cut a release** once the fork PR is merged into `datgel/v<X.Y.Z>-patched`:

```powershell
git fetch origin
git tag datgel-v1.5.0.7 origin/datgel/v1.5.0-patched   # next free 4th part
git push origin datgel-v1.5.0.7
```

Then bump `Datgel.WinUI.TableView` in Hub's `Directory.Packages.props` to that version.

What the workflow enforces:

- The version is explicit and 4-part (`Major.Minor.Build.Revision`, optional `-prerelease`);
  `X.Y.Z` must name a `datgel/vX.Y.Z-patched` branch, and the commit must be on it.
- A version already on the feed is refused before anything is built - versions are immutable.
- The package is packed the way every earlier version was: a separate
  `dotnet restore -p:Configuration=Release`, then VS MSBuild `-t:Pack`
  (`dotnet pack` cannot build the `-windows10.0.19041` TFMs), with `PackageId`,
  `Version` and the fork's `RepositoryUrl` overridden.
- The `lib/` file set must equal the newest published version's (21 files today) unless
  `allow_layout_change` is set; dependency-group changes are reported as warnings.
- After the push it downloads the package back from the feed and requires the SHA-256 to
  match what was packed, then prints the feed's version list.

**Dry run** (nothing pushed): every PR into `datgel/v*-patched` runs it. It packs, checks
the package, and proves the workflow token can still publish by re-pushing the newest
version already on the feed and requiring `409 Conflict`.

A manual run (`workflow_dispatch`, `version` + `push` inputs) is also defined, but GitHub
only offers it once the workflow file is on the repository's default branch (`main`, the
upstream mirror), so the tag is the release path.

Tags are `datgel-v*`, never `v*`: upstream's `cd-build.yml` publishes `v*` tags to nuget.org.
The assembly is **strong-named** with the committed `src/Datgel.WinUI.TableView.snk` (2048-bit,
public key token `1be46287a3c9dbc5`) in every configuration, and the workflow refuses a package
whose DLLs lack that token (DH-2532). The test project is signed with the same key, which is why
`InternalsVisibleTo` names the full public key. It is not Authenticode-signed, and it is never
obfuscated (a WinUI/XAML assembly). Versions up to 1.5.0.6 were not strong-named.
