// Server browser data: the lobby's session list, this game's mods and versions, and how they compare.
// Everything here is plain data, the screens are in server-browser.tsx.

export interface LobbyPlayer {
    name: string;
    color: string | null;
}

export interface LobbyMod {
    id: string;
    name: string | null;
    version: string | null;
    clientSide: boolean;
}

// Sent by hosts on CS2M versions with SessionDetailsCommand; null for older hosts
export interface LobbyDetails {
    population: number;
    milestone: number;
    year: number;
    month: number; // 0-11
    playerList: LobbyPlayer[];
    mods: LobbyMod[];
    modCount: number;
}

export interface LobbySession {
    token: string;
    name: string | null;
    host: string | null;
    players: number;
    maxPlayers: number;
    hasPassword: boolean;
    gameVersion: string | null;
    modVersion: string | null;
    details: LobbyDetails | null;
}

export interface LocalMod {
    id: string;
    name: string;
    version: string;
    clientSide: boolean;
}

export interface LocalVersions {
    game: string;
    mod: string;
}

export interface RecentEntry {
    token: string;
    ip: string;
    port: number;
    name: string;
    host: string;
    time: number; // unix seconds
}

export type ModState = "installed" | "different" | "missing" | "client";

export interface ModCheck {
    id: string;
    name: string;
    version: string;
    state: ModState;
    localVersion: string | null;
}

export interface SessionCheck {
    gameOk: boolean;
    modOk: boolean;
    mods: ModCheck[];
    missing: number;
    different: number;
    // mods the host has but couldn't list (its list is capped)
    unlisted: number;
    // uses mods besides CS2M and client side ones: shown in the Modded tab
    modded: boolean;
    full: boolean;
    compatible: boolean;
}

function parseList<T>(json: string, valid: (item: any) => boolean): T[] {
    try {
        const list = JSON.parse(json);
        return Array.isArray(list) ? list.filter(item => item && valid(item)) : [];
    } catch {
        return [];
    }
}

export function parseSessions(json: string): LobbySession[] {
    return parseList<LobbySession>(json, s => typeof s.token === "string").map(s => ({
        ...s,
        details: s.details && Array.isArray(s.details.mods) && Array.isArray(s.details.playerList) ? s.details : null
    }));
}

export function parseLocalMods(json: string): LocalMod[] {
    return parseList<LocalMod>(json, m => typeof m.id === "string");
}

export function parseRecent(json: string): RecentEntry[] {
    return parseList<RecentEntry>(json, r => typeof r.token === "string" || typeof r.ip === "string");
}

export function parseVersions(json: string): LocalVersions {
    try {
        const v = JSON.parse(json);
        return {game: v?.game ?? "", mod: v?.mod ?? ""};
    } catch {
        return {game: "", mod: ""};
    }
}

function sameText(a: string | null | undefined, b: string | null | undefined): boolean {
    return !!a && !!b && a.trim().toLowerCase() === b.trim().toLowerCase();
}

/**
 * Compares a session with this game: versions, and each of the host's mods against the enabled local ones
 * (by id, else by name, as a local copy of a Paradox mod has a different id).
 */
export function checkSession(session: LobbySession, local: LocalVersions, localMods: LocalMod[]): SessionCheck {
    const gameOk = !!session.gameVersion && session.gameVersion === local.game;
    const modOk = !!session.modVersion && session.modVersion === local.mod;
    const mods: ModCheck[] = [];
    let missing = 0;
    let different = 0;

    for (const mod of session.details?.mods ?? []) {
        if (isCs2mDependency(mod)) {
            continue;
        }
        const name = mod.name || mod.id;
        const version = mod.version ?? "";
        const mine = localMods.find(m => m.id === mod.id) ?? localMods.find(m => sameText(m.name, name));
        let state: ModState;
        if (mod.clientSide) {
            state = "client";
        } else if (!mine) {
            state = "missing";
            missing++;
        } else if (version && mine.version && version !== mine.version) {
            state = "different";
            different++;
        } else {
            state = "installed";
        }
        mods.push({id: mod.id, name, version, state, localVersion: mine?.version ?? null});
    }

    const listed = session.details?.mods.length ?? 0;
    const unlisted = Math.max(0, (session.details?.modCount ?? 0) - listed);
    const full = session.maxPlayers > 0 && session.players >= session.maxPlayers;
    return {
        gameOk,
        modOk,
        mods,
        missing,
        different,
        unlisted,
        modded: mods.some(m => m.state !== "client"),
        full,
        compatible: gameOk && modOk && missing === 0 && different === 0
    };
}

/** A recent lobby session that is online again, maybe under a new token after the host restarted. */
export function findLive(entry: RecentEntry, sessions: LobbySession[]): LobbySession | undefined {
    if (!entry.token) {
        return undefined;
    }
    return sessions.find(s => s.token === entry.token) ??
        sessions.find(s => !!entry.name && s.name === entry.name && s.host === entry.host);
}

// Mods every CS2M player has because CS2M needs them (older hosts still list them): never make a session "modded".
export function isCs2mDependency(mod: { id?: string | null, name?: string | null }): boolean {
    return mod.id === "75426" || /^i18n ?everywhere$/i.test((mod.name ?? "").trim());
}
