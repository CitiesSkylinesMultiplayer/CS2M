import {getModule} from "cs2/modding";
import {bindValue, trigger, useValue} from "cs2/api";
import mod from "../../mod.json";
import {FocusBoundary, NavigationScope} from "cs2/input";
import {useLocalization} from "cs2/l10n";
import {InputField} from "../util/input-field";
import {setVal} from "../api";

export const hostMenuVisible = bindValue<boolean>(mod.id, 'HostMenuVisible');
export const modSupport = bindValue<Array<any>>(mod.id, 'modSupport');
export const port = bindValue<number>(mod.id, 'HostPort');
export const username = bindValue<string>(mod.id, 'Username');
export const hostGameEnabled = bindValue<boolean>(mod.id, 'HostGameEnabled');
export const playerStatus = bindValue<string>(mod.id, 'PlayerStatus');

export function hideHostGame() {
    trigger(mod.id, "HideHostGameMenu");
}

export function setIntVal(name: string, value: any) {
    trigger(mod.id, name, parseInt(value));
}

export function hostGame() {
    trigger(mod.id, "HostGame");
}

export const HostGameSettings = () => {
    const GameOptionsCSS = getModule('game-ui/menu/components/shared/game-options/game-options.module.scss', 'classes');

    let portValue = useValue(port);
    let usernameValue = useValue(username);
    let enabled = useValue(hostGameEnabled);

    const focusChange = () => {};
    return (
        <FocusBoundary onFocusChange={focusChange}>
            <div className={GameOptionsCSS.mainRow}>
                <div className={GameOptionsCSS.optionsColumn}>
                    <NavigationScope focused={null} onChange={() => {}}>
                        <InputField label={"CS2M.UI.Port"} value={portValue} disabled={!enabled}
                                    onChange={(val : any) => {setIntVal("SetHostPort", val)}}></InputField>
                        <InputField label={"CS2M.UI.Username"} value={usernameValue} disabled={!enabled}
                                    onChange={(val: any) => {setVal("SetUsername", val)}}></InputField>
                    </NavigationScope>
                </div>
                <div className={GameOptionsCSS.infoColumn}>

                </div>
            </div>
        </FocusBoundary>
    );
}

export const HostGameMenu = () => {
    const SubScreen = getModule('game-ui/menu/components/shared/sub-screen/sub-screen.tsx', 'SubScreen');
    const LoadGameScreenCSS = getModule('game-ui/menu/components/load-game-screen/load-game-screen.module.scss', 'classes');
    const DetailSection = getModule('game-ui/menu/components/shared/detail-section/detail-section.tsx', 'DetailSection');
    const DetailSectionCSS = getModule('game-ui/menu/components/shared/detail-section/detail-section.module.scss', 'classes');
    const Field = getModule('game-ui/menu/components/shared/detail-section/detail-section.tsx', 'Field');
    const InputActionConsumer = getModule('game-ui/common/input-events/input-action-consumer.tsx', 'InputActionConsumer');
    const AutoNavigationScope = getModule('game-ui/common/focus/auto-navigation-scope.tsx', 'AutoNavigationScope');
    const SaveListCSS = getModule('game-ui/menu/components/load-game-screen/save-list/save-list.module.scss', 'classes');
    const FooterButton = getModule('game-ui/menu/components/shared/detail-section/detail-section.tsx', 'FooterButton');

    const visible : boolean = useValue(hostMenuVisible);
    const modSupports  = useValue(modSupport);
    const enabled = useValue(hostGameEnabled);
    const status = useValue(playerStatus);

    const { translate } = useLocalization();

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
        detailsTitle = status ?? translate("CS2M.UI.StartingServer");
    }

    let footer = <FooterButton disabled={!enabled} onSelect={hostGame}>{translate("CS2M.UI.StartServer")}</FooterButton>;

    let content;
    if (visible) {
        content = (
            <SubScreen title={translate("CS2M.UI.Multiplayer")} onClose={hideHostGame}>
                <InputActionConsumer actions={actions}>
                    <div className={LoadGameScreenCSS.content}>
                        <AutoNavigationScope>
                            <div className={LoadGameScreenCSS.stepContainer}>
                                <div className={SaveListCSS.saveList + " " + LoadGameScreenCSS.step}>
                                    <div className={DetailSectionCSS.title}>{translate("CS2M.UI.HostGame")}</div>
                                    <HostGameSettings></HostGameSettings>
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
