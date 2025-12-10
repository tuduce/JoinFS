# JoinFS Release Notes

This directory contains release notes for JoinFS releases.

## Format

Release notes should be named `Release-VERSION.md` where VERSION matches the version number being released.

For example:
- `Release-25.8.md` for version 25.8
- `Release-26.0.md` for version 26.0

## Usage

When creating a release using the "Build JoinFS in all release variants" workflow:
1. Create a file named `Release-VERSION.md` in this directory with your release notes
2. Run the workflow with the VERSION parameter (e.g., "25.8")
3. The workflow will automatically use the release notes from the file when creating the GitHub release

If no release notes file exists for the version, the workflow will create a release with a default message.
