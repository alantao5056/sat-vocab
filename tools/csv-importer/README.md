# CSV Importer Tool

A standalone Node.js script that parses a vocabulary CSV file and generates the per-user **template vocabulary database** (SQLite/LibSQL) that the app copies for each new user. Point `TEMPLATE_DB_PATH` (see the app's `.env`) at the `.db` file it produces.

## Features

- Uses `@libsql/client` to generate an SQLite `.db` file containing the app's `Word`, `Session`, and `ReviewSession` tables.
- Maps the `Flagged` column to a boolean representation.
- Optional JSON output containing 50 randomly selected words.
- Runs completely isolated from the main Astro project's dependencies.

## Prerequisites

Before running the tool for the first time, install its dependencies:

```bash
npm install
```

## Usage

You can run the script via the default NPM script or manually via Node.

### Running with NPM (Default paths)

This will read from `../../tmp/words.csv` and output the database to `../../tmp/words.db`. Copy that file to your configured `TEMPLATE_DB_PATH` (e.g. `db/template.db`).

```bash
npm start
```

### Running Manually (Custom paths)

```bash
node index.js <csvPath> <outDbPath> [outJsonPath]
```

**Arguments:**

1. `<csvPath>` (Required): Path to the input CSV file.
2. `<outDbPath>` (Required): Path to the output SQLite `.db` file.
3. `[outJsonPath]` (Optional): Path to output a JSON file containing 50 randomly selected words. If omitted, the JSON generation is skipped entirely.

**Examples:**

```bash
# Output only the database
node index.js ../../tmp/words.csv ../../tmp/init_prod.db

# Output both the database and the JSON sample
node index.js ../../tmp/words.csv ../../tmp/init_prod.db ../../db/sample.json
```
