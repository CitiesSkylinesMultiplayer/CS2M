import { ModRegistrar } from "cs2/modding";
import {MenuUIExtensions, PauseMenuCSMExtend} from "extends/main-menu";
import {JoinGameMenu} from "./screens/join-game-menu";

const register: ModRegistrar = (moduleRegistry) => {
    moduleRegistry.extend('game-ui/common/input/button/labeled-icon-button.tsx', 'LabeledIconButton', PauseMenuCSMExtend);
    moduleRegistry.add('cs2m/screens/join-game-menu.tsx', JoinGameMenu);

    moduleRegistry.extend('game-ui/common/animations/transition-group-coordinator.tsx', 'TransitionGroupCoordinator', MenuUIExtensions);
}

export default register;
