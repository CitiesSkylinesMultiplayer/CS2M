import {getModule, ModRegistrar} from "cs2/modding";
import {MenuUIExtensions, PauseMenuCSMExtend} from "extends/main-menu";
import {JoinGameMenu} from "./screens/join-game-menu";
import {HostGameMenu} from "./screens/host-game-menu";
import {ChatPanel} from "./screens/chat";
import {CursorLabels} from "./screens/cursors";
import {MultiplayerHud} from "./screens/hud";

const register: ModRegistrar = (moduleRegistry) => {
    moduleRegistry.extend('game-ui/common/input/button/labeled-icon-button.tsx', 'LabeledIconButton', PauseMenuCSMExtend);
    moduleRegistry.add('cs2m/screens/join-game-menu.tsx', JoinGameMenu);
    moduleRegistry.add('cs2m/screens/host-game-menu.tsx', HostGameMenu);

    moduleRegistry.extend('game-ui/common/animations/transition-group-coordinator.tsx', 'TransitionGroupCoordinator', MenuUIExtensions);

    moduleRegistry.append('Game', CursorLabels);
    // The HUD shows the chat, so the old chat button (ChatIcon) is no longer added
    moduleRegistry.append('Game', MultiplayerHud);
    getModule('game-ui/game/components/game-panel-renderer.tsx', 'gamePanelComponents')['CS2M.UI.ChatPanel'] = ChatPanel;
}

export default register;
