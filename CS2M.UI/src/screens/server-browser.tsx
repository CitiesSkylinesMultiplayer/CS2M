import {useEffect, useState} from "react";
import {getModule} from "cs2/modding";
import {bindValue, trigger, useValue} from "cs2/api";
import {LocalizedDate, LocalizedNumber, Unit, useLocalization} from "cs2/l10n";
import {Scrollable} from "cs2/ui";
import mod from "../../mod.json";
import {setVal} from "../api";
import {format} from "../util/format";
import styles from "./server-browser.module.scss";
import {
    checkSession,
    findLive,
    LobbySession,
    LocalVersions,
    ModCheck,
    parseLocalMods,
    parseRecent,
    parseSessions,
    parseVersions,
    RecentEntry,
    SessionCheck
} from "./browser-data";
import {
    ipAddress,
    joinGame,
    JoinGameSettings,
    JoinMessages,
    JoinStatus,
    LocalCompatibility,
    PasswordField,
    playerStatus,
    port,
    username
} from "./join-parts";

// Sessions from {LobbyUrl}/sessions, fetched by UISystem.RefreshLobby
const sessionsBinding = bindValue<string>(mod.id, 'LobbySessions', '[]');
const statusBinding = bindValue<string>(mod.id, 'LobbyStatus', 'idle');
// This game's enabled mods and versions, to compare with each session (UISystem.UpdateLocalMods)
const localModsBinding = bindValue<string>(mod.id, 'LocalMods', '[]');
const localVersionsBinding = bindValue<string>(mod.id, 'LocalVersions', '{}');
const recentBinding = bindValue<string>(mod.id, 'RecentSessions', '[]');

const AUTO_REFRESH_SECONDS = 30;
const HOST_COLOR = "#D6FF3F";
const OTHER_COLOR = "#9AA08F";
const OK_ICON = "Media/Glyphs/Checkmark.svg";
const NO_ICON = "Media/Glyphs/Close.svg";

type Tab = "browse" | "modded" | "direct" | "recent";

type Translate = (id: string, fallback?: string | null) => string | null;

function refreshLobby() {
    trigger(mod.id, "RefreshLobby");
}

function joinSession(session: LobbySession) {
    trigger(mod.id, "JoinSession", session.token, session.name ?? "", session.host ?? "");
}

function joinAddress(ip: string, portNumber: number) {
    setVal("SetJoinIpAddress", ip);
    trigger(mod.id, "SetJoinPort", portNumber);
    joinGame();
}

function playersText(session: LobbySession): string {
    return session.maxPlayers > 0 ? `${session.players} / ${session.maxPlayers}` : `${session.players}`;
}

function milestoneName(translate: Translate, milestone: number): string {
    return translate("Progression.MILESTONE_NAME:" + milestone, null) ??
        format(translate("CS2M.UI.Browser.Milestone"), {N: milestone});
}

/** Why the join button is disabled for this session, or null. The host's join check stays the real gate. */
function blockReason(translate: Translate, session: LobbySession, check: SessionCheck, local: LocalVersions): string | null {
    if (!check.gameOk) {
        return format(translate("CS2M.UI.Browser.Reason.Game"), {SERVER: session.gameVersion ?? "?", CLIENT: local.game});
    }
    if (!check.modOk) {
        return format(translate("CS2M.UI.Browser.Reason.Mod"), {SERVER: session.modVersion ?? "?", CLIENT: local.mod});
    }
    if (check.missing > 0) {
        return format(translate("CS2M.UI.Browser.Reason.Missing"), {N: check.missing});
    }
    if (check.full) {
        return translate("CS2M.UI.Browser.Reason.Full");
    }
    return null;
}

/** A small glyph from the game's icons, tinted through a mask. */
const Mark = ({ok}: { ok: boolean }) => {
    const src = `url(${ok ? OK_ICON : NO_ICON})`;
    return <span className={styles.mark + " " + (ok ? styles.markOk : styles.markBad)}
                 style={{maskImage: src, WebkitMaskImage: src}}/>;
}

const ModRow = ({check}: { check: ModCheck }) => {
    const {translate} = useLocalization();
    let state;
    switch (check.state) {
        case "installed":
            state = <span className={styles.ok}>{translate("CS2M.UI.Browser.Mod.Installed")} <Mark ok={true}/></span>;
            break;
        case "different":
            state = <span className={styles.warn}>
                {format(translate("CS2M.UI.Browser.Mod.Different"), {CLIENT: check.localVersion ?? "?"})} !
            </span>;
            break;
        case "missing":
            state = <span className={styles.bad}>{translate("CS2M.UI.Browser.Mod.Missing")} <Mark ok={false}/></span>;
            break;
        default:
            state = <span className={styles.muted}>{translate("CS2M.UI.Browser.Mod.ClientSide")}</span>;
            break;
    }
    return (
        <div className={styles.mod}>
            <span className={styles.modName}>{check.name + (check.version ? " " + check.version : "")}</span>
            <span className={styles.modState}>{state}</span>
        </div>
    );
}

// "1.6.2f1 (768.21d1) [6300.27778]" -> "1.6.2f1": the full string doesn't fit the column
function shortVersion(version?: string | null): string {
    return (version ?? "?").split(" ")[0];
}

const VersionBadge = ({session, check}: { session: LobbySession, check: SessionCheck }) => {
    const ok = check.gameOk && check.modOk;
    return (
        <span className={styles.badge + " " + (ok ? styles.badgeOk : styles.badgeNo)}>
            <Mark ok={ok}/>{shortVersion(session.gameVersion)}
        </span>
    );
}

const SessionRow = ({session, check, selected, onSelect}: {
    session: LobbySession, check: SessionCheck, selected: boolean, onSelect: () => void
}) => {
    const {translate} = useLocalization();
    const details = session.details;
    const off = !(check.gameOk && check.modOk);
    return (
        <div className={styles.row + (selected ? " " + styles.rowSelected : "") + (off ? " " + styles.rowOff : "")}
             onClick={onSelect}>
            <div className={styles.colCity}>
                <div className={styles.cityName}>
                    <span className={styles.cityText}>{session.name || translate("CS2M.UI.Lobby.Unnamed")}</span>
                    {session.hasPassword && <img className={styles.lock} src="Media/Glyphs/Lock.svg"/>}
                </div>
                {details && <div className={styles.citySub}>
                    {`${translate("CS2M.UI.Browser.Pop") ?? ""} ${details.population.toLocaleString()}` +
                        (details.milestone > 0 ? " · " + milestoneName(translate, details.milestone) : "")}
                </div>}
            </div>
            <div className={styles.colHost}>{session.host || "-"}</div>
            <div className={styles.colPlayers}>
                <span className={styles.dot + (check.full ? " " + styles.dotFull : "")}/>
                {playersText(session)}
            </div>
            <div className={styles.colVersion}><VersionBadge session={session} check={check}/></div>
        </div>
    );
}

const Kv = ({label, children}: { label: string | null, children: any }) => (
    <div className={styles.kv}>
        <span className={styles.kvKey}>{label}</span>
        <span className={styles.kvValue}>{children}</span>
    </div>
);

/** The right panel for a listed session. */
const SessionDetails = ({session, check}: { session: LobbySession, check: SessionCheck }) => {
    const {translate} = useLocalization();
    const details = session.details;
    const sub = format(translate("CS2M.UI.Browser.HostedBy"), {HOST: session.host || "-"}) + " · " +
        translate(session.hasPassword ? "CS2M.UI.Browser.Locked" : "CS2M.UI.Browser.Open");

    return (
        <div className={styles.detInner}>
            <div className={styles.detTitle}>{session.name || translate("CS2M.UI.Lobby.Unnamed")}</div>
            <div className={styles.detSub}>{sub}</div>
            <Kv label={translate("CS2M.UI.Lobby.Players")}>{playersText(session)}</Kv>
            {details && <>
                <Kv label={translate("CS2M.UI.Browser.Population")}>
                    <LocalizedNumber value={details.population} unit={Unit.Integer}/>
                </Kv>
                {details.milestone > 0 &&
                    <Kv label={translate("CS2M.UI.Browser.MilestoneLabel")}>{milestoneName(translate, details.milestone)}</Kv>}
                {details.year > 0 && <Kv label={translate("CS2M.UI.Browser.Date")}>
                    <LocalizedDate value={{year: details.year, month: details.month}}/>
                </Kv>}
            </>}
            <Kv label={translate("CS2M.UI.Browser.GameVersion")}>
                <span className={check.gameOk ? styles.ok : styles.bad}>
                    {session.gameVersion ?? "?"} <Mark ok={check.gameOk}/>
                </span>
            </Kv>
            <Kv label={translate("CS2M.UI.Browser.ModVersion")}>
                <span className={check.modOk ? styles.ok : styles.bad}>
                    {session.modVersion ?? "?"} <Mark ok={check.modOk}/>
                </span>
            </Kv>

            {details && details.playerList.length > 0 && <>
                <div className={styles.sec}>{translate("CS2M.UI.Browser.InSession")}</div>
                <div className={styles.players}>
                    {details.playerList.map((player, i) => (
                        <span key={i} className={styles.player}>
                            <span className={styles.swatch}
                                  style={{backgroundColor: player.color ?? (i == 0 ? HOST_COLOR : OTHER_COLOR)}}/>
                            {player.name + (i == 0 ? " " + (translate("CS2M.UI.Browser.HostTag") ?? "") : "")}
                        </span>
                    ))}
                </div>
            </>}

            <div className={styles.sec}>{translate("CS2M.UI.Browser.Mods")}</div>
            {!details && <div className={styles.muted}>{translate("CS2M.UI.Browser.NoModInfo")}</div>}
            {details && check.mods.length == 0 &&
                <div className={styles.muted}>{translate("CS2M.UI.Browser.NoMods")}</div>}
            {check.mods.map(m => <ModRow key={m.id} check={m}/>)}
            {check.unlisted > 0 &&
                <div className={styles.muted}>{format(translate("CS2M.UI.Browser.Unlisted"), {N: check.unlisted})}</div>}
        </div>
    );
}

/** Join status, errors and the big button, at the bottom of the right panel. */
const JoinArea = ({label, reason, onJoin, extra}: {
    label: string | null, reason: string | null, onJoin: (() => void) | null, extra?: any
}) => {
    const status = useValue(playerStatus);
    const idle = status == "INACTIVE";
    const enabled = idle && onJoin != null && reason == null;
    return (
        <div className={styles.joinArea}>
            <JoinStatus/>
            {idle && <JoinMessages/>}
            {idle && reason && <div className={styles.reason}>{reason}</div>}
            <button className={styles.join + (enabled ? "" : " " + styles.joinDisabled)}
                    onClick={() => {
                        if (enabled && onJoin) onJoin();
                    }}>
                {label}
            </button>
            {extra}
        </div>
    );
}

// Returned as a plain string: in these flex columns every separate text node lands on its own line
function lastJoined(translate: Translate, time: number): string {
    const minutes = Math.max(0, Math.floor((Date.now() / 1000 - time) / 60));
    if (minutes < 60) {
        return format(translate("CS2M.UI.Browser.MinutesAgo"), {N: minutes});
    }
    const hours = Math.floor(minutes / 60);
    if (hours < 48) {
        return format(translate("CS2M.UI.Browser.HoursAgo"), {N: hours});
    }
    return format(translate("CS2M.UI.Browser.DaysAgo"), {N: Math.floor(hours / 24)});
}

/**
 * The join screen inside the multiplayer SubScreen: "Playing as", the session list with its tabs and filters,
 * and the selected session with the join button.
 */
export const MultiplayerScreen = () => {
    const StringInputField = getModule('game-ui/editor/widgets/fields/string-input-field.tsx', 'StringInputField');
    const {translate} = useLocalization();

    const sessionsJson = useValue(sessionsBinding);
    const lobbyStatus = useValue(statusBinding);
    const sessions = parseSessions(sessionsJson);
    const localMods = parseLocalMods(useValue(localModsBinding));
    const local = parseVersions(useValue(localVersionsBinding));
    const recent = parseRecent(useValue(recentBinding));
    const usernameValue = useValue(username);
    const status = useValue(playerStatus);
    const ip = useValue(ipAddress);
    const portValue = useValue(port);
    const idle = status == "INACTIVE";

    const [tab, setTab] = useState<Tab>("browse");
    const [search, setSearch] = useState("");
    const [compatibleOnly, setCompatibleOnly] = useState(true);
    const [hideFull, setHideFull] = useState(false);
    const [hideLocked, setHideLocked] = useState(false);
    const [selected, setSelected] = useState<string | null>(null);
    const [selectedRecent, setSelectedRecent] = useState(0);
    const [updated, setUpdated] = useState<number | null>(null);
    const [now, setNow] = useState(Date.now());

    // "Updated N s ago", and a refresh every so often while a list is showing
    useEffect(() => {
        if (lobbyStatus === "ok") {
            setUpdated(Date.now());
        }
    }, [lobbyStatus, sessionsJson]);
    useEffect(() => {
        const timer = setInterval(() => setNow(Date.now()), 1000);
        return () => clearInterval(timer);
    }, []);
    const listing = tab != "direct";
    useEffect(() => {
        if (listing && idle && lobbyStatus === "ok" && updated != null && now - updated > AUTO_REFRESH_SECONDS * 1000) {
            refreshLobby();
        }
    }, [now]);

    const checks = new Map<string, SessionCheck>();
    for (const session of sessions) {
        checks.set(session.token, checkSession(session, local, localMods));
    }
    const checkOf = (session: LobbySession) => checks.get(session.token)!;

    const needle = search.trim().toLowerCase();
    const browse = sessions.filter(s => !checkOf(s).modded);
    const modded = sessions.filter(s => checkOf(s).modded);
    const tabSessions = tab == "modded" ? modded : browse;
    const shown = tabSessions.filter(s => {
        const check = checkOf(s);
        return (!needle || (s.name ?? "").toLowerCase().includes(needle) || (s.host ?? "").toLowerCase().includes(needle)) &&
            (!compatibleOnly || check.compatible) &&
            (!hideFull || !check.full) &&
            (!hideLocked || !s.hasPassword);
    });
    const selectedSession = shown.find(s => s.token == selected) ?? shown[0];

    const tabButton = (id: Tab, label: string, count?: number) => (
        <button className={styles.tab + (tab == id ? " " + styles.tabOn : "")} onClick={() => setTab(id)}>
            {translate(label)}{count != undefined && <span className={styles.tabCount}>{count}</span>}
        </button>
    );
    const chip = (on: boolean, set: (value: boolean) => void, label: string) => (
        <button className={styles.chip + (on ? " " + styles.chipOn : "")} onClick={() => set(!on)}>
            {translate(label)}
        </button>
    );

    // Left panel
    let left;
    if (tab == "browse" || tab == "modded") {
        let empty = null;
        if (lobbyStatus === "error") {
            empty = <div className={styles.empty}>
                <div className={styles.emptyTitle}>{translate("CS2M.UI.Browser.Unreachable")}</div>
                <div>{translate("CS2M.UI.Browser.UnreachableHint")}</div>
                <button className={styles.btn + " " + styles.emptyButton} onClick={refreshLobby}>
                    {translate("CS2M.UI.Browser.Retry")}
                </button>
            </div>;
        } else if (shown.length == 0) {
            const hidden = tabSessions.length;
            empty = <div className={styles.empty}>
                <div className={styles.emptyTitle}>
                    {lobbyStatus === "loading" && sessions.length == 0
                        ? translate("CS2M.UI.Lobby.Loading")
                        : translate(tab == "modded" ? "CS2M.UI.Browser.EmptyModded" : "CS2M.UI.Browser.Empty")}
                </div>
                {hidden > 0 && <div>{format(translate("CS2M.UI.Browser.Filtered"), {N: hidden})}</div>}
            </div>;
        }

        const seconds = updated == null ? null : Math.max(0, Math.floor((now - updated) / 1000));
        left = <>
            <div className={styles.bar}>
                <div className={styles.search}>
                    <StringInputField value={search} placeholder={translate("CS2M.UI.Browser.Search")}
                                      onChange={(val: any) => setSearch(String(val ?? ""))} maxLength={64}/>
                </div>
                {chip(compatibleOnly, setCompatibleOnly, "CS2M.UI.Browser.CompatibleOnly")}
                {chip(hideFull, setHideFull, "CS2M.UI.Browser.HideFull")}
                {chip(hideLocked, setHideLocked, "CS2M.UI.Browser.HideLocked")}
                <div className={styles.refresh}>
                    {lobbyStatus === "loading" ? translate("CS2M.UI.Lobby.Loading")
                        : seconds != null ? format(translate("CS2M.UI.Browser.Updated"), {N: seconds}) : ""}
                    <button className={styles.btn + (lobbyStatus === "loading" ? " " + styles.btnDisabled : "")}
                            onClick={refreshLobby}>
                        {translate("CS2M.UI.Lobby.Refresh")}
                    </button>
                </div>
            </div>
            <div className={styles.head}>
                <div className={styles.colCity}>{translate("CS2M.UI.Lobby.City")}</div>
                <div className={styles.colHost}>{translate("CS2M.UI.Lobby.Host")}</div>
                <div className={styles.colPlayers}>{translate("CS2M.UI.Lobby.Players")}</div>
                <div className={styles.colVersion}>{translate("CS2M.UI.Browser.Version")}</div>
            </div>
            <Scrollable className={styles.list} vertical={true}>
                {shown.map(session => (
                    <SessionRow key={session.token} session={session} check={checkOf(session)}
                                selected={session === selectedSession} onSelect={() => setSelected(session.token)}/>
                ))}
                {empty}
            </Scrollable>
            <div className={styles.foot}>
                {format(translate("CS2M.UI.Browser.Footer"), {N: sessions.length})}
            </div>
        </>;
    } else if (tab == "direct") {
        left = <div className={styles.direct}>
            <JoinGameSettings/>
            <div className={styles.note}>{translate("CS2M.UI.Browser.DirectHint")}</div>
        </div>;
    } else {
        left = <>
            <div className={styles.head}>
                <div className={styles.colCity}>{translate("CS2M.UI.Browser.Session")}</div>
                <div className={styles.colHost}>{translate("CS2M.UI.Lobby.Host")}</div>
                <div className={styles.colVersion}>{translate("CS2M.UI.Browser.Status")}</div>
            </div>
            <Scrollable className={styles.list} vertical={true}>
                {recent.map((entry, i) => {
                    const live = findLive(entry, sessions);
                    return (
                        <div key={i} className={styles.row + (i == selectedRecent ? " " + styles.rowSelected : "")}
                             onClick={() => setSelectedRecent(i)}>
                            <div className={styles.colCity}>
                                <div className={styles.cityName}>
                                    <span className={styles.cityText}>
                                        {entry.ip ? `${entry.ip}:${entry.port}` : entry.name || translate("CS2M.UI.Lobby.Unnamed")}
                                    </span>
                                </div>
                                <div className={styles.citySub}>
                                    {(translate(entry.ip ? "CS2M.UI.Browser.DirectEntry" : "CS2M.UI.Browser.LobbyEntry") ?? "") +
                                        (entry.time > 0 ? " · " + lastJoined(translate, entry.time) : "")}
                                </div>
                            </div>
                            <div className={styles.colHost}>{entry.host || "-"}</div>
                            <div className={styles.colVersion}>
                                {entry.ip ? null : live
                                    ? <span className={styles.badge + " " + styles.badgeOk}>{translate("CS2M.UI.Browser.Online")}</span>
                                    : <span className={styles.muted}>{translate("CS2M.UI.Browser.Offline")}</span>}
                            </div>
                        </div>
                    );
                })}
                {recent.length == 0 && <div className={styles.empty}>
                    <div className={styles.emptyTitle}>{translate("CS2M.UI.Browser.NoRecent")}</div>
                    <div>{translate("CS2M.UI.Browser.NoRecentHint")}</div>
                </div>}
            </Scrollable>
        </>;
    }

    // Right panel
    const joinLabel = translate("CS2M.UI.Browser.JoinGame");
    let details = null;
    let joinArea;
    if (tab == "browse" || tab == "modded") {
        if (selectedSession) {
            const check = checkOf(selectedSession);
            details = <>
                <SessionDetails session={selectedSession} check={check}/>
                {selectedSession.hasPassword && <div className={styles.detInner}><PasswordField enabled={true}/></div>}
            </>;
            joinArea = <JoinArea label={joinLabel} reason={blockReason(translate, selectedSession, check, local)}
                                 onJoin={() => joinSession(selectedSession)}/>;
        } else {
            details = <div className={styles.detInner}>
                <div className={styles.muted}>{translate("CS2M.UI.Browser.SelectSession")}</div>
            </div>;
            joinArea = <JoinArea label={joinLabel} reason={null} onJoin={null}/>;
        }
    } else if (tab == "direct") {
        details = <div className={styles.detInner}>
            <div className={styles.detTitle}>{translate("CS2M.UI.Browser.Tab.Direct")}</div>
            <div className={styles.detSub}>{ip ? `${ip}:${portValue}` : translate("CS2M.UI.Browser.DirectHint")}</div>
            <div className={styles.sec}>{translate("CS2M.UI.Compatibility")}</div>
            <LocalCompatibility/>
        </div>;
        joinArea = <JoinArea label={joinLabel} reason={ip ? null : translate("CS2M.UI.Browser.Reason.NoAddress")}
                             onJoin={joinGame}/>;
    } else {
        const index = recent[selectedRecent] ? selectedRecent : 0;
        const entry: RecentEntry | undefined = recent[index];
        const againLabel = translate("CS2M.UI.Browser.JoinAgain");
        const remove = entry && <button className={styles.remove} onClick={() => {
            trigger(mod.id, "RemoveRecent", index);
            setSelectedRecent(0);
        }}>{translate("CS2M.UI.Browser.Forget")}</button>;
        const live = entry ? findLive(entry, sessions) : undefined;
        if (!entry) {
            joinArea = <JoinArea label={againLabel} reason={null} onJoin={null}/>;
        } else if (entry.ip) {
            details = <div className={styles.detInner}>
                <div className={styles.detTitle}>{`${entry.ip}:${entry.port}`}</div>
                <div className={styles.detSub}>
                    {(translate("CS2M.UI.Browser.DirectEntry") ?? "") + (entry.time > 0 ? " · " + lastJoined(translate, entry.time) : "")}
                </div>
            </div>;
            joinArea = <JoinArea label={againLabel} reason={null} onJoin={() => joinAddress(entry.ip, entry.port)}
                                 extra={remove}/>;
        } else if (live) {
            const check = checkOf(live);
            details = <SessionDetails session={live} check={check}/>;
            joinArea = <JoinArea label={againLabel} reason={blockReason(translate, live, check, local)}
                                 onJoin={() => joinSession(live)} extra={remove}/>;
        } else {
            details = <div className={styles.detInner}>
                <div className={styles.detTitle}>{entry.name || translate("CS2M.UI.Lobby.Unnamed")}</div>
                <div className={styles.detSub}>
                    {format(translate("CS2M.UI.Browser.HostedBy"), {HOST: entry.host || "-"}) +
                        (entry.time > 0 ? " · " + lastJoined(translate, entry.time) : "")}
                </div>
            </div>;
            joinArea = <JoinArea label={againLabel} reason={translate("CS2M.UI.Browser.Reason.Offline")} onJoin={null}
                                 extra={remove}/>;
        }
    }

    return (
        <div className={styles.screen}>
            <div className={styles.top}>
                <div className={styles.who}>
                    <div className={styles.avatar}>{(usernameValue || "?").substring(0, 1).toUpperCase()}</div>
                    <span className={styles.whoLabel}>{translate("CS2M.UI.Browser.PlayingAs")}</span>
                    <div className={styles.whoInput}>
                        {idle
                            ? <StringInputField value={usernameValue} maxLength={32}
                                                placeholder={translate("CS2M.UI.Username")}
                                                onChange={(val: any) => setVal("SetUsername", val)}/>
                            : <span>{usernameValue}</span>}
                    </div>
                </div>
            </div>
            <div className={styles.grid}>
                <div className={styles.panel + " " + styles.main}>
                    <div className={styles.tabs}>
                        {tabButton("browse", "CS2M.UI.Browser.Tab.Browse", browse.length)}
                        {tabButton("modded", "CS2M.UI.Browser.Tab.Modded", modded.length)}
                        {tabButton("direct", "CS2M.UI.Browser.Tab.Direct")}
                        {tabButton("recent", "CS2M.UI.Browser.Tab.Recent", recent.length)}
                    </div>
                    {left}
                </div>
                <div className={styles.panel + " " + styles.side}>
                    <Scrollable className={styles.det} vertical={true}>
                        {details}
                    </Scrollable>
                    {joinArea}
                </div>
            </div>
        </div>
    );
}
