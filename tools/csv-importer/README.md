# CSV Importer Tool

A standalone Node.js script designed to parse a vocabulary CSV file and generate a database compatible with Astro DB (LibSQL). It can also optionally output a small, randomized JSON file of 50 words for development seeding.

## Features
- Uses `@libsql/client` to generate an SQLite `.db` file that exactly matches the schema of Astro DB.
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
This will read from `../../tmp/words.csv`, output the database to `../../tmp/words.db`, and output the 50 random words to `../../db/words.json`.
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
