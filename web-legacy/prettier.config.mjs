// Prettier replaces rather than merges configs, so the repository root options are
// repeated here. The only addition is the Astro plugin, which lives with the legacy app
// because it is the only package that has .astro files — and it goes away at cutover.
export default {
    useTabs: false,
    tabWidth: 4,
    semi: true,
    singleQuote: false,
    trailingComma: "es5",
    printWidth: 120,
    plugins: ["prettier-plugin-astro"],
    overrides: [
        {
            files: "*.astro",
            options: {
                parser: "astro",
            },
        },
    ],
};
