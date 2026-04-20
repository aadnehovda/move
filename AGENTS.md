# Agent Instructions

## Release Rules

When preparing a release:

- Run `dotnet build move.slnx`
- Run `dotnet test move.slnx`
- Commit the intended release changes before tagging
- Push `main` before pushing the release tag
- Verify the tag-triggered `Build And Test` workflow succeeds
- Verify the tag-triggered `Publish Release` workflow succeeds
- Verify release assets exist on GitHub
- Verify release notes are present and meaningful

## Release Notes

- Do not rely on remembering release notes manually
- Before tagging a release, create `docs/releases/<tag>.md`
- Release notes must be authored for each release and committed to the repository
- Release notes should summarize user-visible changes and verification, not just repeat commit messages
- The release workflow must publish `docs/releases/<tag>.md` as the release body
- After release creation, verify with:
  - `gh release view <tag> --json body,url`
- A release body that is only a `Full Changelog` link is not sufficient

## GitHub Verification

After pushing a tag, verify with `gh`:

- `gh run list --limit 10`
- `gh release view <tag> --json assets,body,url`

Do not report the release as complete until both workflow success and release note presence have been verified.
