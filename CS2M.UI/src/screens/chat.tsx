import {bindValue, trigger, useValue} from "cs2/api";
import {getModule} from "cs2/modding";
import {Button, Panel, PanelSection, Scrollable} from "cs2/ui";
import {useLocalization} from "cs2/l10n";
import {setVal} from "api";
import styles from "./chat.module.scss";
import mod from "../../mod.json";

interface Message {
    timestamp: string;
    user: string;
    text: string;
}

const chatMessagesBinding = bindValue<Array<Message>>(mod.id, 'ChatMessages', []);
const currentUsernameBinding = bindValue<string>(mod.id, 'CurrentUsername');
const localChatMessageBinding = bindValue<string>(mod.id, 'LocalChatMessage');

export function sendChatMessage() {
    trigger(mod.id, "SendChatMessage");
}

export const ChatPanel = (e: any) => {
    const LightOpaqueTheme = getModule('game-ui/common/panel/themes/light-opaque.module.scss', 'classes');
    const LifePathPanelStyle = getModule('game-ui/game/components/life-path/life-path-panel/life-path-panel.module.scss', 'classes');
    const TransitionSounds = getModule('game-ui/common/animations/transition-sounds.tsx', 'panelTransitionSounds');
    const SocialPanelLayout = getModule('game-ui/game/components/game-panel-renderer.tsx', 'SocialPanelLayout');
    const StringInputField = getModule('game-ui/editor/widgets/fields/string-input-field.tsx', 'StringInputField');

    const {translate} = useLocalization();

    const chatMessages = useValue(chatMessagesBinding);
    const currentUsername = useValue(currentUsernameBinding);
    const localChatMessage = useValue(localChatMessageBinding);

    return (
        <SocialPanelLayout className={styles.socialPanelLayoutContent}>
            <Panel theme={LightOpaqueTheme}
                   header={translate("CS2M.UI.ChatPanel.Header")}
                   transitionSounds={TransitionSounds}
                   className={LifePathPanelStyle.lifePathPanel}
                   contentClassName={LifePathPanelStyle.content}
                   onClose={e.onClose}>
                <Scrollable autoScroll={true} smooth={true}>
                    {chatMessages?.map(message => (
                        <div
                            className={`${message.user === currentUsername ? "styles.chatBubble-right" : "styles.chatBubble"}`}>
                            <div className={styles.content}>
                                <div className={styles.header}>
                                    <button className={styles.username}>{message.user}</button>
                                    <div className={styles.timestamp}>{message.timestamp}</div>
                                </div>

                                <div className={styles.message}>
                                    <p cohinline="cohinline">
                                        {message.text}
                                    </p>
                                </div>
                            </div>
                        </div>
                    ))}
                </Scrollable>

                <PanelSection>
                    <StringInputField
                        className={styles.messageInput}
                        value={localChatMessage}
                        placeholder={translate("CS2M.UI.ChatPanel.ChatMessageInput")}
                        onChange={(val: any) => {
                            setVal("SetLocalChatMessage", val)
                        }}/>
                    <Button onClick={sendChatMessage}>
                        {translate("CS2M.UI.ChatPanel.SendMessage")}
                    </Button>
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
                        onSelect={ChatPanelState.toggle}
                        theme={RightMenuButtonStyle}
                        className={RightMenuButtonStyle.toggleStates}>
            </IconButton>
        </div>
    );
};
