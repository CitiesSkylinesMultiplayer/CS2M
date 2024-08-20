import {ModuleRegistryExtend} from "cs2/modding";
import {MenuButton} from "cs2/ui";
import {trigger} from "cs2/api";
import mod from "../../mod.json";
import {JoinGameMenu} from "../screens/join-game-menu";
import {HostGameMenu} from "../screens/host-game-menu";

export function showMultiplayerMenu() {
    trigger(mod.id, "ShowMultiplayerMenu");
}

// Extend TransitionGroupCoordinator as it is the only place we can put the JoinGameMenu
// Only extend it if it has 5 children => Main Menu or Pause Menu
export const MenuUIExtensions : ModuleRegistryExtend = (Component) => {
    return (props) => {
        const {children, ...otherProps} = props || {};
        let menus;
        if (children && children.length == 5) {
            menus = 
                <>
                    <JoinGameMenu></JoinGameMenu>
                    <HostGameMenu></HostGameMenu>
                </>;
        }
        return (
            <Component {...otherProps}>
                {children}
                {menus}
            </Component>
        )
    };
}

// Extend LabeledIconButton to place another button before every "ArrowRight"
// This covers both the main menu (where we could also just extend the menu), but also the in-game pause menu
// where it is not so easy to add another button.
export const PauseMenuCSMExtend : ModuleRegistryExtend = (Component) => {
    return (props) => {
        const { children, ...otherProps } = props || {};
        if (props.src == 'Media/Glyphs/ArrowRight.svg') {
            return (
                <>
                    <MenuButton onClick={showMultiplayerMenu}>Multiplayer</MenuButton>
                    <Component {...otherProps}>
                        {children}
                    </Component>
                </>

            );
        } else {
            return (
                <Component {...otherProps}>
                    {children}
                </Component>
            );
        }
    };
}
