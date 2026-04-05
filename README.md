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
| `npm run start`     | Start the production server on a VPS             |
| `npm run preview`   | Preview your build locally, before deploying     |
| `npm run astro ...` | Run CLI commands like `astro add`, `astro check` |

## 🌐 VPS Deployment & Data Persistence

To deploy this application to a VPS and ensure your vocabulary progress is not lost during redeployments, follow these steps:

### 1. Configure Persistent Database

By default, Astro DB uses a local file that is recreated on every build. To persist data across deployments:

1. Create a directory for your database outside of the project folder (e.g., `/var/lib/sat-vocab/`).
2. Set the `ASTRO_DB_REMOTE_URL` environment variable to point to this location:
    ```env
    ASTRO_DB_REMOTE_URL="file:///var/lib/sat-vocab/data.db"
    ```

### 2. Deployment Steps

1. **Build the project**: `npm run build`
2. **Start the server**: `npm run start` (uses the Node.js standalone adapter)

### 3. Environment Variables

- `PORT`: (Optional) The port the server will listen on (defaults to 4321).
- `HOST`: (Optional) The host the server will bind to (defaults to `0.0.0.0`).
- `ASTRO_DB_REMOTE_URL`: **Required** for data persistence on a VPS.
