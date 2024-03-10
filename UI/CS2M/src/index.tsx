import { ModRegistrar } from "cs2/modding";
import {JoinGameMenu, MainMenuCSMExtend} from "mods/main-menu";

const register: ModRegistrar = (moduleRegistry) => {
    moduleRegistry.registry.forEach((v, k) => {
        console.log(k, v);
    });

    moduleRegistry.extend('game-ui/menu/components/main-menu-screen/main-menu-screen.tsx', 'MainMenuNavigation', MainMenuCSMExtend);
    //moduleRegistry.append('Menu', JoinGameMenu);
}

export default register;
