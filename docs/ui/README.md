# SAT Vocab UI Reference

This folder contains screenshots and references for the SAT Vocab application's user interface.

## Overview

The SAT Vocab app is a mobile-first web application designed to help users memorize SAT vocabulary words through interactive flashcards. The design uses a clean, light color palette with large tap targets suitable for touch screens.

## Screens and Workflows

### 1. Landing Page (`landing-page.jpg`)
- **Elements:**
  - Book icon.
  - App title: "SAT Vocab".
  - Subtitle: "Master your SAT vocabulary with interactive flashcards".
  - Primary Action: "Start Learning" button.
  - Helper text: "10 words • Tap cards to reveal definitions".

### 2. Main Study View (`study-page.jpg`, `study-page-parital-rating.jpg`)
- **Layout:** A 2-column grid of vocabulary cards.
- **Card Elements:**
  - The vocabulary word (e.g., Aberrant).
  - A rating bar with three self-assessment options:
    - ✅ (Green Checkmark): Known
    - 🤔 (Yellow Thinking Face): Unsure/Fuzzy
    - ❓ (Red Question Mark): Unknown
- **Interaction:** Selecting an option highlights its background with a corresponding color.
- **Global Action:** A sticky "Submit Progress" button at the bottom of the screen.

### 3. Incomplete Submission Handling
When a user attempts to submit progress without rating all 10 cards, they encounter validation flows to ensure completeness.

#### Validation Error (`study-page-parital-rating-submit-canceled.jpg`)
- **Error Banner:** A red alert banner at the top reads "Please rate all cards before submitting."
- **Card Highlight:** Any unrated cards are outlined in red to draw the user's attention.

#### Bulk Rating Modal (`study-page-submit-warning.jpg`, `study-page-submit-warning-mark-all.jpg`)
- **Dialog:** A modal overlay appears warning the user: "You have X cards that are not rated yet. Do you want to mark them all with the same rating?"
- **Interaction:**
  - The user can select one of the three rating options (✅, 🤔, ❓).
  - The "Yes, Mark All" button (initially grayed out) becomes active (purple) once a rating is chosen.
  - The user can cancel out of the modal using the "Cancel" button.
