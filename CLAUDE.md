# Fabulis

Web app for generating stories using LLMs and managing a story library.

## Stack

- ASP.NET Core on .NET 10
- Solution format: .slnx
- EF Core with SQLite + SQLCipher (encrypted at rest)

## Project structure

- `Fabulis.slnx` — solution file
- `src/Fabulis.Server/` — ASP.NET Core web server
  - `Data/` — EF Core DbContext and entity models

## Build & run

```bash
dotnet build Fabulis.slnx
dotnet run --project src/Fabulis.Server
```

The database password is entered in the browser at `/unlock` — it is never stored on disk.
