import { bindValue, useValue, bindEvent, useMapValue } from "cs2/api";
import { getModule } from "cs2/modding";
import { Button, Panel, PanelSection, Scrollable } from "cs2/ui";
import { useLocalization } from "cs2/l10n";
import mod from "../../mod.json";
import { InputField } from "../util/input-field";
import { setVal } from "api";

const chatMessagesBinding = bindValue<Array<string>>(mod.id, 'ChatMessages', []);
const localChatMessageBinding = bindValue<string>(mod.id, 'LocalChatMessage');

interface Message {
    user: string;
    timestamp: Date;
    text: string;
}

export const ChatPanel = (e: any) => {
    const LightOpaqueTheme = getModule('game-ui/common/panel/themes/light-opaque.module.scss', 'classes');
    const LifePathPanelStyle = getModule('game-ui/game/components/life-path/life-path-panel/life-path-panel.module.scss', 'classes');
    const TransitionSounds = getModule('game-ui/common/animations/transition-sounds.tsx', 'panelTransitionSounds');
    const SocialPanelLayout = getModule('game-ui/game/components/game-panel-renderer.tsx', 'SocialPanelLayout');

    const { translate } = useLocalization();

    const chatMessages = useValue(chatMessagesBinding);
    const localChatMessage = useValue(localChatMessageBinding);

    return (
        <SocialPanelLayout>
            <Panel theme={LightOpaqueTheme}
                header={translate("CS2M.UI.ChatPanel.Header")}
                transitionSounds={TransitionSounds}
                className={LifePathPanelStyle.lifePathPanel}
                contentClassName={LifePathPanelStyle.content}
                onClose={e.onClose}>
                <Scrollable>
                    {chatMessages?.map(message => (
                        <div>
                            <p>{message}</p>
                        </div>
                    ))}
                </Scrollable>

                <PanelSection>
                    <InputField label={"CS2M.UI.ChatPanel.ChatMessageInput"}
                        value={localChatMessage}
                        onChange={(val: any) => { setVal("SetLocalChatMessage", val) }} />
                    <Button>Send</Button>
                </PanelSection>
            </Panel>
        </SocialPanelLayout>
    );
};

export const ChatIcon = () => {
    const IconButton = getModule('game-ui/common/input/button/icon-button.tsx', 'IconButton');
    const useGamePanelState = getModule('game-ui/common/hooks/use-game-panel-state.tsx', 'useGamePanelState');
    const RightMenuButtonStyle = getModule('game-ui/game/components/right-menu/right-menu-button.module.scss', 'classes');
    const RightMenuStyle = getModule('game-ui/game/components/right-menu/right-menu.module.scss', 'classes');

    const ChatPanelState = useGamePanelState('CS2M.UI.ChatPanel');

    return (
        <div className={RightMenuStyle.item}>
            <IconButton src='Media/Game/Icons/Communications.svg'
                selected={ChatPanelState.visible}
                onSelect={ChatPanelState.toggle} theme={RightMenuButtonStyle}
                className={RightMenuButtonStyle.toggleStates} >
            </IconButton>
        </div>
    );
};
