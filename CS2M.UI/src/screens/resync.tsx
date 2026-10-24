import {bindValue, trigger, useValue} from "cs2/api";
import {useLocalization} from "cs2/l10n";
import mod from "../../mod.json";
import {format} from "../util/format";
import styles from "./resync.module.scss";

const statusBinding = bindValue<string>(mod.id, 'PlayerStatus', 'INACTIVE');
const doneBinding = bindValue<number>(mod.id, 'DownloadDone', 0);
const remainingBinding = bindValue<number>(mod.id, 'DownloadRemaining', 0);

// Re-downloads the host's city to clear drift. On the host it sends the city to every joiner again.
export const ResyncButton = ({host}: { host: boolean }) => {
    const {translate} = useLocalization();
    return (
        <button
            className={styles.button}
            title={translate(host ? "CS2M.UI.Resync.AllHint" : "CS2M.UI.Resync.Hint") ?? ""}
            onClick={() => trigger(mod.id, "Resync")}>
            {translate(host ? "CS2M.UI.Resync.All" : "CS2M.UI.Resync.Button")}
        </button>
    );
};

// While a resync downloads or loads, the HUD has no session to show; this says what's happening instead.
// (Worded for the first join too: the game UI can mount while that one is still loading.)
export const ResyncStatus = () => {
    const {translate} = useLocalization();
    const status = useValue(statusBinding);
    const done = useValue(doneBinding);
    const remaining = useValue(remainingBinding);
    if (status !== "DOWNLOADING_MAP" && status !== "LOADING_MAP") {
        return null;
    }

    const total = done + remaining;
    const text = status === "LOADING_MAP"
        ? translate("CS2M.UI.Resync.Loading")
        : total > 0
            ? format(translate("CS2M.UI.Resync.DownloadingPercent"), {N: Math.floor(done * 100 / total)})
            : translate("CS2M.UI.Resync.Downloading");
    return <div className={styles.status}>{text}</div>;
}
