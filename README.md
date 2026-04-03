# SAT Vocab

SAT Vocab is a minimalist web application designed to help users efficiently memorize SAT vocabulary words.

## 🚀 Project Overview

The goal of this project is to provide a focused environment for mastering approximately 3,000 SAT words through a simple, iterative learning process.

### Features (Version 1)

- **Single User Experience:** Simple and direct workflow without complex authentication.
- **Home Page:** Features the app name, a brief description, and a "Start" button to begin learning.
- **Word Session:**
    - Displays **10 word cards** per session.
    - Each card provides three selection options for the user:
        - **Memorized**
        - **Fuzzy**
        - **Unknown**
    - Users submit their ratings for all 10 words to proceed to the next set.

### Technical Details

- **Frontend:** Built with [Astro](https://astro.build/).
- **Database:** A local database (TBD) containing ~3,000 words.
- **Data Schema:** Each word entry includes:
    - `word`: The vocabulary word.
    - `definition`: The meaning of the word.
    - `example`: A usage example.
    - `total_views`: Total times the word has been displayed.
    - `memorized_count`: Times marked as "Memorized".
    - `fuzzy_count`: Times marked as "Fuzzy".
    - `unknown_count`: Times marked as "Unknown".
- **Algorithm:** Words are dynamically selected from the local database using a priority/selection algorithm to optimize learning.

## 🧞 Commands

All commands are run from the root of the project:

| Command             | Action                                           |
| :------------------ | :----------------------------------------------- |
| `npm install`       | Installs dependencies                            |
| `npm run dev`       | Starts local dev server at `localhost:4321`      |
| `npm run build`     | Build your production site to `./dist/`          |
| `npm run preview`   | Preview your build locally, before deploying     |
| `npm run astro ...` | Run CLI commands like `astro add`, `astro check` |
