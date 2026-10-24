import {getModule} from "cs2/modding";
import {bindValue, trigger, useValue} from "cs2/api";
import {FocusBoundary, NavigationScope} from "cs2/input";
import {LocalizedNumber, LocalizedPercentage, LocalizedString, Unit} from "cs2/l10n";
import mod from "../../mod.json";
import {InputField} from "../util/input-field";
import {setVal} from "../api";
import styles from "./server-browser.module.scss";

// Bindings shared by the join screen's parts (UISystem.cs)
export const joinMenuVisible = bindValue<boolean>(mod.id, 'JoinMenuVisible');
export const modSupport = bindValue<Array<any>>(mod.id, 'modSupport');
export const ipAddress = bindValue<string>(mod.id, 'JoinIpAddress');
export const port = bindValue<number>(mod.id, 'JoinPort');
export const username = bindValue<string>(mod.id, 'Username');
export const playerStatus = bindValue<string>(mod.id, 'PlayerStatus');
export const downloadDone = bindValue<number>(mod.id, 'DownloadDone');
export const downloadRemaining = bindValue<number>(mod.id, 'DownloadRemaining');
export const downloadSpeed = bindValue<number>(mod.id, 'DownloadSpeed');
export const joinErrorMessage = bindValue<Array<string>>(mod.id, 'JoinErrorMessage');
export const joinPassword = bindValue<string>(mod.id, 'JoinPassword', '');

export function hideJoinGame() {
    trigger(mod.id, "HideJoinGameMenu");
}

export function setIntVal(name: string, value: any) {
    trigger(mod.id, name, parseInt(value));
}

export function joinGame() {
    trigger(mod.id, "JoinGame");
}

/** Password for joining a locked session (Direct connect tab and locked browser sessions). */
export const PasswordField = ({enabled}: {enabled: boolean}) => {
    const value = useValue(joinPassword);
    return <InputField label={"CS2M.UI.Password"} value={value} disabled={!enabled} maxLength={64}
                       onChange={(val: any) => setVal("SetJoinPassword", val)}></InputField>;
}

/** The IP address and port join (Direct connect tab). */
export const JoinGameSettings = () => {
    const ipAddressValue = useValue(ipAddress);
    const portValue = useValue(port);
    const status = useValue(playerStatus);
    const enabled = status == "INACTIVE";

    return (
        <FocusBoundary onFocusChange={() => {
        }}>
            <NavigationScope focused={null} onChange={() => {
            }}>
                <InputField label={"CS2M.UI.IPAddress"} value={ipAddressValue} disabled={!enabled}
                            onChange={(val: any) => {
                                setVal("SetJoinIpAddress", val)
                            }}></InputField>
                <InputField label={"CS2M.UI.Port"} value={portValue} disabled={!enabled}
                            onChange={(val: any) => {
                                setIntVal("SetJoinPort", val)
                            }}></InputField>
                <PasswordField enabled={enabled}/>
            </NavigationScope>
        </FocusBoundary>
    );
}

/** Why the last join failed (precondition errors from the host, or connection errors). */
export const JoinMessages = () => {
    const errMsg = useValue(joinErrorMessage);
    if (errMsg.length == 0) {
        return null;
    }

    const messages = [];
    for (let i = 0; i < errMsg.length; i++) {
        let err = errMsg[i];
        let message;
        if (err.startsWith("precondition:")) {
            err = err.substring(13);
            switch (err) {
                case "GAME_VERSION_MISMATCH":
                case "MOD_VERSION_MISMATCH": {
                    const err_id = "CS2M.UI.JoinError." + err;
                    message = <LocalizedString id={err_id} args={{SERVER: errMsg[++i], CLIENT: errMsg[++i]}}/>;
                    break;
                }
                case "DLCS_MISMATCH":
                case "MODS_MISMATCH": {
                    const err_id = "CS2M.UI.JoinError." + err;
                    message = <LocalizedString id={err_id}/>;
                    const serverList = errMsg[++i];
                    const clientList = errMsg[++i];
                    if (serverList != '') {
                        const err_id = "CS2M.UI.JoinError." + err + ".server";
                        message = <>{message} <LocalizedString id={err_id} args={{SERVER: serverList}}/></>;
                    }
                    if (clientList != '') {
                        const err_id = "CS2M.UI.JoinError." + err + ".client";
                        message = <>{message} <LocalizedString id={err_id} args={{CLIENT: clientList}}/></>;
                    }
                    break;
                }
                case "USERNAME_NOT_AVAILABLE":
                case "PASSWORD_INCORRECT":
                case "SERVER_FULL": {
                    const err_id = "CS2M.UI.JoinError." + err;
                    message = <LocalizedString id={err_id}/>;
                    break;
                }
            }
        } else {
            message = <LocalizedString id={err}/>;
        }
        if (message) {
            messages.push(<div key={i} className={styles.error}>{message}</div>);
        }
    }

    return (
        <div className={styles.messages}>
            <div className={styles.error}><LocalizedString id={"CS2M.UI.JoinError.Intro"}/></div>
            {messages}
        </div>
    );
}

/** While joining: what's happening, with the download progress. Null when not joining. */
export const JoinStatus = () => {
    const status = useValue(playerStatus);
    const dlDone = useValue(downloadDone);
    const dlRemaining = useValue(downloadRemaining);
    const dlSpeed = useValue(downloadSpeed);

    if (status == "INACTIVE") {
        return null;
    }

    const statusStr = <LocalizedString id={"CS2M.UI.JoinStatus[" + status + "]"}/>;
    if (status != "DOWNLOADING_MAP") {
        return <div className={styles.status}>{statusStr}</div>;
    }

    const dlTotal = dlDone + dlRemaining;
    const percent = Math.round(100 * dlDone / (dlTotal == 0 ? 1 : dlTotal));
    const mib = (bytes: number) => (bytes / 1024 / 1024).toFixed(1);
    // one string per line: separate text nodes each get their own line in this layout
    return (
        <div className={styles.status}>
            <div className={styles.statusLine}>{statusStr}<span>{` (${percent} %)`}</span></div>
            {dlTotal > 0 && <div className={styles.muted}>{`${mib(dlDone)} MiB / ${mib(dlTotal)} MiB (${mib(dlSpeed)} MiB/s)`}</div>}
        </div>
    );
}

/** This game's DLC and mod compatibility as CS2M sees it (Direct connect tab, where there's no host to compare with). */
export const LocalCompatibility = () => {
    const Field = getModule('game-ui/menu/components/shared/detail-section/detail-section.tsx', 'Field');
    const modSupports = useValue(modSupport);

    return (
        <>
            {modSupports.map((support: any, i: number) => {
                let supportStr = <LocalizedString id={"CS2M.UI.Compatibility[" + support.support + "]"}/>;
                if (support.client_side) {
                    supportStr = <LocalizedString id={"CS2M.UI.ClientSide"}/>;
                }
                let color;
                switch (support.support) {
                    case "Unsupported":
                        color = "red";
                        break;
                    case "Supported":
                        color = "green";
                        break;
                    case "KnownWorking":
                        color = "yellow";
                        break;
                    default:
                        color = "orange";
                        break;
                }
                return <div key={i} style={{color: color}}><Field label={support.name}>{supportStr}</Field></div>;
            })}
        </>
    );
}
