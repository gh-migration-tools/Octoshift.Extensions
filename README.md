# Octoshift.Extensions

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/gh-migration-tools/Octoshift.Extensions/blob/main/LICENSE)
[![Build and test](https://github.com/gh-migration-tools/Octoshift.Extensions/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/gh-migration-tools/Octoshift.Extensions/actions/workflows/build-and-test.yml)

The **Octoshift.Extensions** project provides a [NuGet package](https://github.com/gh-migration-tools/Octoshift.Extensions/pkgs/nuget/Octoshift.Extensions) with reusable classes based on the [**Octoshift**](https://github.com/gh-migration-tools/Octoshift) library for custom migration tools.

## Usage

### Add NuGet source

As GitHub Packages does not support anonymous access for NuGet feeds you need to create a NuGet source using a GitHub personal access token (PAT) with the scope `read:packages`.

```
dotnet nuget add source https://nuget.pkg.github.com/gh-migration-tools/index.json
  --name gh-migration-tools
  --username USERNAME
  --password PAT
```

> [!TIP]
> Keep the `--username USERNAME` option as it is, just set the `PAT` in the `--password` option.

### Add NuGet package

```
dotnet add package Octoshift.Extensions --source gh-migration-tools
```
