import { getModule } from "cs2/modding";
import { bindValue, trigger, useValue } from "cs2/api";
import mod from "../../mod.json";
import { FocusBoundary, NavigationScope } from "cs2/input";
import { useLocalization } from "cs2/l10n";
import { InputField,InputFieldWide } from "../util/input-field";

export const joinMenuVisible = bindValue<boolean>(mod.id, 'JoinMenuVisible');
export const modSupport = bindValue<Array<any>>(mod.id, 'modSupport');

export const ipAddress = bindValue<string>(mod.id, 'JoinIpAddress');
export const port = bindValue<number>(mod.id, 'JoinPort');
export const username = bindValue<string>(mod.id, 'Username');
export const joinGameEnabled = bindValue<boolean>(mod.id, 'JoinGameEnabled');
export const hostGameEnabled = bindValue<boolean>(mod.id, 'HostGameEnabled');
export const chatSendEnabled = bindValue<boolean>(mod.id, 'ChatSendEnabled');

export const listNetworkStates = bindValue<string>(mod.id, 'uiNetworkStates');
export const playerMessage = bindValue<string>(mod.id, 'playerMessage');
export const stopSrvVisible = bindValue<boolean>(mod.id, 'stopSrvVisible');

export function hideJoinGame() {
    trigger(mod.id, "HideJoinGameMenu");
}

export function setVal(name: string, value: any) {
    trigger(mod.id, name, value);
}

export function setIntVal(name: string, value: any) {
    trigger(mod.id, name, parseInt(value));
}

export function joinGame() {
    trigger(mod.id, "JoinGame");
}

export function hostGame() {
    trigger(mod.id, "HostGame");
}

export function sendMessage() {
    trigger(mod.id, "SendMessage");
}

export function stopServer() {
    trigger(mod.id, "StopServer");
}

export const JoinGameSettings = () => {
    const GameOptionsCSS = getModule('game-ui/menu/components/shared/game-options/game-options.module.scss', 'classes');
    const FooterButton = getModule('game-ui/menu/components/shared/detail-section/detail-section.tsx', 'FooterButton');

    let ipAddressValue = useValue(ipAddress);
    let portValue = useValue(port);
    let usernameValue = useValue(username);
    let enabled = useValue(joinGameEnabled);
    let playerMessageVal = useValue(playerMessage);

    let outNetworkStates = useValue(listNetworkStates);
    let chatSendEnabledVal = useValue(chatSendEnabled);

    const { translate } = useLocalization();

    const focusChange = () => { };
    return (
        <FocusBoundary onFocusChange={focusChange}>
            <div className={GameOptionsCSS.mainRow}>
                <div className={GameOptionsCSS.optionsColumn}>
                    <NavigationScope focused={null} onChange={() => { }}>
                        <InputField label={"CS2M.UI.IPAddress"} value={ipAddressValue} disabled={!enabled}
                            onChange={(val: any) => { setVal("SetJoinIpAddress", val) }}></InputField>
                        <InputField label={"CS2M.UI.Port"} value={portValue} disabled={!enabled}
                            onChange={(val: any) => { setIntVal("SetJoinPort", val) }}></InputField>
                        <InputField label={"CS2M.UI.Username"} value={usernameValue} disabled={!enabled}
                            onChange={(val: any) => { setVal("SetUsername", val) }}></InputField>

                    </NavigationScope>
                </div>
                <div className={GameOptionsCSS.infoColumn} style={{ whiteSpace: "pre-wrap" }}>
                    <div style={{height: "90%"}}>
                        {outNetworkStates}
                    </div>
                    <hr />
                    <InputFieldWide disabled={!enabled} value={playerMessageVal}
                        onChange={(val: any) => { setVal("SetMsgFromUsr", val) }}></InputFieldWide>
                    <FooterButton disabled={!chatSendEnabledVal} 
                        onSelect={sendMessage}>{translate("CS2M.UI.sendMessage", "Send")}</FooterButton>
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

    const visible: boolean = useValue(joinMenuVisible);
    const modSupports = useValue(modSupport);
    const enabled = useValue(joinGameEnabled);
    const enabled2 = useValue(hostGameEnabled);

    const stopSrvVis = useValue(stopSrvVisible);

    const { translate } = useLocalization();

    const actions = {};

    let details = [];
    let detailsTitle;
    if (enabled) {
        detailsTitle = translate("CS2M.UI.Compatibility", "Compatibility");
        for (let support of modSupports) {
            let support_str = translate("CS2M.UI.Compatibility[" + support.support + "]", support.support);
            if (support.client_side) {
                support_str = translate("CS2M.UI.ClientSide", "Client Side");
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
            details.push(<div style={{ color: color }}><Field label={support.name}>{support_str}</Field></div>);
        }
    } else {
        detailsTitle = translate("CS2M.UI.JoiningGame", "Joining Game");
    }

    let footer = <>
        {!stopSrvVis && <FooterButton disabled={!enabled2} onSelect={hostGame}>{translate("CS2M.UI.HostChat", "Host Chat")}</FooterButton>}
        <FooterButton disabled={!enabled} onSelect={joinGame}>{translate("CS2M.UI.JoinGame", "Join Game")}</FooterButton>
        {stopSrvVis && (<FooterButton onSelect={stopServer}>{translate("CS2M.UI.StopServer", "Stop Server")}</FooterButton>)}
    </>;

    let content;
    if (visible) {
        content = (
            <SubScreen title={translate("CS2M.UI.Multiplayer", "Multiplayer")} onClose={hideJoinGame}>
                <InputActionConsumer actions={actions}>
                    <div className={LoadGameScreenCSS.content}>
                        <AutoNavigationScope>
                            <div className={LoadGameScreenCSS.stepContainer}>
                                <div className={SaveListCSS.saveList + " " + LoadGameScreenCSS.step}>
                                    <div className={DetailSectionCSS.title}>{translate("CS2M.UI.JoinGame", "Join_Game")}</div>
                                    <JoinGameSettings></JoinGameSettings>
                                </div>
                            </div>
                            <DetailSection title={detailsTitle} className={LoadGameScreenCSS.detail} content={details} footer={footer}>
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
