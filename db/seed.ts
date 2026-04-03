import { db, Word } from "astro:db";

// https://astro.build/db/config
export default async function seed() {
    await db.insert(Word).values([
        {
            word: "abate",
            definition: "to become less active, less intense, or less in amount",
            example: "As I began my speech, my feelings of nervousness quickly abated.",
        },
        {
            word: "abstract",
            definition: "existing purely in the mind; not representing actual reality",
            example: "Julie had trouble understanding the appeal of the abstract painting.",
        },
        {
            word: "abysmal",
            definition: "extremely bad",
            example: "I got an abysmal grade on my research paper!",
        },
        {
            word: "advent",
            definition: "the arrival or creation of something",
            example: "The advent of the personal computer changed the world.",
        },
        {
            word: "adversarial",
            definition: "relating to hostile opposition",
            example: "An adversarial attitude will make you many enemies in life.",
        },
        {
            word: "advocate",
            definition: "someone who promotes or defends something",
            example: "I am an advocate for free higher education.",
        },
        {
            word: "allude",
            definition: "to make a secretive mention of something",
            example: "She alluded to her past at the wedding.",
        },
        {
            word: "ambivalence",
            definition: "the state of having contradictory or conflicting emotional attitudes",
            example: "His ambivalence made it difficult for him to decide which job to take.",
        },
        {
            word: "analogous",
            definition: "comparable in certain respects",
            example:
                "The relationship between a ruler and their subjects is analogous to that of a shepherd and their sheep.",
        },
        {
            word: "annihilate",
            definition: "to destroy or cause to devastate or vanish",
            example: "The atomic bomb annihilated the city.",
        },
        {
            word: "anomaly",
            definition: "something that is different from the norm",
            example: "The three-legged dog was an anomaly in the neighborhood.",
        },
        {
            word: "apex",
            definition: "the highest point of something",
            example: "The apex of the mountain was covered in snow.",
        },
        {
            word: "articulate",
            definition: "clearly expressed or pronounced",
            example: "She is an articulate speaker who can explain complex ideas clearly.",
        },
        {
            word: "benevolent",
            definition: "kind, generous",
            example: "The benevolent billionaire donated millions to charity.",
        },
        {
            word: "candor",
            definition: "the quality of being honest and straightforward",
            example: "I admire her candor, even when it is uncomfortable.",
        },
    ]);
}
