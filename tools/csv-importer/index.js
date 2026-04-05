import fs from "node:fs";
import path from "node:path";
import csv from "csv-parser";
import { createClient } from "@libsql/client";

const args = process.argv.slice(2);
if (args.length < 2) {
    console.error("Usage: node index.js <csvPath> <outDbPath> [outJsonPath]");
    process.exit(1);
}

const csvPath = path.resolve(process.cwd(), args[0]);
const outDbPath = path.resolve(process.cwd(), args[1]);
const outJsonPath = args[2] ? path.resolve(process.cwd(), args[2]) : null;

const results = [];

console.log(`Reading CSV from ${csvPath}...`);

fs.createReadStream(csvPath)
    .pipe(csv({ mapHeaders: ({ header }) => header.trim().toLowerCase() }))
    .on("data", (data) => results.push(data))
    .on("end", async () => {
        console.log(`Parsed ${results.length} rows.`);

        const wordsToInsert = results
            .map((row) => {
                const flaggedVal = (row.flagged || "").toLowerCase();
                const isFlagged = flaggedVal === "yes" ? 1 : 0;
                return {
                    word: (row.word || "").trim(),
                    definition: (row.definition || "").trim(),
                    example: (row.example || "").trim(),
                    flagged: isFlagged,
                    total_views: 0,
                    memorized_count: 0,
                    fuzzy_count: 0,
                    unknown_count: 0,
                    selection_weight: 50,
                };
            })
            .filter((row) => row.word !== "");

        console.log(`Found ${wordsToInsert.length} valid words.`);

        // Write to SQLite via LibSQL
        console.log(`Initializing database at ${outDbPath}...`);
        const db = createClient({ url: "file:" + outDbPath });

        await db.execute(`
        CREATE TABLE IF NOT EXISTS "Word" (
            "id" INTEGER PRIMARY KEY,
            "word" TEXT NOT NULL,
            "definition" TEXT NOT NULL,
            "example" TEXT NOT NULL,
            "flagged" BOOLEAN NOT NULL DEFAULT 0,
            "total_views" INTEGER NOT NULL DEFAULT 0,
            "memorized_count" INTEGER NOT NULL DEFAULT 0,
            "fuzzy_count" INTEGER NOT NULL DEFAULT 0,
            "unknown_count" INTEGER NOT NULL DEFAULT 0,
            "selection_weight" INTEGER NOT NULL DEFAULT 50
        )
    `);

        // Clear existing
        await db.execute('DELETE FROM "Word"');

        // Batch insert
        console.log("Inserting records...");
        const statements = wordsToInsert.map((row) => {
            return {
                sql: `INSERT INTO "Word" (word, definition, example, flagged, total_views, memorized_count, fuzzy_count, unknown_count, selection_weight) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)`,
                args: [
                    row.word,
                    row.definition,
                    row.example,
                    row.flagged,
                    row.total_views,
                    row.memorized_count,
                    row.fuzzy_count,
                    row.unknown_count,
                    row.selection_weight,
                ],
            };
        });

        // Execute in transaction to be fast and safe
        await db.batch(statements, "write");
        console.log(`\u2714 Successfully inserted ${wordsToInsert.length} words into ${outDbPath}`);

        // If outJsonPath provided, select 50 random words and write to JSON
        if (outJsonPath) {
            const shuffled = [...wordsToInsert].sort(() => 0.5 - Math.random());
            const jsonWords = shuffled.slice(0, 50);

            fs.writeFileSync(outJsonPath, JSON.stringify(jsonWords, null, 4));
            console.log(
                `\u2714 Successfully generated JSON seed data with ${jsonWords.length} words at: ${outJsonPath}`
            );
        } else {
            console.log(`Skipped JSON generation (no path provided).`);
        }
    });
