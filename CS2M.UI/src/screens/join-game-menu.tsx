import {getModule} from "cs2/modding";
import {bindValue, trigger, useValue} from "cs2/api";
import mod from "../../mod.json";
import {FocusBoundary, NavigationScope} from "cs2/input";
import {LocalizedNumber, LocalizedPercentage, LocalizedString, Unit, useLocalization} from "cs2/l10n";
import {InputField} from "../util/input-field";
import {setVal} from "api";

export const joinMenuVisible = bindValue<boolean>(mod.id, 'JoinMenuVisible');
export const modSupport = bindValue<Array<any>>(mod.id, 'modSupport');

export const ipAddress = bindValue<string>(mod.id, 'JoinIpAddress');
export const port = bindValue<number>(mod.id, 'JoinPort');
export const username = bindValue<string>(mod.id, 'Username');
export const playerStatus = bindValue<string>(mod.id, 'PlayerStatus');
export const downloadDone = bindValue<number>(mod.id, 'DownloadDone');
export const downloadRemaining = bindValue<number>(mod.id, 'DownloadRemaining');
export const joinErrorMessage = bindValue<Array<string>>(mod.id, 'JoinErrorMessage');

export function hideJoinGame() {
    trigger(mod.id, "HideJoinGameMenu");
}

export function setIntVal(name: string, value: any) {
    trigger(mod.id, name, parseInt(value));
}

export function joinGame() {
    trigger(mod.id, "JoinGame");
}

export const JoinGameSettings = () => {
    const GameOptionsCSS = getModule('game-ui/menu/components/shared/game-options/game-options.module.scss', 'classes');

    const ipAddressValue = useValue(ipAddress);
    const portValue = useValue(port);
    const usernameValue = useValue(username);
    const status = useValue(playerStatus);
    const errMsg = useValue(joinErrorMessage);

    const enabled = status == "INACTIVE";

    let messages = <></>;
    if (errMsg.length > 0) {
        messages = <LocalizedString id={"CS2M.UI.JoinError.Intro"}/>;
    }
    for (let i = 0; i < errMsg.length; i++) {
        let err = errMsg[i];
        let message;
        if (err.startsWith("precondition:")) {
            err = err.substring(12);
            switch (err) {
                case "GAME_VERSION_MISMATCH":
                case "MOD_VERSION_MISMATCH":
                case "DLCS_MISMATCH":
                case "MODS_MISMATCH": {
                    const err_id = "CS2M.UI.JoinError." + err;
                    message = <LocalizedString id={err_id} args={{SERVER: errMsg[++i], CLIENT: errMsg[++i]}}/>;
                    break;
                }
                case "USERNAME_NOT_AVAILABLE":
                case "PASSWORD_INCORRECT": {
                    const err_id = "CS2M.UI.JoinError." + err;
                    message = <LocalizedString id={err_id}/>;
                    break;
                }
            }
        } else {
            message = <LocalizedString id={err}/>;
        }
        messages = <>{messages}{message}<br/></>
    }

    const focusChange = () => {
    };
    return (
        <FocusBoundary onFocusChange={focusChange}>
            <div className={GameOptionsCSS.mainRow}>
                <div className={GameOptionsCSS.optionsColumn}>
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
                        <InputField label={"CS2M.UI.Username"} value={usernameValue} disabled={!enabled}
                                    onChange={(val: any) => {
                                        setVal("SetUsername", val)
                                    }}></InputField>
                    </NavigationScope>
                </div>
                <div className={GameOptionsCSS.infoColumn}>
                    {messages}
                </div>
            </div>
        </FocusBoundary>
    );
}

export const JoinGameMenu = () => {
    const SubScreen = getModule('game-ui/menu/components/shared/sub-screen/sub-screen.tsx', 'SubScreen');
    const LoadGameScreenCSS = getModule('game-ui/menu/components/load-game-screen/load-game-screen.module.scss', 'classes');
    const DetailSection = getModule('game-ui/menu/components/shared/detail-section/detail-section.tsx', 'DetailSection');
    const DetailSectionCSS = getModule('game-ui/menu/components/shared/detail-section/detail-section.module.scss', 'classes');
    const Field = getModule('game-ui/menu/components/shared/detail-section/detail-section.tsx', 'Field');
    const InputActionConsumer = getModule('game-ui/common/input-events/input-action-consumer.tsx', 'InputActionConsumer');
    const AutoNavigationScope = getModule('game-ui/common/focus/auto-navigation-scope.tsx', 'AutoNavigationScope');
    const SaveListCSS = getModule('game-ui/menu/components/load-game-screen/save-list/save-list.module.scss', 'classes');
    const FooterButton = getModule('game-ui/menu/components/shared/detail-section/detail-section.tsx', 'FooterButton');

    let visible: boolean = useValue(joinMenuVisible);
    const modSupports = useValue(modSupport);
    const status = useValue(playerStatus);
    const dlDone = useValue(downloadDone);
    const dlRemaining = useValue(downloadRemaining);

    let enabled = status == "INACTIVE";

    const {translate} = useLocalization();

    const actions = {};

    let details = [];
    let detailsTitle;
    if (enabled) {
        detailsTitle = translate("CS2M.UI.Compatibility");
        for (let support of modSupports) {
            let support_str = translate("CS2M.UI.Compatibility[" + support.support + "]", support.support);
            if (support.client_side) {
                support_str = translate("CS2M.UI.ClientSide");
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
            details.push(<div style={{color: color}}><Field label={support.name}>{support_str}</Field></div>);
        }
    } else {
        let status_str = translate("CS2M.UI.JoinStatus[" + status + "]", status);
        if (status == "DOWNLOADING_MAP") {
            let dlTotal = dlDone + dlRemaining;
            let doneMib = dlDone / 1024 / 1024;
            let totalMib = dlTotal / 1024 / 1024;

            let dlValues;
            if (dlTotal == 0) {
                dlTotal = 100;
            } else {
                dlValues = <>
                    <LocalizedNumber
                        unit={Unit.FloatSingleFraction}
                        signed={false}
                        value={doneMib}
                    />&nbsp;MiB
                    &nbsp;/&nbsp;
                    <LocalizedNumber
                        unit={Unit.FloatSingleFraction}
                        signed={false}
                        value={totalMib}
                    />&nbsp;MiB
                </>;
            }

            let download_state = <>
                {dlValues}&nbsp;(<LocalizedPercentage value={dlDone} max={dlTotal}/>)
            </>;
            detailsTitle = <>
                <span style={{whiteSpace: "nowrap"}}>
                    {status_str} {download_state}
                </span>
            </>;
        } else {
            detailsTitle = <>{status_str}</>;
        }
    }

    let footer = <>
        <FooterButton disabled={!enabled} onSelect={joinGame}>{translate("CS2M.UI.JoinGame")}</FooterButton>
    </>;

    let content;
    if (visible) {
        content = (
            <SubScreen title={translate("CS2M.UI.Multiplayer")} onClose={hideJoinGame}>
                <InputActionConsumer actions={actions}>
                    <div className={LoadGameScreenCSS.content}>
                        <AutoNavigationScope>
                            <div className={LoadGameScreenCSS.stepContainer}>
                                <div className={SaveListCSS.saveList + " " + LoadGameScreenCSS.step}>
                                    <div className={DetailSectionCSS.title}>{translate("CS2M.UI.JoinGame")}</div>
                                    <JoinGameSettings></JoinGameSettings>
                                </div>
                            </div>
                            <DetailSection title={detailsTitle} className={LoadGameScreenCSS.detail} content={details}
                                           footer={footer}>
                            </DetailSection>
                        </AutoNavigationScope>
                    </div>
                </InputActionConsumer>
            </SubScreen>
        );
    }

    return (
        <>
            {content}
        </>
    );
}
