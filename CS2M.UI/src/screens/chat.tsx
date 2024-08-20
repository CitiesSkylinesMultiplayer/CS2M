import {getModule} from "cs2/modding";
import {Panel} from "cs2/ui";

export const ChatPanel = (e: any) => {
    const LightOpaqueTheme = getModule('game-ui/common/panel/themes/light-opaque.module.scss', 'classes');
    const LifePathPanelStyle = getModule('game-ui/game/components/life-path/life-path-panel/life-path-panel.module.scss', 'classes');
    const TransitionSounds = getModule('game-ui/common/animations/transition-sounds.tsx', 'panelTransitionSounds');
    const SocialPanelLayout = getModule('game-ui/game/components/game-panel-renderer.tsx', 'SocialPanelLayout');

  
    return (
        <SocialPanelLayout>
            <Panel theme={LightOpaqueTheme} header="Chat" transitionSounds={TransitionSounds}
                   className={LifePathPanelStyle.lifePathPanel} contentClassName={LifePathPanelStyle.content}
                   onClose={e.onClose}>
                
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
            <IconButton src='Media/Game/Icons/Communications.svg' selected={ChatPanelState.visible}
                        onSelect={ChatPanelState.toggle} theme={RightMenuButtonStyle}
                        className={RightMenuButtonStyle.toggleStates}>
                
            </IconButton>
        </div>
    );
};
