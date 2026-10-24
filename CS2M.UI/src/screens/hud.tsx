import {bindValue, trigger, useValue} from "cs2/api";
import {getModule} from "cs2/modding";
import {useLocalization} from "cs2/l10n";
import {useEffect, useState} from "react";
import {setVal} from "api";
import {format} from "../util/format";
import mod from "../../mod.json";
import styles from "./hud.module.scss";
import {ResyncButton, ResyncStatus} from "./resync";
import {DisconnectNotice, PlayerActions} from "./moderation";

interface Player {
    n: string;   // name
    c: string;   // cursor colour
    a: string;   // activity
    me: boolean; // this game's player
    id?: number; // the id the host stamps on this player's messages (not set for yourself)
}

interface Message {
    timestamp: string;
    user: string;
    text: string;
}

const playersBinding = bindValue<string>(mod.id, 'Players', '[]');
const collapsedBinding = bindValue<boolean>(mod.id, 'HudCollapsed', false);
const chatMessagesBinding = bindValue<Array<Message>>(mod.id, 'ChatMessages', []);
const localChatMessageBinding = bindValue<string>(mod.id, 'LocalChatMessage');

const VISIBLE_MESSAGES = 6;

// Multiplayer HUD: who's in the session (with their cursor colours) and chat. Hidden outside a session.
export const MultiplayerHud = () => {
    const StringInputField = getModule('game-ui/editor/widgets/fields/string-input-field.tsx', 'StringInputField');
    const {translate} = useLocalization();

    const collapsed = useValue(collapsedBinding);
    const messages = useValue(chatMessagesBinding) ?? [];
    const localMessage = useValue(localChatMessageBinding);
    let players: Player[] = [];
    try {
        players = JSON.parse(useValue(playersBinding));
    } catch {
        players = [];
    }

    // unread count while collapsed
    const [seen, setSeen] = useState(messages.length);
    useEffect(() => {
        if (!collapsed) setSeen(messages.length);
    }, [collapsed, messages.length]);
    const unread = Math.max(0, messages.length - seen);

    if (players.length === 0) {
        return <><ResyncStatus/><DisconnectNotice/></>;
    }

    const colourOf = (user: string) => players.find(p => p.n.replace(" (host)", "") === user)?.c;
    const send = () => trigger(mod.id, "SendChatMessage");

    if (collapsed) {
        return (
            <button className={styles.collapsed} onClick={() => setVal("SetHudCollapsed", false)}>
                <span className={styles.dot}/>
                <span>{players.length}</span>
                {unread > 0 && <span className={styles.badge}>{unread}</span>}
            </button>
        );
    }

    return (
        <div className={styles.hud}>
            <div className={styles.header}>
                <span className={styles.title}>{format(translate(players.length === 1 ? "CS2M.UI.Hud.Title.One" : "CS2M.UI.Hud.Title.Many"), {N: players.length})}</span>
                <ResyncButton host={players.some(p => p.me && p.n.endsWith(" (host)"))}/>
                <button className={styles.hide} onClick={() => setVal("SetHudCollapsed", true)}>–</button>
            </div>
            <div className={styles.players}>
                {players.map(player => (
                    <div key={player.n} className={styles.player}>
                        <span className={styles.swatch} style={{backgroundColor: player.c}}/>
                        <span className={styles.name}>{player.me ? `${player.n} ${translate("CS2M.UI.Hud.You") ?? ""}` : player.n}</span>
                        {player.a && <span className={styles.activity}>{player.a}</span>}
                        {!player.me && player.id !== undefined && <PlayerActions id={player.id} name={player.n}/>}
                    </div>
                ))}
            </div>
            <div className={styles.messages}>
                {messages.slice(-VISIBLE_MESSAGES).map((message, i) => message.user === "CS2M" ? (
                    // notices (joined, left, server started) without a sender
                    <div key={i} className={styles.notice}>{message.text}</div>
                ) : (
                    <div key={i} className={styles.message}>
                        <span className={styles.user} style={{color: colourOf(message.user) ?? "#9aa08f"}}>{message.user}</span>
                        <span className={styles.text}>{message.text}</span>
                    </div>
                ))}
            </div>
            <div className={styles.input}>
                <StringInputField
                    className={styles.field}
                    value={localMessage}
                    placeholder={translate("CS2M.UI.ChatPanel.ChatMessageInput")}
                    onChange={(val: any) => setVal("SetLocalChatMessage", val)}/>
                <button className={styles.send} onClick={send}>{translate("CS2M.UI.ChatPanel.SendMessage")}</button>
            </div>
        </div>
    );
}
