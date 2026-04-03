# Project: SAT Vocab

## Project Overview
**SAT Vocab** is a single-user web application built with **Astro** to help users memorize ~3,000 SAT vocabulary words. It uses a session-based approach (10 words per session) where users rate their familiarity with each word.

- **Main Technologies:** [Astro](https://astro.build/), TypeScript, Node.js.
- **Architecture:** 
  - **Frontend:** Astro file-based routing.
  - **Backend/Data:** Local database (TBD) to store words and tracking statistics.
  - **Logic:** Custom selection algorithm to choose 10 words per session from the database.

## Building and Running
- **Install dependencies:** `npm install`
- **Start development server:** `npm run dev` (runs at `http://localhost:4321`)
- **Build for production:** `npm run build`
- **Astro CLI:** `npm run astro ...`

## Development Conventions
- **Language Mandate:** **ALL code, documentation, and comments MUST be in English.**
- **Structure:**
  - `src/pages/`: Contains the routes.
    - `index.astro`: Home page with "Start" button.
    - `words.astro`: Main session page (planned).
  - `public/`: Static assets.
- **Data Model:**
  - `word`, `definition`, `example`
  - Statistics: `total_views`, `memorized_count`, `fuzzy_count`, `unknown_count`.
- **UI/UX:** Focus on simplicity and visual clarity for the word cards.
- **TypeScript:** Strict typing (extends `astro/tsconfigs/strict`).
