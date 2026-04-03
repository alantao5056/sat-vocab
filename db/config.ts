import { defineDb, defineTable, column } from "astro:db";

const Word = defineTable({
    columns: {
        id: column.number({ primaryKey: true }),
        word: column.text(),
        definition: column.text(),
        example: column.text(),
        flagged: column.boolean({ default: false }),
        total_views: column.number({ default: 0 }),
        memorized_count: column.number({ default: 0 }),
        fuzzy_count: column.number({ default: 0 }),
        unknown_count: column.number({ default: 0 }),
        selection_weight: column.number({ default: 50 }),
    },
});

// https://astro.build/db/config
export default defineDb({
    tables: {
        Word,
    },
});
