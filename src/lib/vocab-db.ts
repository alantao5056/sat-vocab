import { createClient, type Client } from "@libsql/client";
import path from "node:path";

/**
 * Convert a filesystem path (or an existing `file:` URL) into a libsql client URL.
 * Relative paths are resolved against the current working directory.
 */
export function toLibsqlUrl(filePath: string): string {
    if (filePath.startsWith("file:")) return filePath;
    return "file:" + path.resolve(filePath);
}

// Reuse one client per database file across requests.
const clients = new Map<string, Client>();

/**
 * Get a libsql client for a per-user vocabulary database file. Clients are cached
 * by resolved path so repeated requests share a single connection.
 */
export function getVocabDb(dbPath: string): Client {
    const url = toLibsqlUrl(dbPath);
    let client = clients.get(url);
    if (!client) {
        client = createClient({ url });
        clients.set(url, client);
    }
    return client;
}
