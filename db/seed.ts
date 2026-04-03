import { db, Word } from "astro:db";
import wordsData from "./words.json";

// https://astro.build/db/config
export default async function seed() {
    console.log(`Seeding database with ${wordsData.length} words from words.json...`);
    
    // Map the JSON data so that 'flagged' maps to a boolean as expected by Astro DB
    const formattedData = wordsData.map(word => ({
        ...word,
        flagged: word.flagged === 1
    }));

    // Insert in chunks of 500 to avoid SQLite limits
    const chunkSize = 500;
    for (let i = 0; i < formattedData.length; i += chunkSize) {
        const chunk = formattedData.slice(i, i + chunkSize);
        await db.insert(Word).values(chunk);
    }
    
    console.log("Seeding complete!");
}
