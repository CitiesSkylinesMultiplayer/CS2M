import {getModule} from "cs2/modding";
import {bindValue, trigger, useValue} from "cs2/api";
import mod from "../../mod.json";
import {AutoNavigationScope, FocusBoundary, NavigationDirection, NavigationScope} from "cs2/input";

export const joinMenuVisible = bindValue<boolean>(mod.id, 'JoinMenuVisible');
export const modSupport = bindValue<Array<any>>(mod.id, 'modSupport');

export const ipAddress = bindValue<string>(mod.id, 'JoinIpAddress');
export const port = bindValue<number>(mod.id, 'JoinPort');
export const username = bindValue<string>(mod.id, 'JoinUsername');
export const joinGameEnabled = bindValue<boolean>(mod.id, 'JoinGameEnabled');

export function hideJoinGame() {
    trigger(mod.id, "HideJoinGameMenu");
}

export const InputField = (props : any) => {
    const ColumnFieldCSS = getModule('game-ui/menu/components/shared/game-options/field/column-field.module.scss', 'classes');
    const CityNameFieldCSS = getModule('game-ui/menu/components/shared/game-options/city-name-field/city-name-field.module.scss', 'classes');
    const OptionField = getModule('game-ui/menu/widgets/field/field.tsx', 'OptionField');
    const EllipsisTextInput = getModule('game-ui/common/input/text/ellipsis-text-input/ellipsis-text-input.tsx', 'EllipsisTextInput');

    const label = props.label ?? "";

    return (
        <OptionField id={props.id} label={label} theme={ColumnFieldCSS}>
            <div className={CityNameFieldCSS.cityNameField}>
                <AutoNavigationScope direction={NavigationDirection.Horizontal}>
                    <EllipsisTextInput value={props.value} vkTitle={label} maxLength="85" onChange={props.onChange} disabled={props.disabled}>

                    </EllipsisTextInput>
                </AutoNavigationScope>
            </div>
        </OptionField>
    )
}

export function setVal(name: string, event: any) {
    trigger(mod.id, name, event.target.value);
}

export function setIntVal(name: string, event: any) {
    trigger(mod.id, name, parseInt(event.target.value));
}

export function joinGame() {
    trigger(mod.id, "JoinGame");
}

export const JoinGameSettings = () => {
    const GameOptionsCSS = getModule('game-ui/menu/components/shared/game-options/game-options.module.scss', 'classes');

    let ipAddressValue = useValue(ipAddress);
    let portValue = useValue(port);
    let usernameValue = useValue(username);
    let enabled = useValue(joinGameEnabled);

    const focusChange = () => {};
    return (
        <FocusBoundary onFocusChange={focusChange}>
            <div className={GameOptionsCSS.mainRow}>
                <div className={GameOptionsCSS.optionsColumn}>
                    <NavigationScope focused={null} onChange={() => {}}>
                        <InputField id="cs2m_ip" label="IP Address" value={ipAddressValue} disabled={!enabled}
                                    onChange={(val: any) => {setVal("SetJoinIpAddress", val)}}></InputField>
                        <InputField id="cs2m_port" label="Port" value={portValue} disabled={!enabled}
                                    onChange={(val : any) => {setIntVal("SetJoinPort", val)}}></InputField>
                        <InputField id="cs2m_user" label="Username" value={usernameValue} disabled={!enabled}
                                    onChange={(val: any) => {setVal("SetJoinUsername", val)}}></InputField>
                    </NavigationScope>
                </div>
                <div className={GameOptionsCSS.infoColumn}>

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

    const visible : boolean = useValue(joinMenuVisible);
    const modSupports  = useValue(modSupport);
    const enabled = useValue(joinGameEnabled);

    const actions = {};

    let details = [];
    let detailsTitle = "";
    if (enabled) {
        detailsTitle = "Compatibility Info";
        for (let support of modSupports) {
            let support_str = support.support;
            if (support.client_side) {
                support_str = "Client side";
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
        detailsTitle = "Joining game...";
    }

    let footer = <FooterButton disabled={!enabled} onSelect={joinGame}>Join Game</FooterButton>;

    let content;
    if (visible) {
        content = (
            <SubScreen title="Multiplayer" onClose={hideJoinGame}>
                <InputActionConsumer actions={actions}>
                    <div className={LoadGameScreenCSS.content}>
                        <AutoNavigationScope>
                            <div className={LoadGameScreenCSS.stepContainer}>
                                <div className={SaveListCSS.saveList + " " + LoadGameScreenCSS.step}>
                                    <div className={DetailSectionCSS.title}>Join Game</div>
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
