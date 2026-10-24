import {ModuleRegistryExtend} from "cs2/modding";
import {MenuButton} from "cs2/ui";
import {trigger} from "cs2/api";
import {useLocalization} from "cs2/l10n";
import mod from "../../mod.json";
import {JoinGameMenu} from "../screens/join-game-menu";
import {HostGameMenu} from "../screens/host-game-menu";

export function showMultiplayerMenu() {
    trigger(mod.id, "ShowMultiplayerMenu");
}

// MenuButton needs its label as plain text; a LocalizedString child renders empty there
function MultiplayerButton() {
    const {translate} = useLocalization();
    return <MenuButton onClick={showMultiplayerMenu}>{translate("CS2M.UI.Multiplayer", "Multiplayer")}</MenuButton>;
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
                    <MultiplayerButton/>
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
